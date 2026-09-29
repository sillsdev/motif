using System.Diagnostics;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class ConcurrentWalkthroughTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    [RealParserFact]
    public void TwoWindowsShareTheManagedRootWhileBothAssessmentsComplete()
    {
        using var firstProject = new ConformanceProject();
        using var secondProject = new WalkthroughProject(pristine);
        var managedRoot = Path.Combine(
            Path.GetTempPath(), "SIL.Motif.Conformance.Concurrent.Managed", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(managedRoot);
        var deadline = Stopwatch.GetTimestamp() + 120 * Stopwatch.Frequency;

        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(() =>
            {
                using var firstWalkthrough = new WalkthroughWindow(
                    managedRoot, firstProject.FwDataPath);
                var secondGate = new HoldingStartGate();
                using var secondWalkthrough = new WalkthroughWindow(
                    managedRoot, secondProject.FwDataPath, startGate: secondGate);
                WalkthroughSteps.ChooseConformanceProjectAndCaptureBaseline(firstWalkthrough, deadline);
                WalkthroughSteps.ChooseProjectAndCaptureBaseline(secondWalkthrough, deadline);
                secondWalkthrough.Check(SeededProject.TextTitle);
                secondGate.HoldAssess();

                Assert.NotNull(firstWalkthrough.Workspace.Baseline.Token);
                Assert.NotNull(secondWalkthrough.Workspace.Baseline.Token);
                var firstToken = firstWalkthrough.Workspace.Baseline.Token!;
                var secondToken = secondWalkthrough.Workspace.Baseline.Token!;
                var slowStarted = Stopwatch.GetTimestamp();
                WalkthroughSteps.StartSlowAssessment(firstWalkthrough, deadline);
                WalkthroughSteps.StartAssessmentOverPastedWords(secondWalkthrough, deadline, secondGate);
                var firstExecution = firstWalkthrough.Workspace.Assess.RunCommand.ExecutionTask;
                var secondExecution = secondWalkthrough.Workspace.Assess.RunCommand.ExecutionTask;
                Assert.NotNull(firstExecution);
                Assert.NotNull(secondExecution);
                firstWalkthrough.WaitUntil(
                    () => firstExecution.IsCompleted && firstWalkthrough.Workspace.Assess.State == RunState.Completed,
                    WalkthroughSteps.Remaining(deadline), "the conformance Assessment command did not finish beside the second");
                secondWalkthrough.WaitUntil(
                    () => secondExecution.IsCompleted && secondWalkthrough.Workspace.Assess.State == RunState.Completed,
                    WalkthroughSteps.Remaining(deadline), "the seeded Assessment command did not finish beside the first");
                output.WriteLine($"Slow word list Run-to-Completed wall time: {Stopwatch.GetElapsedTime(slowStarted).TotalSeconds:F3} seconds.");

                Assert.Null(firstWalkthrough.Workspace.Assess.Refusal);
                Assert.Null(secondWalkthrough.Workspace.Assess.Refusal);
                var firstRuns = WalkthroughStoreAssertions.ListInvocations(firstProject.FwDataPath)
                    .Where(invocation => invocation.BaselineToken == firstToken).ToArray();
                Assert.Equal(2, firstRuns.Length);
                Assert.Contains(firstRuns, invocation => invocation.Selection.ResolvedWords
                    .Order(StringComparer.Ordinal).SequenceEqual(ConformanceProject.SlowWords.Order(StringComparer.Ordinal)));
                var secondRuns = WalkthroughStoreAssertions.ListInvocations(secondProject.FwDataPath)
                    .Where(invocation => invocation.BaselineToken == secondToken).ToArray();
                Assert.Equal(2, secondRuns.Length);
                var pastedWords = new HashSet<string>(["mofita", "motifa", "motifb"], StringComparer.Ordinal);
                Assert.Contains(secondRuns, invocation => pastedWords.IsSubsetOf(
                    invocation.Selection.ResolvedWords.ToHashSet(StringComparer.Ordinal)));
                return Task.CompletedTask;
            }, WalkthroughSteps.Remaining(deadline));
        }
        finally
        {
            WalkthroughTestFiles.DeleteDirectory(managedRoot);
        }
    }
}
