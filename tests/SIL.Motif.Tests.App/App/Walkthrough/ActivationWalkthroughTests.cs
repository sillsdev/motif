using System.Diagnostics;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class ActivationWalkthroughTests(PristineProjectFixture pristine)
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
        var client = RealCommandClient.Create(project.ManagedRoot);
        var deadline = Stopwatch.GetTimestamp() + 90 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var capture = await client.CaptureBaselineAsync(
                new BaselineCaptureRequest(project.FwDataPath), CancellationToken.None);
            Assert.True(capture.Succeeded, capture.Refusal?.Message);
            var configured = await client.SetDefaultSelectionAsync(new SetDefaultSelectionRequest(
                project.FwDataPath, "Default", [], ["motifa"]), CancellationToken.None);
            Assert.True(configured.Succeeded, configured.Refusal?.Message);
            var skipped = await client.SkipSetupAsync(
                new SkipSetupRequest(project.FwDataPath), CancellationToken.None);
            Assert.True(skipped.Succeeded, skipped.Refusal?.Message);

            using var walkthrough = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: parserPath);
            walkthrough.Show();
            walkthrough.LoadKnownProjects();
            walkthrough.OpenProjectMenu();
            var openRecent = walkthrough.FindProjectMenuEntry<Button>("Open a recent project");
            Assert.True(openRecent.IsEffectivelyEnabled);
            HeadlessClick.Click(walkthrough.Window, openRecent, "Open a recent project");
            var recentProject = Assert.Single(walkthrough.Window.RecentProjectItems);
            HeadlessClick.Click(walkthrough.Window, recentProject,
                AutomationProperties.GetName(recentProject) ?? "recent project");
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.OpenRecentProjectCommand.ExecutionTask is { IsCompleted: true },
                WalkthroughSteps.Remaining(deadline), "opening the recent project did not finish");
            await walkthrough.Workspace.Context.EvidencePublication;
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Baseline.HasBaseline &&
                    walkthrough.Workspace.Selection.Texts.Count == 1,
                WalkthroughSteps.Remaining(deadline), "the recent project did not load its Baseline and Texts");

            var baselineToken = walkthrough.Workspace.Baseline.Token;
            Assert.NotNull(baselineToken);
            Assert.Equal(capturedAt.ToUniversalTime(),
                walkthrough.Workspace.Baseline.SourceLastWriteUtc!.Value.UtcDateTime);
            var invocationsBeforeSave = FakeParser.Invocations(parserPath);
            Assert.False(walkthrough.Workspace.Context.Setup?.IsOpen == true);

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

            Assert.Equal(baselineToken, walkthrough.Workspace.Baseline.Token);
            Assert.Equal(invocationsBeforeSave, FakeParser.Invocations(parserPath));
            var renderedText = walkthrough.Window.GetLogicalDescendants().OfType<TextBlock>()
                .Where(text => text.IsVisible).Select(text => text.Text).OfType<string>().ToArray();
            Assert.Contains("FieldWorks saved since", renderedText);
            var renderedDetail = Assert.Single(renderedText, text => text.Contains(
                "the numbers still describe", StringComparison.Ordinal));
            Assert.Equal(walkthrough.Workspace.FreshnessDetail, renderedDetail);
            Assert.EndsWith("until you refresh.", renderedDetail, StringComparison.Ordinal);
            Assert.Contains(Path.GetFileNameWithoutExtension(project.FwDataPath), renderedDetail,
                StringComparison.Ordinal);
        }, WalkthroughSteps.Remaining(deadline));
    }
}
