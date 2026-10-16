using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ReviewWorkspacePaneTests
{
    [Fact]
    public void ForPageOwnsReviewPanesWithoutGetterReparentingAndReusesTheRealWordCard()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = ReviewDenseTableTests.MixedChanges();
            var (workspace, main) = await ReviewScreenshots.OpenAsync(fake);
            var standalone = new Window { Width = 1600, Height = 1000 };
            try
            {
                workspace.CurrentPage = WorkspacePage.Review;
                var page = workspace.PageModel<ReviewPageModel>();
                await page.LoadReviewAsync();
                page.SelectEntry(Assert.Single(page.Entries, entry => entry.Change is not null));
                var panel = new ReviewPanel(page);
                Assert.DoesNotContain(panel.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetAutomationId(button) == "motif-apply-changes");
                var side = panel.SidePanel;
                var inspector = panel.InspectorContent;
                var footer = panel.InspectorFooter;
                Assert.All(new[] { side, inspector, footer }, pane =>
                {
                    Assert.Null(pane.Parent);
                    Assert.Same(page, pane.DataContext);
                });
                Assert.IsNotType<ScrollViewer>(footer);
                var host = WorkspaceLayoutHost.ForPage(panel);
                standalone.Content = host;
                standalone.Show();
                PageScreenshots.Settle(standalone);
                var parents = new[] { side.Parent, inspector.Parent, footer.Parent };
                Assert.Same(side, panel.SidePanel);
                Assert.Same(inspector, panel.InspectorContent);
                Assert.Same(footer, panel.InspectorFooter);
                Assert.Equal(parents, new[] { side.Parent, inspector.Parent, footer.Parent });
                var card = Assert.Single(inspector.GetVisualDescendants()
                    .OfType<SIL.Motif.App.Controls.WordPresentation.WordCard>());
                Assert.Equal(page.Current!.Change!.Presentation.Key, card.Document!.Key);
                Assert.True(ApplyButton(standalone).IsEffectivelyVisible);
                AssertVisibleDisabledReason(standalone, page);
                host.Page = new Border();
                host.SetPagePanels(null);
                Assert.All(new[] { side, inspector, footer }, pane => Assert.Null(pane.Parent));
                host.Page = panel;
                host.SetPagePanels(panel);
                PageScreenshots.Settle(standalone);
                Assert.Same(footer, host.FindControl<ContentControl>("InspectorFooterHost")!.Content);
                Assert.Same(inspector, host.FindControl<ContentControl>("PageInspectorHost")!.Content);
                Assert.Equal(page.Current.Change.Presentation.Key,
                    Assert.Single(inspector.GetVisualDescendants()
                        .OfType<SIL.Motif.App.Controls.WordPresentation.WordCard>()).Document!.Key);
                LayoutAssertions.AssertCurrent(standalone);
                Assert.Empty(fake.ApplyPendingRequests);
            }
            finally
            {
                standalone.Close();
                main.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void EmptyReviewKeepsApplyAndItsReasonVisible(bool exported, bool undoAll)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.PendingChangesIs(undoAll ? OneChange() : new PendingChangesSnapshot(null, "none", [], []));
            fake.OnDraftDiscarded = _ => fake.PendingChangesIs(new PendingChangesSnapshot(null, "none", [], []));
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            var shellWindow = new Window { Width = 400, Height = 780 };
            try
            {
                workspace.CurrentPage = WorkspacePage.Review;
                PageScreenshots.Settle(window);
                var panel = Assert.Single(window.GetVisualDescendants().OfType<ReviewPanel>());
                var page = workspace.PageModel<ReviewPageModel>();
                Window host = window;
                if (exported)
                {
                    shellWindow.Width = 1600;
                    shellWindow.Content = WorkspaceLayoutHost.ForPage(new ReviewPanel(page));
                    shellWindow.Show();
                    host = shellWindow;
                }
                PageScreenshots.Settle(host);
                var apply = ApplyButton(host);
                if (undoAll)
                {
                    Assert.True(apply.IsEffectivelyVisible);
                    await page.UndoAllCommand.ExecuteAsync(null);
                    Assert.Single(fake.DiscardDraftRequests);
                    PageScreenshots.Settle(host);
                }

                Assert.True(page.ShowsEmptyState);
                Assert.Empty(page.Entries);
                Assert.Equal("Choose a change in Texts to begin.", page.ApplyDisabledReason);
                Assert.Same(apply, ApplyButton(host));
                AssertVisibleDisabledReason(host, page);
            }
            finally
            {
                shellWindow.Close();
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void MeasuringExplainsDisabledApplyUntilCompleteOrCancelled(bool exported, bool cancel)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var completion = new TaskCompletionSource<CommandOutcome<MeasurePendingResult>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var fake = new FakeCommandClient();
            fake.PendingChangesIs(OneChange());
            fake.MeasurePendingHandler = (_, token) =>
            {
                token.Register(() => completion.TrySetResult(CommandOutcome<MeasurePendingResult>.Refused(
                    new Refusal("job.wait-cancelled", FailureReason.Cancelled, "The check was cancelled."))));
                return completion.Task;
            };
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            var shellWindow = new Window { Width = 400, Height = 780 };
            Task? measuring = null;
            try
            {
                workspace.CurrentPage = WorkspacePage.Review;
                PageScreenshots.Settle(window);
                var panel = Assert.Single(window.GetVisualDescendants().OfType<ReviewPanel>());
                var page = workspace.PageModel<ReviewPageModel>();
                Window host = window;
                if (exported)
                {
                    shellWindow.Width = 1600;
                    shellWindow.Content = WorkspaceLayoutHost.ForPage(new ReviewPanel(page));
                    shellWindow.Show();
                    host = shellWindow;
                }
                PageScreenshots.Settle(host);
                measuring = page.MeasureCommand.ExecuteAsync(null);
                PageScreenshots.Settle(host);

                Assert.Single(fake.MeasurePendingRequests);
                Assert.False(measuring.IsCompleted);
                Assert.True(page.IsMeasuring);
                Assert.Equal("Checking these changes…", page.ApplyDisabledReason);
                AssertVisibleDisabledReason(host, page);

                if (cancel)
                {
                    var cancelButton = Assert.Single(host.GetVisualDescendants().OfType<Button>(),
                        button => button.IsEffectivelyVisible && button.Command == page.CancelMeasureCommand);
                    Assert.Equal("Cancel", cancelButton.Content);
                    Assert.True(cancelButton.IsEnabled);
                    cancelButton.Command!.Execute(null);
                }
                else completion.SetResult(CommandOutcome<MeasurePendingResult>.Success(
                    SIL.Motif.Tests.Support.TestFixtures.PendingMeasureResponseFactory.Create(
                        "job/one", "revision/one", FakeCommandClient.CompleteNumbers)));
                await measuring.WaitAsync(TimeSpan.FromSeconds(15));
                PageScreenshots.Settle(host);

                Assert.False(page.IsMeasuring);
                if (cancel)
                {
                    Assert.NotNull(page.MeasurementRefusal);
                    Assert.Equal("See what applying does to the numbers before applying.", page.ApplyDisabledReason);
                    AssertVisibleDisabledReason(host, page);
                }
                else
                {
                    Assert.True(page.CanApply);
                    Assert.True(ApplyButton(host).IsEnabled);
                    Assert.Null(page.ApplyDisabledReason);
                    Assert.False(DisabledReason(host).IsEffectivelyVisible);
                }
            }
            finally
            {
                completion.TrySetResult(CommandOutcome<MeasurePendingResult>.Refused(
                    new Refusal("job.wait-cancelled", FailureReason.Cancelled, "The check was cancelled.")));
                if (measuring is not null) await measuring.WaitAsync(TimeSpan.FromSeconds(15));
                shellWindow.Close();
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MeasuredChangeWithoutAFitResultExplainsDisabledApply(bool exported)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.PendingChangesIs(OneChange() with { FitSummary = [] });
            fake.MeasurePendingCompletesWith(
                SIL.Motif.Tests.Support.TestFixtures.PendingMeasureResponseFactory.Create(
                    "job/one", "revision/one", FakeCommandClient.CompleteNumbers));
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            var shellWindow = new Window { Width = 400, Height = 780 };
            try
            {
                workspace.CurrentPage = WorkspacePage.Review;
                PageScreenshots.Settle(window);
                var panel = Assert.Single(window.GetVisualDescendants().OfType<ReviewPanel>());
                var page = workspace.PageModel<ReviewPageModel>();
                Window host = window;
                if (exported)
                {
                    shellWindow.Width = 1600;
                    shellWindow.Content = WorkspaceLayoutHost.ForPage(new ReviewPanel(page));
                    shellWindow.Show();
                    host = shellWindow;
                }
                PageScreenshots.Settle(host);
                await page.MeasureCommand.ExecuteAsync(null);
                PageScreenshots.Settle(host);

                Assert.False(page.CanApply);
                Assert.True(page.IsApplyBlocked);
                Assert.Equal("Apply is blocked by 1 change", page.ApplyBlockedTitle);
                Assert.Equal("Refresh to check whether every change still fits the project.", page.ApplyDisabledReason);
                AssertVisibleDisabledReason(host, page);
                Assert.Empty(fake.ApplyPendingRequests);
            }
            finally
            {
                shellWindow.Close();
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    private static PendingChangesSnapshot OneChange() => new("draft/one", "revision/one",
        [new PendingChange("one", "wordform/one", "kitabu", "approve", "assessment/one", "book", ["op/one"])],
        [new ChangeFit("one", true, [])]);

    private static Button ApplyButton(Window host) => Assert.Single(host.GetVisualDescendants().OfType<Button>(),
        button => AutomationProperties.GetAutomationId(button) == "motif-apply-changes");

    private static CopyableTextBlock DisabledReason(Window host) =>
        Assert.Single(host.GetVisualDescendants().OfType<CopyableTextBlock>(),
            text => AutomationProperties.GetName(text) == "Why Apply is unavailable");

    private static void AssertVisibleDisabledReason(Window host, ReviewPageModel page)
    {
        var apply = ApplyButton(host);
        Assert.True(apply.IsEffectivelyVisible);
        Assert.False(apply.IsEnabled);
        var reason = DisabledReason(host);
        Assert.True(reason.IsEffectivelyVisible);
        Assert.False(string.IsNullOrWhiteSpace(reason.Text));
        Assert.Equal(page.ApplyDisabledReason, reason.Text);
        Assert.True(reason.Bounds.Width > 0 && reason.Bounds.Height > 0);
    }

    [Fact]
    public void ForPageRestoresTheSameApplyControlAcrossAttachmentAndWidthChanges()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.PendingChangesIs(OneChange());
            var (workspace, main) = await ReviewScreenshots.OpenAsync(fake);
            var panel = new ReviewPanel(workspace.PageModel<ReviewPageModel>());
            var host = WorkspaceLayoutHost.ForPage(panel);
            var window = new Window { Width = 1600, Height = 780, Content = host };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var apply = ApplyButton(window);
                var footer = panel.InspectorFooter;
                Assert.Same(footer, host.FindControl<ContentControl>("InspectorFooterHost")!.Content);
                window.Content = new Border();
                PageScreenshots.Settle(window);
                window.Content = host;
                PageScreenshots.Settle(window);
                Assert.Same(apply, ApplyButton(window));
                window.Width = 800;
                PageScreenshots.Settle(window);
                Assert.Same(footer, host.FindControl<ContentControl>("PageActionFooterHost")!.Content);
                Assert.Same(apply, ApplyButton(window));
                Assert.True(apply.IsEffectivelyVisible);
                var parent = footer.Parent;
                Assert.Same(footer, panel.InspectorFooter);
                Assert.Same(parent, footer.Parent);
                AssertVisibleDisabledReason(window, panel.Page);
                Assert.Empty(fake.ApplyPendingRequests);
            }
            finally
            {
                window.Close();
                main.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ShowThemFindsBothKindsOfBlockedChangesWithoutRemovingTheOthers()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            PendingChange Change(string id) => new(id, "wordform/" + id, id, "approve", "assessment/one", id, ["op/" + id]);
            var fake = new FakeCommandClient();
            fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
                [Change("fits"), Change("gone"), Change("uncertain")],
                [new ChangeFit("fits", true, []), new ChangeFit("gone", false, ["Wordform wordform/gone was deleted."]),
                 new ChangeFit("uncertain", ChangeFitStatus.Uncertain, ["The sentence changed."])]));
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                workspace.CurrentPage = WorkspacePage.Review;
                PageScreenshots.Settle(window);
                var panel = Assert.Single(window.GetVisualDescendants().OfType<ReviewPanel>());
                var page = workspace.PageModel<ReviewPageModel>();
                var inspector = panel.InspectorContent;
                var show = Assert.Single(panel.InspectorFooter.GetVisualDescendants().OfType<HyperlinkButton>(),
                    button => button.Content?.ToString() == "Show them");
                show.Command!.Execute(null);
                PageScreenshots.Settle(window);
                PageScreenshots.Settle(window);
                Assert.Equal(["gone", "uncertain"], page.ReviewEntries.Where(entry => entry.Change is not null)
                    .Select(entry => entry.Change!.ChangeId));
                Assert.Equal("gone", page.Current!.Key);
                Assert.Contains(inspector.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    text => text.Text == "No longer fits");
                await page.NextCommand.ExecuteAsync(null);
                PageScreenshots.Settle(window);
                Assert.Equal("uncertain", page.Current!.Key);
                Assert.Contains(inspector.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    text => text.Text == "Needs another look");
                Assert.Contains(inspector.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    text => text.Text == page.Current.Title);
                var navigator = Assert.IsAssignableFrom<IWorkspacePaneProvider>(panel).SidePanel!;
                Assert.Single(navigator.GetVisualDescendants().OfType<ComboBox>()).SelectedIndex = 2;
                Assert.Equal(["fits"], page.ReviewEntries.Where(entry => entry.Change is not null)
                    .Select(entry => entry.Change!.ChangeId));
                Assert.Equal(3, page.Changes.Items.Count);
                Assert.Equal("3 changes not applied yet", page.CountText);
                Assert.False(page.CanApply);
                Assert.Empty(fake.PendingRemoveRequests);
                Assert.Empty(fake.ReviewMarkRequests);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void MainWindowAndForPageUseBoundReviewPanesWithoutGetterSideEffects()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.PendingChangesIs(OneChange());
            var (workspace, main) = await ReviewScreenshots.OpenAsync(fake);
            var standalone = new Window { Width = 1600, Height = 780 };
            try
            {
                workspace.CurrentPage = WorkspacePage.Review;
                PageScreenshots.Settle(main);
                var page = workspace.PageModel<ReviewPageModel>();
                var panel = Assert.Single(main.GetVisualDescendants().OfType<ReviewPanel>());
                var provider = Assert.IsAssignableFrom<IWorkspacePaneProvider>(panel);
                var side = provider.SidePanel!;
                var inspector = provider.InspectorContent!;
                var footer = provider.InspectorFooter!;
                var parents = new[] { side.Parent, inspector.Parent, footer.Parent };
                Assert.Equal("Review changes", provider.SidePanelTitle);
                Assert.All(new[] { side, inspector, footer }, pane => Assert.Same(page, pane.DataContext));
                Assert.Same(side, provider.SidePanel);
                Assert.Same(inspector, provider.InspectorContent);
                Assert.Same(footer, provider.InspectorFooter);
                Assert.Equal(parents, new[] { side.Parent, inspector.Parent, footer.Parent });
                AssertVisibleDisabledReason(main, page);
                var standalonePanel = new ReviewPanel(page);
                standalone.Content = WorkspaceLayoutHost.ForPage(standalonePanel);
                standalone.Show();
                PageScreenshots.Settle(standalone);
                AssertVisibleDisabledReason(standalone, page);
                Assert.Contains(standalonePanel.SidePanel.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    text => text.Text == "0 of 1 looked at");
                Assert.DoesNotContain(standalonePanel.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetAutomationId(button) == "motif-apply-changes");
                Assert.Empty(fake.ApplyPendingRequests);
            }
            finally
            {
                standalone.Close();
                main.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }
}
