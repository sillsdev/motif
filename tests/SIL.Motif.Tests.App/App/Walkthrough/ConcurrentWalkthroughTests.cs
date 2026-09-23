using System.Diagnostics;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
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
                var secondClient = new HoldingCommandClient(
                    new CommandClient(managedRoot), holdAssess: true);
                using var secondWalkthrough = new WalkthroughWindow(
                    managedRoot, secondProject.FwDataPath, commandClient: secondClient);
                WalkthroughSteps.ChooseConformanceProjectAndCaptureBaseline(firstWalkthrough, deadline);
                WalkthroughSteps.ChooseProjectAndCaptureBaseline(secondWalkthrough, deadline);
                secondWalkthrough.Check(SeededProject.TextTitle);

                Assert.NotNull(firstWalkthrough.Workspace.Baseline.Token);
                Assert.NotNull(secondWalkthrough.Workspace.Baseline.Token);
                var firstToken = firstWalkthrough.Workspace.Baseline.Token!;
                var secondToken = secondWalkthrough.Workspace.Baseline.Token!;

                var slowStarted = Stopwatch.GetTimestamp();
                WalkthroughSteps.StartSlowAssessment(firstWalkthrough, deadline);
                WalkthroughSteps.StartAssessmentOverPastedWords(secondWalkthrough, deadline, secondClient);
                firstWalkthrough.WaitUntil(
                    () => firstWalkthrough.Workspace.Assess.State == RunState.Completed,
                    WalkthroughSteps.Remaining(deadline), "the conformance Assessment did not complete beside the second");
                secondWalkthrough.WaitUntil(
                    () => secondWalkthrough.Workspace.Assess.State == RunState.Completed,
                    WalkthroughSteps.Remaining(deadline), "the seeded Assessment did not complete beside the first");
                output.WriteLine($"Slow word list Run-to-Completed wall time: {Stopwatch.GetElapsedTime(slowStarted).TotalSeconds:F3} seconds.");

                Assert.Null(firstWalkthrough.Workspace.Assess.Refusal);
                Assert.Null(secondWalkthrough.Workspace.Assess.Refusal);
                WalkthroughStoreAssertions.AssertSingleInvocationForBaseline(
                    firstProject.FwDataPath, firstToken);
                WalkthroughStoreAssertions.AssertSingleInvocationForBaseline(
                    secondProject.FwDataPath, secondToken);
                return Task.CompletedTask;
            }, WalkthroughSteps.Remaining(deadline));
        }
        finally
        {
            WalkthroughTestFiles.DeleteDirectory(managedRoot);
        }
    }
}
