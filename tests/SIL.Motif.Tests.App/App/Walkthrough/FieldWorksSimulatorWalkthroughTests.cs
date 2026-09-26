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
                    walkthrough.Workspace.Selection.Texts.Count == 1 &&
                    walkthrough.Workspace.Context.Setup?.IsOpen == true,
                WalkthroughSteps.Remaining(deadline), "the window did not show the held project and its setup");
            walkthrough.SkipSetup();
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
    public void AFieldWorksSaveShowsSavedSince()
    {
        using var project = new WalkthroughProject(pristine);
        var capturedAt = new DateTime(2026, 3, 2, 9, 15, 0, DateTimeKind.Local);
        var savedAt = new DateTime(2026, 3, 4, 10, 30, 0, DateTimeKind.Local);
        File.SetLastWriteTime(project.FwDataPath, capturedAt);
        var simulator = new FieldWorksSimulator(project.FwDataPath,
            new FixedClock(new DateTimeOffset(savedAt), TimeZoneInfo.Local));
        var deadline = Stopwatch.GetTimestamp() + 90 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var culture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            try
            {
                using var walkthrough = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath);
                WalkthroughSteps.ChooseProjectAndCaptureBaseline(walkthrough, deadline);
                Assert.Equal(capturedAt.ToUniversalTime(),
                    walkthrough.Workspace.Baseline.SourceLastWriteUtc!.Value.UtcDateTime);
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

                Assert.Equal(ProjectFreshness.SavedSince, walkthrough.Workspace.Freshness);
                Assert.Equal("FieldWorks saved since", walkthrough.Workspace.FreshnessLabel);
                Assert.Equal(
                    $"{Path.GetFileNameWithoutExtension(project.FwDataPath)} saved Wed 4 Mar, 10:30; " +
                    "the numbers still describe Mon 2 Mar, 09:15 until you refresh.",
                    walkthrough.Workspace.FreshnessDetail);
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
            }
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

        var client = RealCommandClient.Create(project.ManagedRoot);
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
}
