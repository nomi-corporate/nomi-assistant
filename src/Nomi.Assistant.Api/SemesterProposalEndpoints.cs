using System.Text.Json;
using Nomi.Assistant.Application;

namespace Nomi.Assistant.Api;

/// <summary>
/// TEC-003 §18 (onboarding proposals), MVP subset: POST /api/v1/semester-proposals turns
/// "Tenho Cálculo com a Ana e Física com o Bruno" into subjects + professors for review.
/// It never creates anything: the Mobile creates them in the Academic after confirmation.
/// Shares the per-user rate limit with commitment proposals.
/// </summary>
public static class SemesterProposalEndpoints
{
    public static IEndpointRouteBuilder MapSemesterProposalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/semester-proposals", CreateAsync);
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(HttpRequest request, JsonElement input,
        IIdentityUserResolver identity, IAcademicContextClient academic, ISemesterInterpreter interpreter,
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
            return CommitmentProposalEndpoints.Invalid("invalid_body", "Envie somente texto, idioma e fuso.");
        var text = CommitmentProposalEndpoints.Read(input, "text")?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > CommitmentProposalEndpoints.MaxTextLength)
            return CommitmentProposalEndpoints.Invalid("invalid_text",
                $"Descreva suas matérias em até {CommitmentProposalEndpoints.MaxTextLength} caracteres.");
        if (CommitmentProposalEndpoints.Read(input, "locale") != "pt-BR")
            return CommitmentProposalEndpoints.Invalid("unsupported_locale", "Somente pt-BR é suportado.");
        var timeZoneId = CommitmentProposalEndpoints.Read(input, "timeZone");
        if (string.IsNullOrWhiteSpace(timeZoneId) || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone))
            return CommitmentProposalEndpoints.Invalid("invalid_time_zone", "Informe um fuso IANA válido.");

        if (!limiter.TryAcquire(auth.UserId!.Value))
            return Results.Problem(statusCode: 429, title: "too_many_proposals",
                detail: "Muitas interpretações em pouco tempo. Aguarde alguns minutos ou cadastre manualmente.");

        var context = await academic.GetContextAsync(authorization, ct);
        var utcNow = clock.GetUtcNow();
        SemesterDraft draft;
        try
        {
            draft = await interpreter.InterpretAsync(
                new InterpretationInput(text, zone.Id, TimeZoneInfo.ConvertTime(utcNow, zone), context), ct);
        }
        catch (InterpreterUnavailableException error)
        {
            loggers.CreateLogger("Nomi.Assistant.SemesterProposals").LogWarning("Interpretation unavailable: {Reason} ({Inner})",
                error.Message, error.InnerException?.GetType().Name);
            return Results.StatusCode(503);
        }

        var proposal = SemesterProposalBuilder.Build(draft, context, utcNow);
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        return Results.Json(new
        {
            id = proposal.Id,
            version = proposal.Version,
            status = proposal.Status,
            message = proposal.Message,
            subjects = proposal.Subjects.Select(s => new
            {
                name = s.Name,
                code = s.Code,
                professorName = s.ProfessorName,
                existingSubjectId = s.ExistingSubjectId,
                existingProfessorId = s.ExistingProfessorId,
            }),
            questions = proposal.Questions.Select(q => new { field = q.Field, message = q.Message }),
            expiresAt = proposal.ExpiresAt,
        }, statusCode: StatusCodes.Status201Created);
    }
}
