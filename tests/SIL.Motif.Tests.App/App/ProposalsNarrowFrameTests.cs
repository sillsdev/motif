using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProposalsNarrowFrameTests
{
    [Theory]
    [InlineData(720, 100)]
    [InlineData(1440, 150)]
    public void ChoosingAnotherProposalMovesTheVisibleSelection(int width, int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            ProposalsCaptureFixture.Configure(fake);
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            var settings = SettingsTestAccess.Model(window);
            var previousZoom = settings.ZoomPercent;
            try
            {
                settings.ZoomPercent = zoom;
                window.Width = width;
                workspace.CurrentPage = WorkspacePage.Proposals;
                PageScreenshots.Settle(window);
                var page = window.PageView<ProposalsPage>();
                var model = workspace.PageModel<ProposalsPageModel>();
                var list = page.FindControl<ListBox>("ProposalsRowsItems")!;
                var first = model.SelectedProposal!;
                var second = model.Rows.OfType<ProposalCardViewModel>()
                    .Single(card => card.ProposalId == "proposal/reduplicated-forms");
                var firstRow = Assert.IsType<ListBoxItem>(list.ContainerFromItem(first));
                var secondRow = Assert.IsType<ListBoxItem>(list.ContainerFromItem(second));

                HeadlessClick.Click(window, secondRow, "Choose another Proposal");
                PageScreenshots.Settle(window);

                Assert.Same(second, model.SelectedProposal);
                Assert.True(secondRow.IsSelected);
                Assert.False(firstRow.IsSelected);
                Assert.True(secondRow.IsEffectivelyVisible);
                var status = Assert.Single(secondRow.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == "Needs another try");
                Assert.Equal(Avalonia.Media.TextWrapping.Wrap, status.TextWrapping);
                Assert.Equal("Needs another try", ToolTip.GetTip(status));
            }
            finally
            {
                settings.ZoomPercent = previousZoom;
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutomaticAndOpenedInspectorKeepTheComposedFrameUsable(bool dark)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var app = Application.Current!;
            var previousTheme = app.RequestedThemeVariant;
            var fake = new FakeCommandClient();
            ProposalsCaptureFixture.Configure(fake);
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            var settings = SettingsTestAccess.Model(window);
            var previousZoom = settings.ZoomPercent;
            try
            {
                app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                settings.ZoomPercent = 150;
                window.Width = 640;
                window.Height = 600;
                workspace.CurrentPage = WorkspacePage.Proposals;
                PageScreenshots.Settle(window);

                Assert.Equal(new Size(640, 600), window.ClientSize);
                var page = window.PageView<ProposalsPage>();
                var model = workspace.PageModel<ProposalsPageModel>();
                Assert.Equal(ProposalsCaptureFixture.SelectedProposalId, model.SelectedProposal?.ProposalId);
                var host = window.FindInWindowOrWorkspace<WorkspaceLayoutHost>("WorkspaceLayout")!;
                Assert.True(page.IsEffectivelyVisible);
                var list = page.FindControl<ListBox>("ProposalsRowsItems")!;
                var row = Assert.IsType<ListBoxItem>(list.ContainerFromItem(model.SelectedProposal!));
                Assert.True(row.IsEffectivelyVisible);
                var title = Assert.Single(row.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Classes.Contains("denseTableItem"));
                Assert.DoesNotContain(title.TextLayout.TextLines, line => line.HasCollapsed);
                var tracks = Assert.Single(row.GetVisualDescendants().OfType<Grid>(),
                    grid => DenseTable.GetColumns(grid) is not null);
                var minimumIdentityWidth = Assert.IsType<double>(app.FindResource("Component.DenseTable.Column.NarrowWidth"));
                Assert.True(tracks.ColumnDefinitions[0].ActualWidth >= minimumIdentityWidth);
                Assert.False(host.FindControl<Border>("InspectorPane")!.IsVisible);
                Assert.True(host.FindControl<Button>("InspectorStrip")!.IsEffectivelyVisible);
                LayoutAssertions.AssertCurrent(window);

                HeadlessClick.Click(window, host.FindControl<Button>("InspectorStrip")!, "Show Inspector");
                PageScreenshots.Settle(window);
                Assert.False(host.FindControl<Grid>("WorkingPageRegion")!.IsVisible);
                Assert.True(host.FindControl<Border>("InspectorPane")!.IsEffectivelyVisible);
                Assert.Contains(host.FindControl<ContentControl>("PageInspectorHost")!.GetVisualDescendants()
                    .OfType<TextBlock>(), text => text.IsEffectivelyVisible &&
                        text.Text == "Nasal assimilation before /p/");
                var back = host.FindControl<Button>("InspectorHideButton")!;
                Assert.Equal("Back to page", back.Content);
                LayoutAssertions.AssertCurrent(window);

                HeadlessClick.Click(window, back, "Back to page");
                PageScreenshots.Settle(window);
                Assert.True(page.IsEffectivelyVisible);
                Assert.Equal(ProposalsCaptureFixture.SelectedProposalId, model.SelectedProposal?.ProposalId);
                LayoutAssertions.AssertCurrent(window);
            }
            finally
            {
                settings.ZoomPercent = previousZoom;
                app.RequestedThemeVariant = previousTheme;
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(3));
    }
}
