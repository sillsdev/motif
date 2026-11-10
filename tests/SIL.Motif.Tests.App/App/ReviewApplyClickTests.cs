using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Clicks through Apply in the real Review panel: ask, then confirm or cancel.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ReviewApplyClickTests
{
    private const string ProposalId = "proposal/click";

    private static readonly BaselineToken Token = new("project-id", "sha256:" + new string('1', 64),
        "projection-v1", "2026-09-24T10:00:00Z", "sha256:" + new string('a', 64));

    [Fact]
    public void ClickingApplyAsksAndConfirmingWritesOnce()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = Fake();
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                var page = await ShowAsync(workspace, window, fake);
                var apply = ButtonWith(window, AutomationIds.ApplyChanges);
                Assert.True(apply.IsEffectivelyEnabled);
                HeadlessClick.Click(window, apply, MotifAppStrings.ReviewApply);
                PageScreenshots.Settle(window);

                Assert.True(page.IsConfirmingApply);
                Assert.Empty(fake.ApplyProposalRequests);
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == page.ApplyQuestionText);
                var cancel = ButtonWith(window, AutomationIds.ApplyConfirmCancel);
                Assert.True(cancel.IsDefault);
                var confirm = ButtonWith(window, AutomationIds.ApplyConfirm);
                HeadlessClick.Click(window, confirm, MotifAppStrings.ReviewApply);
                await workspace.PageModel<ReviewPageModel>().ApplyCommand.ExecutionTask!;

                Assert.Single(fake.ApplyProposalRequests);
                Assert.False(page.IsConfirmingApply);
                Assert.True(page.HasReceipt);
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ClickingCancelSendsNothingAndClosesTheCard()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = Fake();
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                var page = await ShowAsync(workspace, window, fake);
                HeadlessClick.Click(window, ButtonWith(window, AutomationIds.ApplyChanges), MotifAppStrings.ReviewApply);
                PageScreenshots.Settle(window);
                Assert.True(page.IsConfirmingApply);

                HeadlessClick.Click(window, ButtonWith(window, AutomationIds.ApplyConfirmCancel), MotifAppStrings.Cancel);
                PageScreenshots.Settle(window);

                Assert.False(page.IsConfirmingApply);
                Assert.Empty(fake.ApplyProposalRequests);
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button =>
                    button.IsEffectivelyVisible &&
                    AutomationProperties.GetAutomationId(button) == AutomationIds.ApplyConfirm);
                Assert.True(ButtonWith(window, AutomationIds.ApplyChanges).IsEffectivelyVisible);
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(3));
    }

    private static Button ButtonWith(Avalonia.Visual window, string automationId) =>
        Assert.Single(window.GetVisualDescendants().OfType<Button>(), button =>
            button.IsEffectivelyVisible && AutomationProperties.GetAutomationId(button) == automationId);

    private static async Task<ReviewPageModel> ShowAsync(
        WorkspaceShellViewModel workspace, Window window, FakeCommandClient fake)
    {
        await workspace.Context.OpenProposalAsync(ProposalId, CancellationToken.None);
        var page = workspace.PageModel<ReviewPageModel>();
        workspace.CurrentPage = WorkspacePage.Review;
        await page.LoadReviewAsync();
        PageScreenshots.Settle(window);
        return page;
    }

    private static FakeCommandClient Fake()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one", [], []));
        fake.ReadTrialCompletesWith(new TrialRecord("job/trial", ProposalId, "sha256:intent", Token,
            null, new Dictionary<string, string> { [AssessmentKinds.ParseTime] = "assessment/trial" }, ["dogs"], false,
            null, JobStatus.Completed, DateTimeOffset.UtcNow, TrialFreshness.Current));
        fake.Review = new ProposalReviewResponse(ProposalId, "sha256:intent", "sha256:revision",
            [new GrammarChangeEntry("rule", 0, "Voicing rule", ["op-rule"], [], "digest-rule", false,
                "AuthorPhonologicalRule", "1 rule", [], [], [])], null, null, [], 0)
        { EvidenceDigest = "sha256:evidence", Label = "Sound change" };
        var applied = new ApplyProjection(ProposalId, false, "Applied", [], "sha256:effect",
            new AppliedLogEntrySummary(ProposalId, "2026-01-01", "Motif", "sha256:intent"));
        fake.ApplyProposalHandler = (_, _) =>
            Task.FromResult(CommandOutcome<ApplyPendingResult>.Success(ApplyPendingResult.AppliedWith(applied, "ok")));
        return fake;
    }
}
