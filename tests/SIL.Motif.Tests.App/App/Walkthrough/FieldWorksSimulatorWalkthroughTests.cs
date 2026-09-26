using System.Diagnostics;
using System.Globalization;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class FieldWorksSimulatorWalkthroughTests(PristineProjectFixture pristine)
{
    [Fact]
    public void FieldWorksHoldingTheProjectShowsHeldAndBlocksApply()
    {
        using var project = new WalkthroughProject(pristine);
        var wordformId = CreateWordform(project.FwDataPath, "held-change-word");
        AddPendingSpellingChange(project, wordformId, "held-change-word");
        using var held = new FieldWorksSimulator(project.FwDataPath).Hold();
        var deadline = Stopwatch.GetTimestamp() + 90 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath);
            walkthrough.Show();
            walkthrough.ChooseNewProject();
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Baseline.FieldWorksHeldProject &&
                    walkthrough.Workspace.Selection.Texts.Count == 1,
                WalkthroughSteps.Remaining(deadline), "the window did not show the held project");
            walkthrough.ShowPage(WorkspacePage.Review);
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Context.Changes.Items.Count == 1,
                WalkthroughSteps.Remaining(deadline), "the pending change did not appear in Review");

            var review = walkthrough.Workspace.PageModel<ReviewPageModel>();
            Assert.False(review.CanApply);
            Assert.False(review.ApplyCommand.CanExecute(null));
            Assert.Equal("FieldWorks has this project open. Close it before applying changes.",
                review.ApplyBlockReason);
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }

    [Fact]
    public void AFieldWorksSaveShowsSavedSinceWithTheComposedClock()
    {
        using var project = new WalkthroughProject(pristine);
        var clock = new FixedTimeProvider(DateTimeOffset.Now.AddHours(2));
        var simulator = new FieldWorksSimulator(project.FwDataPath, clock);
        var deadline = Stopwatch.GetTimestamp() + 90 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, timeProvider: clock);
            WalkthroughSteps.ChooseProjectAndCaptureBaseline(walkthrough, deadline);
            var numbersAt = walkthrough.Workspace.Baseline.SourceLastWriteUtc!.Value;
            simulator.SaveEdit(cache =>
            {
                var wordform = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances().First();
                NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor,
                    () => wordform.SpellingStatus = 1);
            });

            var check = walkthrough.Workspace.CheckFreshnessAsync();
            walkthrough.WaitUntil(() => check.IsCompleted, TimeSpan.FromSeconds(30),
                "the window did not check FieldWorks' save");
            check.GetAwaiter().GetResult();

            var savedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(project.FwDataPath), TimeSpan.Zero);
            var expected = $"{Path.GetFileNameWithoutExtension(project.FwDataPath)} saved {When(savedAt)}; " +
                $"the numbers still describe {When(numbersAt)} until you refresh.";
            Assert.Equal(ProjectFreshness.SavedSince, walkthrough.Workspace.Freshness);
            Assert.Equal("FieldWorks saved since", walkthrough.Workspace.FreshnessLabel);
            Assert.Equal(expected, walkthrough.Workspace.FreshnessDetail);
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }

    private static Guid CreateWordform(string projectPath, string form)
    {
        Guid wordformId = Guid.Empty;
        new FieldWorksSimulator(projectPath).SaveEdit(cache =>
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                wordformId = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString(form, cache.DefaultVernWs)).Guid));
        return wordformId;
    }

    private static void AddPendingSpellingChange(WalkthroughProject project, Guid wordformId, string form)
    {
        var productVersion = MotifProductVersion.CurrentText;
        var capture = BaselineCaptureCommand.Capture(
            new BaselineCaptureRequest(project.FwDataPath), project.ManagedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);

        var client = new CommandClient(project.ManagedRoot);
        var loaded = client.LoadPendingChangesAsync(
            new PendingChangesRequest(project.FwDataPath, productVersion), CancellationToken.None)
            .GetAwaiter().GetResult();
        Assert.True(loaded.Succeeded, loaded.Refusal?.Message);
        var put = client.PutPendingChangeAsync(new PutPendingChangeRequest(
            project.FwDataPath, productVersion, loaded.Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                CanonicalId.FromGuid(wordformId).Value, form)), CancellationToken.None)
            .GetAwaiter().GetResult();
        Assert.True(put.Succeeded, put.Refusal?.Message);
    }

    private static string When(DateTimeOffset at)
    {
        var local = at.ToLocalTime();
        return local.Date == DateTime.Today
            ? local.ToString("t", CultureInfo.CurrentCulture) + " today"
            : local.ToString("ddd d MMM, ", CultureInfo.CurrentCulture) +
              local.ToString("t", CultureInfo.CurrentCulture);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;
    }
}
