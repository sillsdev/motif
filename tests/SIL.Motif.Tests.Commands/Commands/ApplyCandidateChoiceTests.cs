using SIL.Motif.Commands;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class ApplyCandidateChoiceTests
{
    private static readonly Readiness.Decision Ready = new([], false);

    private static Readiness.Decision NotReady(string reason) => new([reason], false);

    private static AssessmentRecord Record(string id, string selection, params string[] words) => new(
        AssessmentId: id,
        ProposalId: null,
        ProposalIntentDigest: null,
        Assessor: "pangloss",
        Kind: "Correctness",
        ScopeJson: "{}",
        ScopeDigest: "scope",
        Subject: new AssessmentSubject.Words(Selection.Create(selection, words), "whitespace", "1"),
        BaselineToken: "baseline",
        OutcomeDigest: null,
        SemanticDigest: null,
        GrammarSourceSha256: "grammar",
        ModelFingerprint: null,
        Pipeline: null,
        DiagnosticCount: null,
        SavedUtc: "2026-10-15T00:00:00Z",
        Details: new AssessmentRecordDetails.Unloaded());

    private static ProposalCommands.CandidateChoice Choose(string[] newestFirst,
        Func<AssessmentRecord, Readiness.Decision> evaluate) => ProposalCommands.ChooseCandidate(newestFirst,
        id => Record(id, id == "text-trial" ? "Hadithi" : "word list", "sita"),
        candidate => candidate is null ? NotReady("no Assessment") : evaluate(candidate));

    [Fact]
    public void AnOlderReadyAssessmentIsReadWhenTheNewestOneIsNotReady()
    {
        var choice = Choose(["text-trial", "word-list"],
            candidate => candidate.AssessmentId == "word-list" ? Ready : NotReady("changed word not measured"));

        Assert.Equal("word-list", choice.Candidate!.AssessmentId);
        Assert.Empty(choice.Decision.Reasons);
        Assert.Equal(2, choice.Considered);
    }

    [Fact]
    public void TheNewestReadyAssessmentWinsWhenSeveralAreReady()
    {
        var choice = Choose(["text-trial", "word-list"], _ => Ready);

        Assert.Equal("text-trial", choice.Candidate!.AssessmentId);
    }

    [Fact]
    public void WhenNoneIsReadyTheNewestIsReportedAndNamed()
    {
        var choice = Choose(["text-trial", "word-list"], candidate => NotReady("why " + candidate.AssessmentId));

        Assert.Equal("text-trial", choice.Candidate!.AssessmentId);
        Assert.Equal("why text-trial", Assert.Single(choice.Decision.Reasons));
        var text = ProposalCommands.DescribeCandidate(choice.Candidate, choice.Considered);
        Assert.Contains("text-trial", text, StringComparison.Ordinal);
        Assert.Contains("Selection 'Hadithi'", text, StringComparison.Ordinal);
        Assert.Contains("the other 1 Assessment(s) of this revision were checked too and none is ready", text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ARevisionWithNoAssessmentIsEvaluatedAsNoCandidate()
    {
        var choice = Choose([], _ => Ready);

        Assert.Null(choice.Candidate);
        Assert.Equal(0, choice.Considered);
        Assert.Equal("", ProposalCommands.DescribeCandidate(choice.Candidate, choice.Considered));
        Assert.Equal("no Assessment", Assert.Single(choice.Decision.Reasons));
    }

    [Fact]
    public void ASingleAssessmentIsNamedWithoutTheOthersClause()
    {
        var choice = Choose(["word-list"], _ => NotReady("incomplete"));

        var text = ProposalCommands.DescribeCandidate(choice.Candidate, choice.Considered);

        Assert.Contains("word-list", text, StringComparison.Ordinal);
        Assert.DoesNotContain("other", text, StringComparison.Ordinal);
    }

    private static string[] Ids(int count) => [.. Enumerable.Range(0, count).Select(index => "trial-" + index)];

    [Fact]
    public void LoadingStopsAtTheFirstReadyAssessment()
    {
        var loaded = new List<string>();

        var choice = ProposalCommands.ChooseCandidate(Ids(8),
            id => { loaded.Add(id); return Record(id, id, "sita"); },
            candidate => candidate!.AssessmentId == "trial-2" ? Ready : NotReady("not yet"));

        Assert.Equal("trial-2", choice.Candidate!.AssessmentId);
        Assert.Equal(["trial-0", "trial-1", "trial-2"], loaded);
    }

    [Fact]
    public void OnlyTheNewestTenAreLoadedAndTheRestAreCountedAsUnread()
    {
        var loaded = new List<string>();

        var choice = ProposalCommands.ChooseCandidate(Ids(13),
            id => { loaded.Add(id); return Record(id, id, "sita"); },
            _ => NotReady("not ready"));

        Assert.Equal(ProposalCommands.MaxCandidatesRead, loaded.Count);
        Assert.Equal("trial-0", choice.Candidate!.AssessmentId);
        Assert.Equal(10, choice.Considered);
        Assert.Equal(3, choice.Unread);
        var text = ProposalCommands.DescribeCandidate(choice.Candidate, choice.Considered, choice.Unread);
        Assert.Contains("the other 9 Assessment(s) of this revision were checked too and none is ready", text,
            StringComparison.Ordinal);
        Assert.Contains("3 older Assessment(s) were not read, as only the newest 10 are", text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AReadyAssessmentPastTheNewestTenIsNotLookedFor()
    {
        var choice = ProposalCommands.ChooseCandidate(Ids(12),
            id => Record(id, id, "sita"),
            candidate => candidate!.AssessmentId == "trial-11" ? Ready : NotReady("not ready"));

        Assert.NotEmpty(choice.Decision.Reasons);
        Assert.Equal("trial-0", choice.Candidate!.AssessmentId);
    }
}
