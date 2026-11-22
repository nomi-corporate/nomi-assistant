using System.Globalization;
using System.Text;

namespace Nomi.Assistant.Application;

/// <summary>Raw, untrusted model output for "register my semester" (TEC-003 §18, onboarding proposals).</summary>
public sealed record SemesterDraftItem(string? Name, string? Code, string? ProfessorName);
public sealed record SemesterDraft(bool IsAcademic, IReadOnlyList<SemesterDraftItem> Items, IReadOnlyList<InterpretationQuestion> Questions);

public interface ISemesterInterpreter
{
    bool IsConfigured { get; }
    /// <exception cref="InterpreterUnavailableException">The model could not be reached or returned no usable output.</exception>
    Task<SemesterDraft> InterpretAsync(InterpretationInput input, CancellationToken ct);
}

public sealed record SemesterProposalItem(string Name, string? Code, string? ProfessorName, Guid? ExistingSubjectId, Guid? ExistingProfessorId);
public sealed record SemesterProposal(
    Guid Id,
    int Version,
    string Status,
    string? Message,
    IReadOnlyList<SemesterProposalItem> Subjects,
    IReadOnlyList<InterpretationQuestion> Questions,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Validates the model output into a proposal the Mobile can review. Nothing is created here:
/// the Mobile creates professors and subjects in the Academic only after the user confirms.
/// Existing subjects and professors of the user are matched by name to avoid duplicates.
/// </summary>
public static class SemesterProposalBuilder
{
    public const int MaxSubjects = 20;
    public const string UnsupportedMessage =
        "O Nomi cadastra aqui apenas as matérias e os professores do seu semestre. Tente algo como: “Tenho Cálculo com a Ana e Física com o Bruno”.";
    private static readonly string[] QuestionFields = ["subjects", "professor"];

    public static SemesterProposal Build(SemesterDraft draft, AcademicContext context, DateTimeOffset utcNow)
    {
        var id = Guid.NewGuid();
        var expiresAt = utcNow + CommitmentProposalBuilder.Lifetime;
        if (!draft.IsAcademic)
            return new SemesterProposal(id, 1, "unsupported", UnsupportedMessage, [], [], expiresAt);

        var subjects = new List<SemesterProposalItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in draft.Items)
        {
            if (subjects.Count >= MaxSubjects) break;
            var name = Clean(item.Name, 120);
            if (name is null || !seen.Add(Normalize(name))) continue;
            var code = Clean(item.Code, 30);
            var professorName = Clean(item.ProfessorName, 120);
            var existingSubject = context.Subjects.FirstOrDefault(s => Normalize(s.Name) == Normalize(name)
                || (code is not null && s.Code is not null && Normalize(s.Code) == Normalize(code)));
            var existingProfessor = professorName is null ? null : MatchProfessor(professorName, context.Professors);
            subjects.Add(new SemesterProposalItem(existingSubject?.Name ?? name, existingSubject?.Code ?? code,
                existingProfessor?.Name ?? professorName, existingSubject?.Id, existingProfessor?.Id));
        }

        var questions = new List<InterpretationQuestion>();
        if (subjects.Count == 0) questions.Add(new("subjects", "Quais matérias você está cursando neste semestre?"));
        foreach (var question in draft.Questions)
        {
            if (questions.Count >= 2) break;
            if (!QuestionFields.Contains(question.Field) || questions.Any(q => q.Field == question.Field)) continue;
            var message = Clean(question.Message, 200);
            if (message is not null) questions.Add(question with { Message = message });
        }
        return new SemesterProposal(id, 1, questions.Count == 0 ? "readyForReview" : "needsClarification",
            null, subjects, questions, expiresAt);
    }

    /// <summary>Exact normalized name, or a unique professor whose first name matches ("Ana" → "Ana Silva").</summary>
    private static AcademicProfessorContext? MatchProfessor(string spoken, IReadOnlyList<AcademicProfessorContext> professors)
    {
        var target = Normalize(StripTitle(spoken));
        var exact = professors.Where(p => Normalize(StripTitle(p.Name)) == target).ToArray();
        if (exact.Length == 1) return exact[0];
        if (target.Contains(' ')) return null;
        var byFirstName = professors.Where(p => Normalize(StripTitle(p.Name)).Split(' ')[0] == target).ToArray();
        return byFirstName.Length == 1 ? byFirstName[0] : null;
    }

    private static string StripTitle(string name)
    {
        var trimmed = name.Trim();
        foreach (var title in new[] { "professora ", "professor ", "profª ", "prof.ª ", "prof. ", "prof ", "dra. ", "dr. ", "dra ", "dr " })
            if (trimmed.StartsWith(title, StringComparison.OrdinalIgnoreCase)) return trimmed[title.Length..];
        return trimmed;
    }

    public static string Normalize(string value)
    {
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(c);
        return string.Join(' ', builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= max ? text : text[..max].TrimEnd();
    }
}
