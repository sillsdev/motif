using System.Diagnostics;
using Avalonia.Threading;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.App.Walkthrough;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

[Collection(LcmCacheTestCollection.Name)]
public sealed class ActivationRealClientTests(PristineProjectFixture pristine)
{
    [Fact]
    public void ActivatingAfterAFieldWorksSaveReadsTheSaveAndRunsNothing()
    {
        using var project = new WalkthroughProject(pristine);
        var capturedAt = new DateTime(2026, 3, 2, 9, 15, 0, DateTimeKind.Local);
        var savedAt = new DateTime(2026, 3, 4, 10, 30, 0, DateTimeKind.Local);
        File.SetLastWriteTime(project.FwDataPath, capturedAt);
        var parserPath = FakeParser.CopyRecordingInvocations(project.ManagedRoot);
        var simulator = new FieldWorksSimulator(project.FwDataPath,
            new FixedClock(new DateTimeOffset(savedAt), TimeZoneInfo.Local));
        var deadline = Stopwatch.GetTimestamp() + 90 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var walkthrough = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: parserPath);
            walkthrough.Show();
            await walkthrough.Workspace.SetProjectAsync(project.FwDataPath);
            walkthrough.Click("Refresh the project");
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Baseline.HasBaseline &&
                    walkthrough.Workspace.Selection.Texts.Count == 1 &&
                    walkthrough.Workspace.Context.Setup?.IsOpen == true,
                WalkthroughSteps.Remaining(deadline), "the Baseline capture did not show first-time setup");
            walkthrough.WaitUntil(
                () => !walkthrough.Workspace.RefreshCommand.IsRunning,
                WalkthroughSteps.Remaining(deadline), "the Baseline refresh did not finish");
            walkthrough.SkipSetup();

            var baselineToken = walkthrough.Workspace.Baseline.Token;
            Assert.NotNull(baselineToken);
            Assert.Equal(capturedAt.ToUniversalTime(),
                walkthrough.Workspace.Baseline.SourceLastWriteUtc!.Value.UtcDateTime);
            var invocationsBeforeSave = FakeParser.Invocations(parserPath);
            Assert.Contains("grammar-health", invocationsBeforeSave);

            simulator.SaveEdit(cache =>
            {
                var wordform = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances().First();
                NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor,
                    () => wordform.SpellingStatus = 1);
            });

            Assert.Equal(ProjectFreshness.Current, walkthrough.Workspace.Freshness);
            walkthrough.Window.Hide();
            walkthrough.Window.Show();
            walkthrough.Window.Activate();
            Dispatcher.UIThread.RunJobs();
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Freshness == ProjectFreshness.SavedSince,
                WalkthroughSteps.Remaining(deadline), "window activation did not read FieldWorks' save");

            Assert.Equal("FieldWorks saved since", walkthrough.Workspace.FreshnessLabel);
            Assert.Equal(baselineToken, walkthrough.Workspace.Baseline.Token);
            Assert.Equal(invocationsBeforeSave, FakeParser.Invocations(parserPath));
            return;
        }, WalkthroughSteps.Remaining(deadline));
    }
}
