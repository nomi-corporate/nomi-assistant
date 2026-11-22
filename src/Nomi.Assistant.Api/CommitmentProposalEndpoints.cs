using System.Text.Json;
using Nomi.Assistant.Application;

namespace Nomi.Assistant.Api;

/// <summary>
/// TEC-003 §7 — MVP subset: POST /api/v1/commitment-proposals interprets text into a proposal.
/// Proposals are not persisted yet: clarification (messages) and discard (DELETE) are answered
/// by the user correcting the form in the Mobile, and an Idempotency-Key is accepted but not stored.
/// Creating a proposal never creates a commitment; the Academic revalidates everything.
/// </summary>
public static class CommitmentProposalEndpoints
{
    public const int MaxTextLength = 4000;

    public static IEndpointRouteBuilder MapCommitmentProposalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/commitment-proposals", CreateAsync);
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(HttpRequest request, JsonElement input,
        IIdentityUserResolver identity, IAcademicContextClient academic, ICommitmentInterpreter interpreter,
        ProposalRateLimiter limiter, TimeProvider clock, ILoggerFactory loggers, CancellationToken ct)
    {
        var authorization = request.Headers.Authorization.ToString();
        var auth = await identity.ResolveAsync(authorization, ct);
        if (auth.Status == IdentityResolutionStatus.Unauthorized) return Results.Unauthorized();
        if (auth.Status != IdentityResolutionStatus.Authenticated || !interpreter.IsConfigured) return Results.StatusCode(503);

        if (request.Headers.TryGetValue("Idempotency-Key", out var key) && key.ToString().Trim().Length is 0 or > 128)
            return Results.Problem(statusCode: 400, title: "invalid_idempotency_key");
        if (input.ValueKind != JsonValueKind.Object
            || input.EnumerateObject().Any(x => x.Name is not ("text" or "locale" or "timeZone")))
            return Invalid("invalid_body", "Envie somente texto, idioma e fuso.");
        var text = Read(input, "text")?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxTextLength)
            return Invalid("invalid_text", $"Descreva o compromisso em até {MaxTextLength} caracteres.");
        if (Read(input, "locale") != "pt-BR") return Invalid("unsupported_locale", "Somente pt-BR é suportado.");
        var timeZoneId = Read(input, "timeZone");
        if (string.IsNullOrWhiteSpace(timeZoneId) || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone))
            return Invalid("invalid_time_zone", "Informe um fuso IANA válido.");

        if (!limiter.TryAcquire(auth.UserId!.Value))
            return Results.Problem(statusCode: 429, title: "too_many_proposals",
                detail: "Muitas interpretações em pouco tempo. Aguarde alguns minutos ou preencha o formulário.");

        var context = await academic.GetContextAsync(authorization, ct);
        var utcNow = clock.GetUtcNow();
        InterpretationDraft draft;
        try
        {
            draft = await interpreter.InterpretAsync(
                new InterpretationInput(text, zone.Id, TimeZoneInfo.ConvertTime(utcNow, zone), context), ct);
        }
        catch (InterpreterUnavailableException error)
        {
            // The text is personal data: log only the failure kind.
            loggers.CreateLogger("Nomi.Assistant.Proposals").LogWarning("Interpretation unavailable: {Reason} ({Inner})",
                error.Message, error.InnerException?.GetType().Name);
            return Results.StatusCode(503);
        }

        var proposal = CommitmentProposalBuilder.Build(draft, text, context, zone, utcNow);
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        return Results.Json(new
        {
            id = proposal.Id,
            version = proposal.Version,
            status = proposal.Status,
            message = proposal.Message,
            candidate = proposal.Candidate is null ? null : new
            {
                title = proposal.Candidate.Title,
                kind = proposal.Candidate.Kind,
                description = proposal.Candidate.Description,
                subjectId = proposal.Candidate.SubjectId,
                professorId = proposal.Candidate.ProfessorId,
                due = proposal.Candidate.Due is null ? null : (object)(proposal.Candidate.Due.Precision == "date"
                    ? new { precision = "date", localDate = proposal.Candidate.Due.LocalDate, timeZone = proposal.Candidate.Due.TimeZone }
                    : new
                    {
                        precision = "dateTime",
                        localDate = proposal.Candidate.Due.LocalDate,
                        localTime = proposal.Candidate.Due.LocalTime,
                        offset = proposal.Candidate.Due.Offset,
                        timeZone = proposal.Candidate.Due.TimeZone,
                    }),
            },
            questions = proposal.Questions.Select(q => new { field = q.Field, message = q.Message }),
            expiresAt = proposal.ExpiresAt,
        }, statusCode: StatusCodes.Status201Created);
    }

    internal static string? Read(JsonElement input, string name) =>
        input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    internal static IResult Invalid(string code, string detail) =>
        Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: code, detail: detail);
}
