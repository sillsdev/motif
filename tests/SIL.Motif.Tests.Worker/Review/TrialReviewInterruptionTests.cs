using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Worker.Jobs;
using Xunit;

namespace SIL.Motif.Tests.Worker.Review;

public sealed class TrialReviewInterruptionTests
{
    private static FrozenReviewInputs Inputs() => new("project", "proposal", "scope", "evidence", null, [], [],
    [
        new("a", "word-a", "first", null, null, [], []),
        new("b", "word-b", "second", null, null, [], []),
        new("c", "word-c", "third", null, null, [], []),
    ]);

    private static ParseWordEvidence Row(string word, bool capped = false, bool timedOut = false,
        IReadOnlyList<ParseAnalysis>? analyses = null) =>
        new("fieldworks-parse-analysis/v1", 0, word, 1, capped, timedOut, false, analyses ?? [], []);

    [Fact]
    public void WorkAndTimeStopMasksRemainSeparate()
    {
        var row = new TrialReviewObservation("a", "first", "capped", Row("first", capped: true, timedOut: true));
        Assert.Equal(Completion.Stopped, row.Completion);
        Assert.Equal(new[] { "time-limit", "work-limit" }, row.StopReasons);
    }

    [Fact]
    public void MemoryKilledBatchDoesNotMarkEveryWordMemoryStopped()
    {
        var ledger = new TrialReviewLedger("job", new("digest"), [], [], []);
        var stopped = TrialReviewEvidence.Interrupt(ledger, Inputs(), "after", "assess.parser-memory-limit");
        Assert.Empty(stopped.After);
        Assert.Equal(3, Assert.Single(stopped.Interruptions).PendingCaseKeys.Count);
    }

    [Fact]
    public void CompleteEarlierRowsSurviveLaterMemoryKillAndPendingCasesRemainNotMeasured()
    {
        var first = new AssessedWord("first", "no-analysis", []) { Morphology = Row("first") };
        var rows = TrialReviewEvidence.Observe(Inputs(), ["a", "b", "c"], [first]);
        var ledger = new TrialReviewLedger("job", new("digest"), [], rows, []);
        var stopped = TrialReviewEvidence.Interrupt(ledger, Inputs(), "after", "assess.parser-memory-limit");
        Assert.Equal(Completion.Complete, Assert.Single(stopped.After).Completion);
        Assert.Equal(new[] { "b", "c" }, Assert.Single(stopped.Interruptions).PendingCaseKeys);
    }

    [Fact]
    public void ObservedForbiddenPartialParseSurvives()
    {
        var parse = new ParseAnalysis([new ParseMorph(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), null, null)]);
        var partial = new AssessedWord("first", "capped", []) { Morphology = Row("first", capped: true, analyses: [parse]) };
        var observed = Assert.Single(TrialReviewEvidence.Observe(Inputs(), ["a"], [partial]));
        Assert.Equal(Completion.Stopped, observed.Completion);
        Assert.Single(observed.Morphology!.Analyses);
    }

    [Fact]
    public void InterruptedApprovedAbsenceRemainsAnIncompleteSearch()
    {
        var interrupted = new TrialReviewObservation("a", "first", "capped", Row("first", capped: true));
        Assert.Empty(interrupted.Morphology!.Analyses);
        Assert.Equal(Completion.Stopped, interrupted.Completion);
        Assert.Equal("work-limit", Assert.Single(interrupted.StopReasons));
    }

    [Fact]
    public void UnattributedCapDoesNotInventAWorkOrMemoryCause()
    {
        var interrupted = new TrialReviewObservation("a", "first", "stopped", Row("first", capped: true));
        Assert.Equal(Completion.Stopped, interrupted.Completion);
        Assert.Empty(interrupted.StopReasons);
    }

    [Fact]
    public void CancelledBeforeFirstWordKeepsRequiredCount()
    {
        var ledger = new TrialReviewLedger("job", new("digest"), [], [], []);
        var stopped = TrialReviewEvidence.Interrupt(ledger, Inputs(), "after", "cancelled");
        Assert.Equal(3, Assert.Single(stopped.Interruptions).PendingCaseKeys.Count);
    }

    [Fact]
    public void InterruptedBatchRetainsOnlyCompleteValidatedAttributedRows()
    {
        var root = Directory.CreateTempSubdirectory("motif-partial-review-").FullName;
        try
        {
            var good = System.Text.Json.JsonSerializer.Serialize(Row("first", capped: true), ParseMorphEvidence.JsonOptions);
            var wrong = System.Text.Json.JsonSerializer.Serialize(Row("wrong"), ParseMorphEvidence.JsonOptions);
            File.WriteAllText(Path.Combine(root, "analyses.jsonl"), good + "\n" + wrong + "\n{" );
            IReadOnlyList<ParseWordEvidence>? captured = null;
            var batch = new PanGlossRequest.Batch("project.fwdata", ["first", "second"], null)
                { OnPartial = rows => captured = rows };
            PanGlossInvoker.PublishPartial(batch, root);
            Assert.Equal("first", Assert.Single(captured!).Word);
            Assert.True(captured![0].Capped);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void BeforePartialEvidenceAndPendingCasesAreReadableWithoutAWorker()
    {
        var root = Directory.CreateTempSubdirectory("motif-review-stopped-").FullName;
        try
        {
            var project = new ProjectLocator(Path.Combine(root, "project.fwdata"), "project");
            using var database = MotifDatabase.OpenOwned(Path.Combine(root, "project.db"), project,
                MotifSchema.CurrentSchema, new Version(1, 0));
            var inputs = ReviewInputArtifacts.Store(database, new(Inputs(), new Dictionary<string, byte[]>()));
            var partial = TrialReviewEvidence.StoreBefore(database,
                [new TrialReviewObservation("a", "first", "capped", Row("first", capped: true))]);
            var input = new TrialJobInput("{}", null, ReviewInputs: inputs,
                ReviewBeforeStopCode: "assess.parser-memory-limit", ReviewBeforePartial: partial);
            var json = System.Text.Json.JsonSerializer.Serialize(input, SIL.Motif.Contract.MotifJson.CreateOptions());
            new JobRepository(database).RecordInterrupted("job/test", "project", "trial", json, "{}",
                "2026-11-07T00:00:00Z", JobStatus.Failed);

            var ledger = TrialReviewEvidence.ReadJob(database, "job/test")!;
            Assert.Equal(Completion.Stopped, Assert.Single(ledger.Before).Completion);
            Assert.Equal(new[] { "b", "c" }, Assert.Single(ledger.Interruptions).PendingCaseKeys);
            Assert.Throws<OperationCanceledException>(() => TrialReviewEvidence.SaveForClaim(database, ledger, "stale-claim"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
