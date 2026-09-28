using Avalonia.Controls;
using Avalonia.LogicalTree;
using SIL.LCModel;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class ReopenWalkthroughTests(PristineProjectFixture pristine)
{
    [Fact]
    public void ANewWindowOverTheSameStoreShowsStoredEvidenceAndPendingChanges()
    {
        using var project = new WalkthroughProject(pristine);
        var parserPath = FakeParser.CopyRecordingInvocations(project.ManagedRoot);
        var firstClient = RealCommandClient.Create(project.ManagedRoot, parserPath);

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var capture = await firstClient.CaptureBaselineAsync(
                new BaselineCaptureRequest(project.FwDataPath), CancellationToken.None);
            Assert.True(capture.Succeeded, capture.Refusal?.Message);

            var configured = await firstClient.SetDefaultSelectionAsync(new SetDefaultSelectionRequest(
                project.FwDataPath, "Default", [], ["motifa"]), CancellationToken.None);
            Assert.True(configured.Succeeded, configured.Refusal?.Message);
            var skipped = await firstClient.SkipSetupAsync(
                new SkipSetupRequest(project.FwDataPath), CancellationToken.None);
            Assert.True(skipped.Succeeded, skipped.Refusal?.Message);

            var locator = new ProjectLocator(Path.GetFullPath(project.FwDataPath),
                Path.GetFileNameWithoutExtension(project.FwDataPath));
            using (var database = ProjectMotifDatabase.Open(project.FwDataPath))
            {
                var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(locator))!;
                FakeParser.Behave(Path.GetDirectoryName(baseline.FwDataPath)!, new
                {
                    words = new[] { new { word = "motifa", outcome = "complete" } },
                    traceJson = "{\"type\":\"WordAnalysis\",\"inputShape\":\"motifa\",\"children\":[" +
                        "{\"type\":\"MorphologicalRuleAnalysis\",\"source\":\"SeededRule\",\"children\":[" +
                        "{\"type\":\"Successful\",\"children\":[]}]}]}",
                });
            }

            var assessed = await firstClient.AssessAsync(
                new AssessRequest(project.FwDataPath), new Progress<AssessmentProgress>(), CancellationToken.None);
            Assert.True(assessed.Succeeded, assessed.Refusal?.Message);
            var assessmentId = Assert.Single(assessed.Value!.Measurements,
                measurement => measurement.Kind == AssessmentKinds.ParseTime).AssessmentId;

            var pending = await firstClient.LoadPendingChangesAsync(
                new PendingChangesRequest(project.FwDataPath, MotifProductVersion.CurrentText), CancellationToken.None);
            Assert.True(pending.Succeeded, pending.Refusal?.Message);
            Guid wordformId;
            using (var cache = new FwDataProjectLoader().LoadScratchCache(project.FwDataPath))
            {
                wordformId = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances()
                    .Single(wordform => wordform.Form.VernacularDefaultWritingSystem?.Text ==
                        SeededProject.AnalysedWordForm).Guid;
            }
            var put = await firstClient.PutPendingChangeAsync(new PutPendingChangeRequest(
                project.FwDataPath, MotifProductVersion.CurrentText, pending.Value!.Revision,
                new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                    CanonicalId.FromGuid(wordformId).Value, SeededProject.AnalysedWordForm)), CancellationToken.None);
            Assert.True(put.Succeeded, put.Refusal?.Message);
            var changeId = Assert.Single(put.Value!.Changes).ChangeId;
            var parserInvocationsBeforeOpen = FakeParser.Invocations(parserPath);

            using var reopened = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: parserPath);
            reopened.Show();
            reopened.OpenRecentProjectByClick(project.FwDataPath);
            await reopened.Workspace.Context.EvidencePublication;

            var overview = reopened.Workspace.PageModel<OverviewPageModel>();
            var timing = reopened.Workspace.PageModel<TimingPageModel>();
            var texts = reopened.Workspace.PageModel<TextsPageModel>();
            var review = reopened.Workspace.PageModel<ReviewPageModel>();
            reopened.WaitUntil(
                () => overview.Overview?.AssessmentId == assessmentId && timing.HasStoredTiming &&
                    texts.Assess.Words.AllRows.Count > 0 && review.Changes.Count == 1,
                TimeSpan.FromSeconds(30), "the reopened window did not publish its stored page data");

            Assert.Equal(capture.Value!.Token, reopened.Workspace.Baseline.Token);
            var storedEvidence = reopened.Workspace.Context.Evidence.Assessment;
            Assert.NotNull(storedEvidence);
            Assert.True(storedEvidence.IsStored);
            Assert.Equal(assessmentId, reopened.Workspace.Context.Evidence.ParseTimeAssessmentId);
            Assert.Equal(assessmentId, overview.Overview!.AssessmentId);
            Assert.Equal("100%", overview.TextCoverageMain);
            Assert.Contains("words in the default Selection", overview.TextCoverageWords, StringComparison.Ordinal);
            Assert.Equal("0 of 0", overview.AccuracyMain);
            var overviewText = Assert.Single(reopened.Window.GetLogicalDescendants().OfType<OverviewPage>())
                .GetLogicalDescendants().OfType<TextBlock>().Where(control => control.IsVisible)
                .Select(control => control.Text);
            Assert.Contains(overview.TextCoverageMain, overviewText);
            Assert.Contains(overview.AccuracyMain, overviewText);
            Assert.Equal("motifa", Assert.Single(texts.Assess.Words.AllRows).Word);
            Assert.True(timing.HasStoredTiming);
            Assert.Equal(assessmentId, timing.StoredTiming!.AssessmentId);
            Assert.Equal(changeId, Assert.Single(review.Changes.Items).ChangeId);
            Assert.Equal(changeId, Assert.Single(reopened.Workspace.Context.Changes.Items).ChangeId);
            Assert.False(reopened.Workspace.Context.Setup?.IsOpen == true);
            Assert.Equal(parserInvocationsBeforeOpen, FakeParser.Invocations(parserPath));

            using var verificationDatabase = ProjectMotifDatabase.Open(project.FwDataPath);
            var storedBaseline = new BaselineRepository(verificationDatabase).GetCurrent(ProjectWorkspaceKey.Compute(locator));
            Assert.NotNull(storedBaseline);
            Assert.Equal(capture.Value!.Token.CapturedUtc, storedBaseline.Token.CapturedUtc);
            var storedAssessment = Assert.Single(
                new AssessmentRepository(verificationDatabase).ListBaselineAssessments(AssessmentKinds.ParseTime));
            Assert.Equal(assessmentId, storedAssessment.AssessmentId);
        }, TimeSpan.FromMinutes(3));
    }
}
