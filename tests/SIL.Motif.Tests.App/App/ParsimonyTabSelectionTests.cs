using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the Parsimony tabs' selected state on the rendered window: the Active tab carries the selected state until the
/// Suppressed tab is chosen, and the state moves back when Active is chosen again. The check reads the classes the
/// rendered buttons hold, after a layout pass, so a binding that stops updating fails here.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "Unit")]
public sealed class ParsimonyTabSelectionTests
{
    [Fact]
    public void SelectingSuppressedMovesTheSelectedStateToItAndBackToActive()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false, advancedAiModeEnabled: true,
                configure: (fake, _) => ParsimonyStateScreenshots.Configure(fake, "suppressed-with-reason"));
            try
            {
                workspace.CurrentPage = WorkspacePage.Parsimony;
                var page = workspace.PageModel<ParsimonyPageModel>();
                PageScreenshots.Settle(window);
                Assert.Equal("Active", SelectedTabName(window));

                page.IsShowingSuppressed = true;
                PageScreenshots.Settle(window);
                Assert.Equal("Suppressed", SelectedTabName(window));

                page.ShowActiveCommand.Execute(null);
                PageScreenshots.Settle(window);
                Assert.Equal("Active", SelectedTabName(window));
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void AStagedDecisionLinksToReviewChanges()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false, advancedAiModeEnabled: true,
                configure: (fake, _) => ParsimonyStateScreenshots.Configure(fake, "pending-keep"));
            try
            {
                workspace.CurrentPage = WorkspacePage.Parsimony;
                var page = workspace.PageModel<ParsimonyPageModel>();
                Assert.True(page.SelectFinding(page.Rows.First(row => !row.IsHeader).Link!));
                await page.KeepCommand.ExecuteAsync(null);
                PageScreenshots.Settle(window);

                var link = Assert.Single(window.GetVisualDescendants().OfType<HyperlinkButton>(), button =>
                    AutomationProperties.GetAutomationId(button) == AutomationIds.ParsimonyReviewChanges);
                Assert.True(link.IsEffectivelyVisible);
                Assert.Equal(MotifAppStrings.ParsimonyReviewChanges, link.Content);
                link.Command!.Execute(link.CommandParameter);

                Assert.Equal(WorkspacePage.Review, workspace.CurrentPage);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void FailedAutomaticCheckShowsCopyableAssistantAndCliInstructions()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            FakeCommandClient? commands = null;
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false, advancedAiModeEnabled: true,
                configure: (fake, _) =>
                {
                    commands = fake;
                    ParsimonyStateScreenshots.Configure(fake, "empty");
                    fake.EnqueueParsimonyHandler = (_, _) => Task.FromResult(CommandOutcome<JobEnqueuedResponse>.Success(
                        new JobEnqueuedResponse("job/parsimony-failed", "parsimony-report", "project")));
                    fake.OnWaitForJob(async (request, _, cancellationToken) =>
                    {
                        waiting.TrySetResult();
                        await finish.Task.WaitAsync(cancellationToken);
                        return CommandOutcome<JobStatusResponse>.Success(new JobStatusResponse(request.JobId,
                            "project", true, "parsimony-report", JobStatus.Failed, 1, null, false, null, 1));
                    });
                });
            try
            {
                await waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
                var fake = Assert.IsType<FakeCommandClient>(commands);
                var request = Assert.Single(fake.EnqueueParsimonyRequests);
                Assert.Equal(ParsimonyAdmission.AllMeasures, request.MeasureId);
                Assert.Equal(ParsimonyEvidenceScopeKind.ProjectApproved, request.EvidenceScope);
                Assert.Null(request.AssessmentIds);
                workspace.CurrentPage = WorkspacePage.Parsimony;
                var page = workspace.PageModel<ParsimonyPageModel>();
                PageScreenshots.Settle(window);
                Assert.True(page.IsLoading);
                Assert.False(page.IsListed);
                Assert.Empty(page.Message);
                Assert.False(page.HasNextStep);
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text?.Contains("motif parsimony", StringComparison.Ordinal) == true);

                finish.TrySetResult();
                await page.AutomaticWork.WaitAsync(TimeSpan.FromSeconds(10));
                PageScreenshots.Settle(window);
                Assert.Equal(ParsimonyPageState.Refused, page.State);
                Assert.Equal(MotifAppStrings.ParsimonyJobFailed, page.Message);
                Assert.False(page.IsListed);
                Assert.Empty(page.Rows);
                var guidance = Assert.Single(window.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text?.Contains("motif parsimony", StringComparison.Ordinal) == true);
                Assert.True(guidance.Bounds.Width > 0 && guidance.Bounds.Height > 0);
                var text = guidance.Text ?? string.Empty;
                Assert.Equal(MotifAppStrings.ParsimonyNoReportNextStep, text);
                Assert.Contains("Ask an AI assistant with Motif tools", text, StringComparison.Ordinal);
                Assert.Contains("motif parsimony --project \"<project.fwdata>\" --measure all --wait", text, StringComparison.Ordinal);
                Assert.DoesNotContain("Advanced AI tools", text, StringComparison.Ordinal);
                Assert.Single(fake.EnqueueParsimonyRequests);
                Assert.Empty(fake.AssessRequests);
            }
            finally
            {
                finish.TrySetResult();
                await workspace.DisposeAsync();
                window.Close();
            }
        }, TimeSpan.FromMinutes(2));
    }

    // The one tab with the selected class and the selected background; the other tab is plain.
    private static string SelectedTabName(Window window)
    {
        var tabs = window.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("parsimonyTab")).ToArray();
        Assert.Equal(2, tabs.Length);
        var selected = tabs.Where(tab => tab.Classes.Contains("selected")).ToArray();
        Assert.Single(selected);
        // The plain tab is see-through and the selected one carries a visible fill, so the state reads by eye.
        var plain = tabs.Single(tab => !tab.Classes.Contains("selected"));
        Assert.Equal(0, ((ISolidColorBrush)plain.Background!).Color.A);
        Assert.NotEqual(0, ((ISolidColorBrush)selected[0].Background!).Color.A);
        return AutomationProperties.GetAutomationId(selected[0]) == AutomationIds.ParsimonyActiveTab
            ? "Active"
            : "Suppressed";
    }
}
