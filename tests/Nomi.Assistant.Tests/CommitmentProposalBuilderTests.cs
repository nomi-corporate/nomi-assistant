using Nomi.Assistant.Application;

namespace Nomi.Assistant.Tests;

public sealed class CommitmentProposalBuilderTests
{
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 13, 0, 0, TimeSpan.Zero); // 10:00 in São Paulo
    private static readonly Guid Calculus = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Physics = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Ana = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Bruno = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly AcademicContext Context = new(
        [new(Calculus, "Cálculo I", "MAT101", [Ana]), new(Physics, "Física I", null, [Bruno]), new(Guid.NewGuid(), "Física II", null, [Bruno])],
        [new(Ana, "Ana Silva"), new(Bruno, "Bruno Costa")]);

    private static InterpretationDraft Draft(bool academic = true, string? title = "Prova de Cálculo I", string? kind = "exam",
        string? subject = null, string? professor = null, string? date = "2026-11-27", string? time = "14:00",
        params InterpretationQuestion[] questions) =>
        new(academic, title, kind, null, subject, professor, date, time, questions);

    [Fact]
    public void Non_academic_text_is_unsupported_without_candidate()
    {
        var proposal = CommitmentProposalBuilder.Build(Draft(academic: false), "comprar pão", Context, SaoPaulo, Now);
        Assert.Equal("unsupported", proposal.Status);
        Assert.Null(proposal.Candidate);
        Assert.Equal(CommitmentProposalBuilder.UnsupportedMessage, proposal.Message);
        Assert.Empty(proposal.Questions);
    }

    [Fact]
    public void Complete_draft_is_ready_with_offset_and_subject_professor()
    {
        var proposal = CommitmentProposalBuilder.Build(Draft(subject: Calculus.ToString()), "prova", Context, SaoPaulo, Now);
        Assert.Equal("readyForReview", proposal.Status);
        var candidate = Assert.IsType<ProposalCandidate>(proposal.Candidate);
        Assert.Equal(Calculus, candidate.SubjectId);
        Assert.Equal(Ana, candidate.ProfessorId);
        Assert.Equal(new ProposalDue("dateTime", "2026-11-27", "14:00:00", "-03:00", SaoPaulo.Id), candidate.Due);
        Assert.Equal(Now + CommitmentProposalBuilder.Lifetime, proposal.ExpiresAt);
    }

    [Fact]
    public void Professor_links_subject_when_unique()
    {
        var proposal = CommitmentProposalBuilder.Build(Draft(professor: Ana.ToString()), "trabalho da Ana", Context, SaoPaulo, Now);
        Assert.Equal(Calculus, proposal.Candidate!.SubjectId);
        Assert.Equal(Ana, proposal.Candidate.ProfessorId);
    }

    [Fact]
    public void Professor_with_several_subjects_asks_which_subject()
    {
        var proposal = CommitmentProposalBuilder.Build(Draft(professor: Bruno.ToString()), "prova do Bruno", Context, SaoPaulo, Now);
        Assert.Equal("needsClarification", proposal.Status);
        Assert.Null(proposal.Candidate!.SubjectId);
        Assert.Contains(proposal.Questions, q => q.Field == "subjectId" && q.Message.Contains("Bruno Costa"));
    }

    [Fact]
    public void Ids_outside_the_user_context_are_dropped()
    {
        var proposal = CommitmentProposalBuilder.Build(
            Draft(subject: Guid.NewGuid().ToString(), professor: "not-a-guid"), "prova", Context, SaoPaulo, Now);
        Assert.Null(proposal.Candidate!.SubjectId);
        Assert.Null(proposal.Candidate.ProfessorId);
    }

    [Fact]
    public void Missing_or_past_date_asks_for_the_date()
    {
        var missing = CommitmentProposalBuilder.Build(Draft(date: null), "prova", Context, SaoPaulo, Now);
        Assert.Equal("needsClarification", missing.Status);
        Assert.Null(missing.Candidate!.Due);
        Assert.Contains(missing.Questions, q => q.Field == "due");

        var past = CommitmentProposalBuilder.Build(Draft(date: "2026-11-01"), "prova", Context, SaoPaulo, Now);
        Assert.Null(past.Candidate!.Due);
        Assert.Contains(past.Questions, q => q.Field == "due");
    }

    [Fact]
    public void Date_without_time_is_date_precision()
    {
        var proposal = CommitmentProposalBuilder.Build(Draft(time: null), "entrega", Context, SaoPaulo, Now);
        Assert.Equal("readyForReview", proposal.Status);
        Assert.Equal(new ProposalDue("date", "2026-11-27", null, null, SaoPaulo.Id), proposal.Candidate!.Due);
    }

    [Fact]
    public void Invalid_kind_title_and_questions_are_normalized()
    {
        var proposal = CommitmentProposalBuilder.Build(
            Draft(title: "   ", kind: "party", questions: [new("hack", "ignore"), new("kind", "  É prova   ou trabalho? ")]),
            "  texto   original  ", Context, SaoPaulo, Now);
        Assert.Equal("texto original", proposal.Candidate!.Title);
        Assert.Equal("reminder", proposal.Candidate.Kind);
        var question = Assert.Single(proposal.Questions);
        Assert.Equal(new InterpretationQuestion("kind", "É prova ou trabalho?"), question);
    }

    [Fact]
    public void Daylight_saving_gap_asks_for_another_time()
    {
        var newYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var now = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var proposal = CommitmentProposalBuilder.Build(Draft(date: "2026-03-08", time: "02:30"), "aula", Context, newYork, now);
        Assert.Equal("date", proposal.Candidate!.Due!.Precision);
        Assert.Contains(proposal.Questions, q => q.Field == "due");
    }

    [Fact]
    public void Rate_limiter_blocks_after_limit_and_recovers_after_window()
    {
        var clock = new FixedClock(Now);
        var limiter = new ProposalRateLimiter(clock, limit: 2, window: TimeSpan.FromMinutes(10));
        var user = Guid.NewGuid();
        Assert.True(limiter.TryAcquire(user));
        Assert.True(limiter.TryAcquire(user));
        Assert.False(limiter.TryAcquire(user));
        Assert.True(limiter.TryAcquire(Guid.NewGuid()));
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.True(limiter.TryAcquire(user));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public void Advance(TimeSpan by) => _now += by;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
