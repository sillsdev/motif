using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class EvidenceLensTests
{
    private static readonly BaselineToken Token = new("project", "sha256:" + new string('a', 64), "1",
        "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64));

    [Fact]
    public void PairWithoutATrialHasNoAfter()
    {
        var today = TodayEvidence();
        var lens = new EvidenceLens(today, null, MarkupView.TodayToProposal);
        var pair = lens.Pair(set => set.RootAssessmentId, (before, after) => before != after);

        Assert.True(pair.HasBefore);
        Assert.False(pair.HasAfter);
        Assert.Equal("today-assessment", pair.Before);
    }

    [Fact]
    public void PairReadsEachAvailableSetOnce()
    {
        var today = TodayEvidence();
        var lens = new EvidenceLens(today, TrialEvidenceOf("trial-assessment"), MarkupView.TodayToProposal);
        var reads = 0;

        var pair = lens.Pair(set =>
        {
            reads++;
            return set.RootAssessmentId;
        }, (before, after) => before != after);

        Assert.Equal(2, reads);
        Assert.Equal("today-assessment", pair.Before);
        Assert.Equal("trial-assessment", pair.After);
        Assert.True(pair.HasAfter);
    }

    [Fact]
    public void MovedUsesOnlyTheDomainComparer()
    {
        var lens = new EvidenceLens(TodayEvidence(), TrialEvidenceOf("trial-assessment"),
            MarkupView.TodayToProposal);

        var domainComparison = lens.Pair(set => set.RootAssessmentId,
            (before, after) => before != after);

        Assert.True(domainComparison.Moved);
    }

    [Theory]
    [InlineData(MarkupView.Today, "today-assessment")]
    [InlineData(MarkupView.TodayToProposal, "today-assessment")]
    [InlineData(MarkupView.Proposal, "trial-assessment")]
    public void ShownUsesTheSelectedMarkupView(MarkupView view, string expected)
    {
        var lens = new EvidenceLens(TodayEvidence(), TrialEvidenceOf("trial-assessment"), view);
        var pair = lens.Pair(set => set.RootAssessmentId, (before, after) => before != after);

        Assert.Equal(expected, pair.Shown(view));
    }

    [Fact]
    public void ProposalModeWithoutATrialKeepsTodayAndHasNoAfter()
    {
        var lens = new EvidenceLens(TodayEvidence(), null, MarkupView.Proposal, inProposalMode: true);

        var pair = lens.Pair(set => set.RootAssessmentId, (before, after) => before != after);

        Assert.True(lens.InProposalMode);
        Assert.False(pair.HasAfter);
        Assert.Equal("today-assessment", pair.Shown(MarkupView.Proposal));
    }

    [Fact]
    public async Task PairAsyncReadsTheSameNamedSetsInOrder()
    {
        var lens = new EvidenceLens(TodayEvidence(), TrialEvidenceOf("trial-assessment"),
            MarkupView.TodayToProposal);
        var readIds = new List<string?>();

        var pair = await lens.PairAsync((set, _) =>
        {
            readIds.Add(set.RootAssessmentId);
            return Task.FromResult(CommandOutcome<OverviewResponse>.Success(Overview(set.RootAssessmentId)));
        }, CancellationToken.None, (before, after) => before != after);

        Assert.Equal(["today-assessment", "trial-assessment"], readIds);
        Assert.True(pair.HasBefore);
        Assert.True(pair.HasAfter);
    }

    [Fact]
    public async Task PairAsyncReturnsAnAfterRefusalAndShowsTodaysValue()
    {
        var lens = new EvidenceLens(TodayEvidence(), TrialEvidenceOf("trial-assessment"),
            MarkupView.Proposal);

        var pair = await lens.PairAsync((set, _) => Task.FromResult(
                set.Origin.Kind == EvidenceOriginKind.Trial
                    ? CommandOutcome<OverviewResponse>.Refused(new Refusal("trial.scratch-gone",
                        FailureReason.NotFound, "The Trial scratch is gone."))
                    : CommandOutcome<OverviewResponse>.Success(Overview(set.RootAssessmentId))),
            CancellationToken.None, (before, after) => before != after);

        Assert.True(pair.HasBefore);
        Assert.False(pair.HasAfter);
        Assert.Equal("trial.scratch-gone", pair.AfterRefusal?.Code);
        Assert.Equal("today-assessment", pair.Shown(MarkupView.Proposal)?.AssessmentId);
    }

    [Fact]
    public async Task PairAsyncTreatsASuccessfulNullAsAnUnavailableValue()
    {
        var lens = new EvidenceLens(TodayEvidence(), TrialEvidenceOf("trial-assessment"),
            MarkupView.Proposal);

        var pair = await lens.PairAsync((set, _) => Task.FromResult(
                set.Origin.Kind == EvidenceOriginKind.Trial
                    ? CommandOutcome<OverviewResponse?>.Success(null)
                    : CommandOutcome<OverviewResponse?>.Success(Overview(set.RootAssessmentId))),
            CancellationToken.None, (before, after) => before != after);

        Assert.True(pair.HasBefore);
        Assert.False(pair.HasAfter);
        Assert.Null(pair.AfterRefusal);
        Assert.Equal("today-assessment", pair.Shown(MarkupView.Proposal)?.AssessmentId);
    }

    [Fact]
    public void PairWithoutTodayEvidenceDoesNotInventABeforeValue()
    {
        var lens = new EvidenceLens(new ProjectEvidence(), TrialEvidenceOf("trial-assessment"),
            MarkupView.TodayToProposal);

        var pair = lens.Pair(set => set.RootAssessmentId, (before, after) => before != after);

        Assert.False(pair.HasBefore);
        Assert.Null(pair.Before);
        Assert.True(pair.HasAfter);
    }

    private static ProjectEvidence TodayEvidence()
    {
        var evidence = new ProjectEvidence();
        var assessment = new AssessCommandResponse(
            new BaselineCaptureResponse(Token, "project.fwdata", DateTimeOffset.UnixEpoch, false, false),
            new SelectionProjection([], []), [], "today")
        {
            Measurements = [new ProducedAssessmentReference("today-assessment", AssessmentKinds.ParseTime, "today")],
        };
        evidence.ShowRun(new WorkspaceEvidence(assessment, DateTimeOffset.UnixEpoch, false));
        return evidence;
    }

    private static TrialEvidence TrialEvidenceOf(string rootId)
    {
        var trial = new TrialRecord("job/trial", "proposal/one", "sha256:intent", Token, null,
            new Dictionary<string, string> { [AssessmentKinds.ParseTime] = rootId }, [], false, null,
            SIL.Motif.Contract.Jobs.JobStatus.Completed, DateTimeOffset.UnixEpoch, TrialFreshness.Current);
        var set = new EvidenceSet(Token, rootId, [], new Dictionary<string, string>(), null, [],
            new EvidenceOrigin(EvidenceOriginKind.Trial, trial.ProposalId, trial.IntentDigest, trial.JobId));
        return new TrialEvidence(set, trial, []);
    }

    private static OverviewResponse Overview(string? id) => new("project", DateTimeOffset.UnixEpoch, null,
        0, 0, 0, 0, 0, 0, 0, id, null, null, null, null,
        new OverviewTextCoverage(0, 0, 0, 0, 0, 0), new OverviewAccuracy(0, 0, 0, 0, 0, 0, 0, 0),
        new OverviewTiming(null, null, [], 0), null);
}
