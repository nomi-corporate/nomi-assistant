namespace Nomi.Assistant.Application;

/// <summary>User's academic context, read from the Academic API with the user's own token.</summary>
public sealed record AcademicSubjectContext(Guid Id, string Name, string? Code, IReadOnlyList<Guid> ProfessorIds);
public sealed record AcademicProfessorContext(Guid Id, string Name);
public sealed record AcademicContext(IReadOnlyList<AcademicSubjectContext> Subjects, IReadOnlyList<AcademicProfessorContext> Professors)
{
    public static readonly AcademicContext Empty = new([], []);
}

public interface IAcademicContextClient
{
    /// <summary>Returns the user's subjects and professors, or <see cref="AcademicContext.Empty"/> when Academic is unavailable.</summary>
    Task<AcademicContext> GetContextAsync(string authorizationHeader, CancellationToken ct);
}

/// <summary>Input given to the model. Everything here is data, never instructions.</summary>
public sealed record InterpretationInput(string Text, string TimeZone, DateTimeOffset ReferenceLocalNow, AcademicContext Context);

/// <summary>Raw, untrusted output of the model. Validated by <see cref="CommitmentProposalBuilder"/>.</summary>
public sealed record InterpretationDraft(
    bool IsAcademic,
    string? Title,
    string? Kind,
    string? Description,
    string? SubjectId,
    string? ProfessorId,
    string? LocalDate,
    string? LocalTime,
    IReadOnlyList<InterpretationQuestion> Questions);

public sealed record InterpretationQuestion(string Field, string Message);

public interface ICommitmentInterpreter
{
    bool IsConfigured { get; }
    /// <exception cref="InterpreterUnavailableException">The model could not be reached or returned no usable output.</exception>
    Task<InterpretationDraft> InterpretAsync(InterpretationInput input, CancellationToken ct);
}

public sealed class InterpreterUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public sealed record ProposalDue(string Precision, string LocalDate, string? LocalTime, string? Offset, string TimeZone);
public sealed record ProposalCandidate(string Title, string Kind, string? Description, Guid? SubjectId, Guid? ProfessorId, ProposalDue? Due);
public sealed record CommitmentProposal(
    Guid Id,
    int Version,
    string Status,
    string? Message,
    ProposalCandidate? Candidate,
    IReadOnlyList<InterpretationQuestion> Questions,
    DateTimeOffset ExpiresAt);
