using Microsoft.Extensions.Configuration;
using Nomi.Assistant.Application;
using Nomi.Assistant.Infrastructure.Interpretation;

namespace Nomi.Assistant.Tests;

public sealed class SemesterProposalBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 13, 0, 0, TimeSpan.Zero);
    private static readonly Guid Calculus = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Ana = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AnaPaula = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly AcademicContext Context = new(
        [new(Calculus, "Cálculo I", "MAT101", [Ana])],
        [new(Ana, "Ana Silva"), new(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "Bruno Costa")]);

    private static SemesterDraft Draft(params SemesterDraftItem[] items) => new(true, items, []);

    [Fact]
    public void Non_academic_text_is_unsupported()
    {
        var proposal = SemesterProposalBuilder.Build(new SemesterDraft(false, [new("Pão", null, null)], []), Context, Now);
        Assert.Equal("unsupported", proposal.Status);
        Assert.Empty(proposal.Subjects);
        Assert.Equal(SemesterProposalBuilder.UnsupportedMessage, proposal.Message);
    }

    [Fact]
    public void Matches_existing_subjects_and_professors_ignoring_case_accents_and_titles()
    {
        var proposal = SemesterProposalBuilder.Build(Draft(
            new SemesterDraftItem("calculo i", null, "professora Ana"),
            new SemesterDraftItem("Engenharia de Software", "ES200", "Prof. Bruno Costa")), Context, Now);
        Assert.Equal("readyForReview", proposal.Status);
        Assert.Equal(new SemesterProposalItem("Cálculo I", "MAT101", "Ana Silva", Calculus, Ana), proposal.Subjects[0]);
        var second = proposal.Subjects[1];
        Assert.Equal("Engenharia de Software", second.Name);
        Assert.Equal("ES200", second.Code);
        Assert.Null(second.ExistingSubjectId);
        Assert.Equal("Bruno Costa", second.ProfessorName);
    }

    [Fact]
    public void Ambiguous_first_name_is_not_matched()
    {
        var context = Context with { Professors = [.. Context.Professors, new(AnaPaula, "Ana Paula")] };
        var proposal = SemesterProposalBuilder.Build(Draft(new SemesterDraftItem("Física I", null, "Ana")), context, Now);
        var item = Assert.Single(proposal.Subjects);
        Assert.Null(item.ExistingProfessorId);
        Assert.Equal("Ana", item.ProfessorName);
    }

    [Fact]
    public void Duplicates_blank_names_and_excess_items_are_dropped()
    {
        var items = Enumerable.Range(1, 30).Select(i => new SemesterDraftItem($"Matéria {i}", null, null))
            .Prepend(new SemesterDraftItem("  ", null, null)).Prepend(new SemesterDraftItem("Matéria 1", null, null)).ToArray();
        var proposal = SemesterProposalBuilder.Build(Draft(items), Context, Now);
        Assert.Equal(SemesterProposalBuilder.MaxSubjects, proposal.Subjects.Count);
        Assert.Equal(proposal.Subjects.Count, proposal.Subjects.Select(s => s.Name).Distinct().Count());
    }

    [Fact]
    public void Academic_without_subjects_asks_which_subjects()
    {
        var proposal = SemesterProposalBuilder.Build(Draft(), Context, Now);
        Assert.Equal("needsClarification", proposal.Status);
        Assert.Contains(proposal.Questions, q => q.Field == "subjects");
    }
}

/// <summary>Real model; only with NOMI_CLAUDE_LIVE=1 and ANTHROPIC_API_KEY.</summary>
public sealed class ClaudeSemesterInterpreterLiveTests
{
    private static readonly bool Live = Environment.GetEnvironmentVariable("NOMI_CLAUDE_LIVE") == "1";

    private static Task<SemesterDraft> Interpret(string text)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Anthropic:ApiKey"] = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"),
        }).Build();
        var interpreter = new ClaudeSemesterInterpreter(new ClaudeStructuredClient(configuration));
        return interpreter.InterpretAsync(new InterpretationInput(text, "America/Sao_Paulo",
            new DateTimeOffset(2026, 11, 22, 10, 0, 0, TimeSpan.FromHours(-3)), AcademicContext.Empty), CancellationToken.None);
    }

    [Fact]
    public async Task Extracts_subjects_with_professors()
    {
        if (!Live) return;
        var draft = await Interpret("Tenho Cálculo com a Ana e Engenharia de Software com o Bruno");
        Assert.True(draft.IsAcademic);
        Assert.Equal(2, draft.Items.Count);
        Assert.Contains(draft.Items, i => i.Name!.Contains("Cálculo") && i.ProfessorName == "Ana");
        Assert.Contains(draft.Items, i => i.Name == "Engenharia de Software" && i.ProfessorName == "Bruno");
    }

    [Fact]
    public async Task Rejects_non_academic_text()
    {
        if (!Live) return;
        var draft = await Interpret("quero pedir uma pizza de calabresa");
        Assert.False(draft.IsAcademic);
    }
}
