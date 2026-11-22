using System.Globalization;

namespace Nomi.Assistant.Application;

/// <summary>
/// Turns the untrusted model draft into a proposal that respects TEC-003 §7: it never invents
/// IDs, only keeps subjects/professors that belong to the user, and asks for clarification
/// instead of guessing missing or invalid dates.
/// </summary>
public static class CommitmentProposalBuilder
{
    public const string UnsupportedMessage =
        "O Nomi registra apenas compromissos acadêmicos, como provas, trabalhos, entregas e aulas. Tente descrever um compromisso dos seus estudos.";
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);
    private static readonly string[] Kinds = ["exam", "assignment", "reminder"];
    private static readonly string[] QuestionFields = ["due", "subjectId", "title", "kind"];

    public static CommitmentProposal Build(InterpretationDraft draft, string originalText, AcademicContext context,
        TimeZoneInfo zone, DateTimeOffset utcNow)
    {
        var id = Guid.NewGuid();
        var expiresAt = utcNow + Lifetime;
        if (!draft.IsAcademic)
            return new CommitmentProposal(id, 1, "unsupported", UnsupportedMessage, null, [], expiresAt);

        var questions = new List<InterpretationQuestion>();
        var title = Clean(draft.Title, 160) ?? Clean(originalText, 160) ?? "Compromisso";
        var kind = Kinds.Contains(draft.Kind) ? draft.Kind! : "reminder";
        var description = Clean(draft.Description, 4000);

        var professor = Guid.TryParse(draft.ProfessorId, out var professorId)
            ? context.Professors.FirstOrDefault(p => p.Id == professorId) : null;
        var subject = Guid.TryParse(draft.SubjectId, out var subjectId)
            ? context.Subjects.FirstOrDefault(s => s.Id == subjectId) : null;

        if (subject is null && professor is not null)
        {
            // Automatic professor link: the professor identifies the subject when it is unambiguous.
            var taught = context.Subjects.Where(s => s.ProfessorIds.Contains(professor.Id)).ToArray();
            if (taught.Length == 1) subject = taught[0];
            else if (taught.Length > 1)
                questions.Add(new("subjectId", $"Qual matéria de {professor.Name} você quer vincular?"));
        }
        if (subject is not null && professor is not null && !subject.ProfessorIds.Contains(professor.Id))
            professor = null;
        if (subject is not null && professor is null && subject.ProfessorIds.Count == 1)
            professor = context.Professors.FirstOrDefault(p => p.Id == subject.ProfessorIds[0]);

        var due = ResolveDue(draft.LocalDate, draft.LocalTime, zone, utcNow, questions);

        foreach (var question in draft.Questions)
        {
            if (questions.Count >= 3) break;
            if (!QuestionFields.Contains(question.Field) || questions.Any(q => q.Field == question.Field)) continue;
            if (question.Field == "due" && due is not null) continue;
            if (question.Field == "subjectId" && subject is not null) continue;
            var message = Clean(question.Message, 200);
            if (message is not null) questions.Add(question with { Message = message });
        }

        var candidate = new ProposalCandidate(title, kind, description, subject?.Id, professor?.Id, due);
        return new CommitmentProposal(id, 1, questions.Count == 0 ? "readyForReview" : "needsClarification",
            null, candidate, questions, expiresAt);
    }

    private static ProposalDue? ResolveDue(string? rawDate, string? rawTime, TimeZoneInfo zone, DateTimeOffset utcNow,
        List<InterpretationQuestion> questions)
    {
        if (!DateOnly.TryParseExact(rawDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            questions.Add(new("due", "Para quando é esse compromisso?"));
            return null;
        }
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, zone).DateTime);
        if (date < today || date > today.AddYears(2))
        {
            questions.Add(new("due", "Confira a data: ela parece estar no passado ou muito distante."));
            return null;
        }
        if (string.IsNullOrWhiteSpace(rawTime))
            return new ProposalDue("date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), null, null, zone.Id);
        if (!TimeOnly.TryParseExact(rawTime, ["HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            questions.Add(new("due", "Qual é o horário desse compromisso?"));
            return new ProposalDue("date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), null, null, zone.Id);
        }
        var civil = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(civil))
        {
            questions.Add(new("due", "Esse horário não existe nesse dia no seu fuso. Qual horário você quer?"));
            return new ProposalDue("date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), null, null, zone.Id);
        }
        // For an ambiguous (repeated) hour, the first occurrence (the larger offset) is used.
        var offset = zone.IsAmbiguousTime(civil) ? zone.GetAmbiguousTimeOffsets(civil).Max() : zone.GetUtcOffset(civil);
        return new ProposalDue("dateTime", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            time.ToString("HH:mm:ss", CultureInfo.InvariantCulture), FormatOffset(offset), zone.Id);
    }

    public static string FormatOffset(TimeSpan offset) =>
        (offset < TimeSpan.Zero ? "-" : "+") + offset.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture);

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= max ? text : text[..max].TrimEnd();
    }
}
