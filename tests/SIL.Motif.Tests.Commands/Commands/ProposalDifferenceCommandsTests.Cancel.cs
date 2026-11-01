using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed partial class ProposalDifferenceCommandsTests
{
    // Reports on the caller's thread, so a test can cancel at a known point of the run.
    private sealed class Recorder(Action<AssessmentProgress>? onReport = null) : IProgress<AssessmentProgress>
    {
        public List<AssessmentProgress> Reports { get; } = [];

        public void Report(AssessmentProgress value)
        {
            Reports.Add(value);
            onReport?.Invoke(value);
        }
    }

    [Fact]
    public void ARunCancelledBeforeItStartsSavesNothingAndSaysItWasCancelled()
    {
        using var fixture = new Fixture(_root);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        var outcome = ProposalDifferenceCommands.Produce(fixture.ProduceRequest(), null, cancel.Token);

        Assert.Equal(RefusalCodes.DifferenceCancelled, outcome.Refusal!.Code);
        Assert.Equal(FailureReason.Cancelled, outcome.Refusal.Reason);
        var shown = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(fixture.ProjectPath,
            ProposalId: fixture.ProposalId));
        Assert.Equal(RefusalCodes.DifferenceNone, shown.Refusal!.Code);
    }

    [Fact]
    public void ARunCancelledWhileComparingSavesNothingAndAnUncancelledRunAfterItStillSaves()
    {
        using var fixture = new Fixture(_root);
        using var cancel = new CancellationTokenSource();
        var recorder = new Recorder(report =>
        {
            if (report.Stage == AssessmentStage.Finalizing) cancel.Cancel();
        });

        var cancelled = ProposalDifferenceCommands.Produce(fixture.ProduceRequest(), recorder, cancel.Token);

        Assert.Equal(RefusalCodes.DifferenceCancelled, cancelled.Refusal!.Code);
        Assert.Equal(RefusalCodes.DifferenceNone, ProposalDifferenceCommands.Show(
            new ProposalDifferenceShowRequest(fixture.ProjectPath, ProposalId: fixture.ProposalId)).Refusal!.Code);

        var made = ProposalDifferenceCommands.Produce(fixture.ProduceRequest());

        Assert.True(made.Succeeded, made.Refusal?.Message);
        Assert.Equal(2, fixture.Count("ProposalDifferenceCases", made.Value!.Summary.DifferenceId));
    }

    [Fact]
    public void ARunReportsReadingTheTextThenWordsComparedOfTheTotal()
    {
        using var fixture = new Fixture(_root);
        var recorder = new Recorder();

        var outcome = ProposalDifferenceCommands.Produce(fixture.ProduceRequest(), recorder, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(AssessmentStage.SelectingWords, recorder.Reports[0].Stage);
        Assert.All(recorder.Reports.Skip(1), report => Assert.Equal(AssessmentStage.Finalizing, report.Stage));
        var compared = recorder.Reports.Skip(1).ToList();
        Assert.NotEmpty(compared);
        Assert.Equal(0, compared[0].Completed);
        Assert.Equal(2, compared[0].Total);
        Assert.Equal(recorder.Reports.Skip(1).Select(report => report.Completed).Order(),
            recorder.Reports.Skip(1).Select(report => report.Completed));
    }

    [Fact]
    public void APublishCancelledJustBeforeTheCommitRollsEverythingBackAndAnUncancelledOneStillSaves()
    {
        using var fixture = new Fixture(_root);
        var produced = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!;
        var summary = produced.Summary with { DifferenceId = "difference/late-cancel" };
        var difference = new NewProposalDifference(summary, [.. produced.Words.Select((word, index) =>
            new NewProposalDifferenceWord(word.Word, word.Facts, [new ProposalDifferenceOccurrence("t", "s", index, 1)]))]);
        var repository = new ProposalDifferenceRepository(fixture.Database);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => repository.Publish(difference, cancel.Token));

        foreach (var table in new[] { "ProposalDifferences", "ProposalDifferenceCases", "ProposalDifferenceOccurrences", "ProposalDifferenceBuckets" })
            Assert.Equal(0, fixture.Count(table, "difference/late-cancel"));
        Assert.Equal(1, fixture.Count("ProposalDifferences", produced.Summary.DifferenceId));

        repository.Publish(difference, CancellationToken.None);

        Assert.Equal(1, fixture.Count("ProposalDifferences", "difference/late-cancel"));
    }
}
