using System.Text.RegularExpressions;
using System.Text.Json;
using Amazon.Runtime;
using Microsoft.EntityFrameworkCore;
using Nomi.Assistant.Application;
using Nomi.Assistant.Domain;
using Nomi.Assistant.Infrastructure.Persistence;

namespace Nomi.Assistant.Api;

public static class AudioTranscriptionEndpoints
{
    private const long MaxSize = 10_000_000;
    public static IEndpointRouteBuilder MapAudioTranscriptionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Without a database the API must still start (liveness 200, readiness 503); the
        // transcription routes then report the missing dependency instead of breaking startup.
        var services = endpoints.ServiceProvider.GetRequiredService<IServiceProviderIsService>();
        if (!services.IsService(typeof(AssistantDbContext)))
        {
            var unavailable = () => Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            endpoints.MapPost("/api/v1/audio-uploads", unavailable);
            endpoints.MapPost("/api/v1/transcriptions", unavailable);
            endpoints.MapGet("/api/v1/transcriptions/{id:guid}", unavailable);
            endpoints.MapDelete("/api/v1/transcriptions/{id:guid}", unavailable);
            return endpoints;
        }
        endpoints.MapPost("/api/v1/audio-uploads", CreateUploadAsync);
        endpoints.MapPost("/api/v1/transcriptions", StartAsync);
        endpoints.MapGet("/api/v1/transcriptions/{id:guid}", GetAsync);
        endpoints.MapDelete("/api/v1/transcriptions/{id:guid}", DeleteAsync);
        return endpoints;
    }

    private static async Task<IResult> CreateUploadAsync(HttpRequest request, JsonElement input,
        AssistantDbContext db, IIdentityUserResolver identity, IAudioTranscriptionGateway gateway,
        TimeProvider clock, CancellationToken ct)
    {
        var auth = await identity.ResolveAsync(request.Headers.Authorization.ToString(), ct);
        if (auth.Status == IdentityResolutionStatus.Unauthorized) return Results.Unauthorized();
        if (auth.Status != IdentityResolutionStatus.Authenticated || !gateway.IsConfigured) return Results.StatusCode(503);
        if (input.ValueKind != JsonValueKind.Object
            || input.EnumerateObject().Any(x => x.Name is not ("contentType" or "sizeBytes" or "durationSeconds" or "sha256" or "locale")))
            return Invalid("invalid_body", "Campos de áudio inválidos.");
        var contentType = ReadString(input, "contentType");
        var locale = ReadString(input, "locale");
        var sha = ReadString(input, "sha256");
        if (contentType != "audio/mp4" || locale != "pt-BR" || sha is null || !Regex.IsMatch(sha, "^[a-fA-F0-9]{64}$")
            || !input.TryGetProperty("sizeBytes", out var sizeValue) || sizeValue.ValueKind != JsonValueKind.Number || !sizeValue.TryGetInt64(out var size)
            || size is < 1 or > MaxSize || !input.TryGetProperty("durationSeconds", out var durationValue)
            || durationValue.ValueKind != JsonValueKind.Number || !durationValue.TryGetDecimal(out var duration) || duration is <= 0 or > 60)
            return Invalid("unsupported_audio", "Use M4A de até 10 MB e 60 segundos.");
        var owner = auth.UserId!.Value;
        var upload = new AudioUpload(owner, "audio/" + owner.ToString("N") + "/{id}.m4a", size, duration, sha.ToUpperInvariant(), clock.GetUtcNow());
        string url;
        try { url = await gateway.CreateUploadUrlAsync(upload, ct); }
        catch (AmazonClientException) { return Results.StatusCode(503); }
        db.AudioUploads.Add(upload);
        await db.SaveChangesAsync(ct);
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new { audioId = upload.Id, uploadUrl = url, method = "PUT",
            headers = new { contentType = "audio/mp4" }, expiresAt = upload.UploadExpiresAt });
    }

    private static async Task<IResult> StartAsync(HttpRequest request, JsonElement input,
        AssistantDbContext db, IIdentityUserResolver identity, IAudioTranscriptionGateway gateway,
        TimeProvider clock, CancellationToken ct)
    {
        var auth = await identity.ResolveAsync(request.Headers.Authorization.ToString(), ct);
        if (auth.Status == IdentityResolutionStatus.Unauthorized) return Results.Unauthorized();
        if (auth.Status != IdentityResolutionStatus.Authenticated || !gateway.IsConfigured) return Results.StatusCode(503);
        if (!request.Headers.TryGetValue("Idempotency-Key", out var rawKey) || string.IsNullOrWhiteSpace(rawKey))
            return Results.Problem(statusCode: 400, title: "idempotency_key_required");
        var key = rawKey.ToString().Trim();
        if (key.Length > 128) return Results.Problem(statusCode: 400, title: "invalid_idempotency_key");
        if (input.ValueKind != JsonValueKind.Object || input.EnumerateObject().Any(x => x.Name is not ("audioId" or "locale"))
            || ReadString(input, "locale") != "pt-BR" || !Guid.TryParse(ReadString(input, "audioId"), out var audioId))
            return Invalid("invalid_body", "Informe áudio e idioma válidos.");
        var owner = auth.UserId!.Value;
        var existing = await db.AudioTranscriptions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OwnerUserId == owner && x.IdempotencyKey == key, ct);
        if (existing is not null)
            return existing.AudioId == audioId ? Pending(request, existing) : Results.Problem(statusCode: 409, title: "idempotency_key_reused");
        var upload = await db.AudioUploads.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == audioId && x.OwnerUserId == owner, ct);
        if (upload is null) return Results.NotFound();
        if (upload.UploadExpiresAt <= clock.GetUtcNow()) return Results.StatusCode(410);
        var used = await db.AudioTranscriptions.AsNoTracking().AnyAsync(x => x.AudioId == audioId, ct);
        if (used) return Results.Problem(statusCode: 409, title: "audio_already_used");
        try {
            if (!await gateway.ValidateUploadAsync(upload, ct))
                return Invalid("unsupported_audio", "O arquivo não corresponde ao áudio informado ou excede o limite.");
        } catch (AmazonClientException) { return Results.StatusCode(503); }
        var transcription = new AudioTranscription(owner, audioId, key, clock.GetUtcNow());
        db.AudioTranscriptions.Add(transcription);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            existing = await db.AudioTranscriptions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.OwnerUserId == owner && x.IdempotencyKey == key, ct);
            return existing is not null && existing.AudioId == audioId
                ? Pending(request, existing) : Results.Problem(statusCode: 409, title: "audio_already_used");
        }
        try { await gateway.StartAsync(upload, transcription, ct); }
        catch (AmazonClientException)
        {
            transcription.Update("failed", errorCode: "TRANSCRIPTION_UNAVAILABLE");
            await db.SaveChangesAsync(ct);
            return Results.StatusCode(503);
        }
        return Pending(request, transcription);
    }

    private static async Task<IResult> GetAsync(HttpRequest request, Guid id, AssistantDbContext db,
        IIdentityUserResolver identity, IAudioTranscriptionGateway gateway, TimeProvider clock, CancellationToken ct)
    {
        var auth = await identity.ResolveAsync(request.Headers.Authorization.ToString(), ct);
        if (auth.Status == IdentityResolutionStatus.Unauthorized) return Results.Unauthorized();
        if (auth.Status != IdentityResolutionStatus.Authenticated || !gateway.IsConfigured) return Results.StatusCode(503);
        var item = await db.AudioTranscriptions.SingleOrDefaultAsync(x => x.Id == id && x.OwnerUserId == auth.UserId, ct);
        if (item is null) return Results.NotFound();
        if (item.ExpiresAt <= clock.GetUtcNow()) return Results.StatusCode(410);
        if (item.Status is "queued" or "processing")
        {
            try
            {
                var result = await gateway.GetResultAsync(item, ct);
                if (result.Status != item.Status || result.Text is not null)
                {
                    item.Update(result.Status, result.Text, result.ErrorCode);
                    await db.SaveChangesAsync(ct);
                }
            }
            catch (AmazonClientException) { return Results.StatusCode(503); }
        }
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new { id = item.Id, item.Status, item.Text, item.ErrorCode, item.ExpiresAt });
    }

    private static async Task<IResult> DeleteAsync(HttpRequest request, Guid id, AssistantDbContext db,
        IIdentityUserResolver identity, IAudioTranscriptionGateway gateway, CancellationToken ct)
    {
        var auth = await identity.ResolveAsync(request.Headers.Authorization.ToString(), ct);
        if (auth.Status == IdentityResolutionStatus.Unauthorized) return Results.Unauthorized();
        if (auth.Status != IdentityResolutionStatus.Authenticated || !gateway.IsConfigured) return Results.StatusCode(503);
        var item = await db.AudioTranscriptions.SingleOrDefaultAsync(x => x.Id == id && x.OwnerUserId == auth.UserId, ct);
        if (item is null) return Results.NotFound();
        if (item.Status != "cancelled")
        {
            item.Update("cancelled");
            await db.SaveChangesAsync(ct);
        }
        var upload = await db.AudioUploads.AsNoTracking().SingleAsync(x => x.Id == item.AudioId, ct);
        try { await gateway.DeleteArtifactsAsync(upload, item, ct); }
        catch (AmazonClientException) { return Results.StatusCode(503); }
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        return Results.NoContent();
    }

    private static IResult Pending(HttpRequest request, AudioTranscription item)
    {
        request.HttpContext.Response.Headers.Location = "/api/v1/transcriptions/" + item.Id;
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        return Results.Json(new { id = item.Id, item.Status, item.ExpiresAt }, statusCode: 202);
    }
    private static string? ReadString(JsonElement input, string name) =>
        input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static IResult Invalid(string code, string detail) => Results.Problem(statusCode: 422, title: code, detail: detail);
}
