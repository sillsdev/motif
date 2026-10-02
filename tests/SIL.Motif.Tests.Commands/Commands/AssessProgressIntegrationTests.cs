using SIL.Motif.Commands.Assess;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "Integration")]
public sealed class AssessProgressIntegrationTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task PublicAssessForwardsFlushedRowsBeforeTheNextWordCompletes()
    {
        using var project = new WalkthroughProject(pristine);
        var parser = FakeParser.CopyRecordingInvocations(project.ManagedRoot);
        var release = Path.Combine(project.ManagedRoot, "assess-progress-release");
        FakeParser.BehaveBesideExecutable(parser, new
        {
            streamProgress = true,
            holdEachWordUntil = release,
            words = new[]
            {
                new { word = "a-fast", outcome = "complete", elapsedMs = 2500 },
                new { word = "b-capped", outcome = "capped", elapsedMs = 54000 },
                new { word = "c-timed-out", outcome = "timed-out", elapsedMs = 55000 },
                new { word = "d-held", outcome = "complete", elapsedMs = 12 },
            },
        });
        var firstStarted = new TaskCompletionSource<AssessmentProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCompleted = new TaskCompletionSource<AssessmentProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stoppedRows = new TaskCompletionSource<AssessmentProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = Task.Run(() => AssessCommand.Assess(
            new AssessRequest(project.FwDataPath,
                new SelectionRequest(false, [], ["a-fast", "b-capped", "c-timed-out", "d-held"], false, null),
                PerWordLimitMs: 700, PerWordStepLimit: new SIL.Motif.Contract.Assess.StepCap(123)),
            project.ManagedRoot, parser, progress =>
            {
                if (progress is { Stage: AssessmentStage.Parsing, Completed: 0, CurrentWord: "a-fast" })
                    firstStarted.TrySetResult(progress);
                if (progress is { Stage: AssessmentStage.Parsing, Completed: 1, CurrentWord: "b-capped" })
                    firstCompleted.TrySetResult(progress);
                if (progress is { Stage: AssessmentStage.Parsing, Completed: 3, CurrentWord: "d-held" })
                    stoppedRows.TrySetResult(progress);
            }, CancellationToken.None));

        try
        {
            var started = await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(4, started.Total);
            Assert.Equal(700, started.PerWordLimitMs);
            File.WriteAllText(release + ".0", string.Empty);
            var oneDone = await firstCompleted.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(1, oneDone.Completed);
            Assert.Equal("b-capped", oneDone.CurrentWord);
            File.WriteAllText(release + ".1", string.Empty);
            File.WriteAllText(release + ".2", string.Empty);
            var threeDone = await stoppedRows.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(700, threeDone.PerWordLimitMs);
            Assert.Equal(new[]
            {
                new StoppedParseWord("b-capped", "CAP", 54000),
                new StoppedParseWord("c-timed-out", "TIMEOUT", 55000),
            }, threeDone.StoppedWords);
            Assert.Equal(new ParseWordTiming("c-timed-out", 55000), threeDone.SlowestWord);
            File.WriteAllText(release + ".3", string.Empty);
            var outcome = await run.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
            Assert.Single(FakeParser.Invocations(parser), command => command == "batch");
        }
        finally
        {
            for (var index = 0; index < 4; index++) File.WriteAllText(release + "." + index, string.Empty);
            if (!run.IsCompleted) await run.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }
}
