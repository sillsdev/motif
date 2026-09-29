using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.App.Walkthrough;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Smoke;

[Collection(LcmCacheTestCollection.Name)]
public sealed class FirstProjectSmokeTests(PristineProjectFixture pristine)
{
    [Fact]
    public void AFirstProjectOpensCapturesSetsUpAndShowsItsFirstRun()
    {
        using var project = new TwoTextWalkthroughProject(pristine);
        var deadline = Stopwatch.GetTimestamp() + 60 * Stopwatch.Frequency;
        var parser = FakeParser.Copy(project.ManagedRoot);
        var batchStarted = Path.Combine(project.ManagedRoot, "first-batch-started");
        var releaseBatch = Path.Combine(project.ManagedRoot, "release-first-batch");

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: parser);
            SetupWalkthroughActions.SelectProject(walkthrough, project.FwDataPath);
            SetupWalkthroughActions.CaptureBaselineAndWaitForSetup(
                walkthrough, 2, StepTimeout(deadline));

            var setup = walkthrough.Workspace.Context.Setup!;
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: texts");
            SetupWalkthroughActions.SetSetupTextChecked(walkthrough, SeededProject.TextTitle, true);
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: limits");
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: first run");
            walkthrough.SetFakeParserBehavior(new
            {
                subcommands = new Dictionary<string, object>
                {
                    ["batch"] = new
                    {
                        startedPath = batchStarted,
                        holdUntilPath = releaseBatch,
                        words = new[] { new { word = "motifa", outcome = "complete" } },
                    },
                    ["parse"] = new
                    {
                        traceJson = "{\"type\":\"WordAnalysis\",\"inputShape\":\"unlistedword\",\"children\":[" +
                            "{\"type\":\"MorphologicalRuleAnalysis\",\"source\":\"SeededRule\",\"children\":[" +
                            "{\"type\":\"Successful\",\"children\":[]}]}]}",
                    },
                },
            });
            try
            {
                walkthrough.Click("Start first run");
                walkthrough.WaitUntil(() => File.Exists(batchStarted), StepTimeout(deadline),
                    "the first run did not reach the fake parser");
                Assert.Equal(RunState.Running, walkthrough.Workspace.Assess.State);
                Assert.False(File.Exists(releaseBatch));
            }
            finally
            {
                File.WriteAllText(releaseBatch, string.Empty);
            }
            walkthrough.WaitUntil(() => !setup.IsOpen &&
                walkthrough.Workspace.Assess.State == RunState.Completed &&
                walkthrough.Workspace.Context.EvidencePublication.IsCompleted,
                StepTimeout(deadline), "the released first run did not reach the pages");

            walkthrough.ShowPage(WorkspacePage.Overview);
            walkthrough.WaitUntil(() => walkthrough.Workspace.PageModel<OverviewPageModel>().Overview is not null,
                StepTimeout(deadline), "Overview did not load");
            Assert.True(walkthrough.Find<Border>("Project summary").IsEffectivelyVisible);
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }

    private static TimeSpan StepTimeout(long deadline)
    {
        var remaining = WalkthroughSteps.Remaining(deadline);
        var stepLimit = TimeSpan.FromSeconds(30);
        return remaining < stepLimit ? remaining : stepLimit;
    }
}
