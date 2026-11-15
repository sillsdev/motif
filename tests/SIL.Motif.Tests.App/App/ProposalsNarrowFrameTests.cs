using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.VisualTree;
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
                var host = window.FindInWindowOrWorkspace<WorkspaceLayoutHost>("WorkspaceLayout")!;
                host.ShowSidePanel();
                PageScreenshots.Settle(window);
                var model = workspace.PageModel<ProposalsPageModel>();
                var list = page.SidePanel.GetLogicalDescendants().OfType<ListBox>()
                    .Single(control => control.Name == "ProposalsRowsItems");
                var cards = model.Rows.OfType<ProposalCardViewModel>().ToArray();
                var first = cards.First();
                var second = cards.Single(card => card.ProposalId == "proposal/reduplicated-forms");
                Assert.NotSame(first, second);
                model.SelectProposalCommand.Execute(first);
                PageScreenshots.Settle(window);
                var firstRow = Assert.IsType<ListBoxItem>(list.ContainerFromItem(first));
                var secondRow = Assert.IsType<ListBoxItem>(list.ContainerFromItem(second));

                HeadlessClick.Click(window, secondRow, "Choose another Proposal");
                PageScreenshots.Settle(window);

                Assert.Same(second, model.SelectedProposal);
                Assert.True(secondRow.IsSelected);
                Assert.False(firstRow.IsSelected);
                Assert.True(secondRow.IsEffectivelyVisible);
                var status = Assert.Single(secondRow.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == second.StatusWord);
                Assert.Equal(Avalonia.Media.TextWrapping.Wrap, status.TextWrapping);
            }
            finally
            {
                settings.ZoomPercent = previousZoom;
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ReturningToProposalsKeepsTheSelectedProposal()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            ProposalsCaptureFixture.Configure(fake);
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                workspace.CurrentPage = WorkspacePage.Proposals;
                PageScreenshots.Settle(window);
                var model = workspace.PageModel<ProposalsPageModel>();
                var page = window.PageView<ProposalsPage>();
                var list = page.SidePanel.GetLogicalDescendants().OfType<ListBox>()
                    .Single(control => control.Name == "ProposalsRowsItems");
                var second = model.Rows.OfType<ProposalCardViewModel>()
                    .Single(card => card.ProposalId == "proposal/reduplicated-forms");
                model.SelectProposalCommand.Execute(second);
                PageScreenshots.Settle(window);
                Assert.Same(second, model.SelectedProposal);

                workspace.CurrentPage = WorkspacePage.Progress;
                PageScreenshots.Settle(window);
                workspace.CurrentPage = WorkspacePage.Proposals;
                PageScreenshots.Settle(window);

                Assert.Same(second, model.SelectedProposal);
                Assert.Same(second, list.SelectedItem);
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheProposalListCanBeRevealedInANarrowFrame(bool dark)
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
                var page = window.PageView<ProposalsPage>();
                var host = window.FindInWindowOrWorkspace<WorkspaceLayoutHost>("WorkspaceLayout")!;
                Assert.Equal(new Size(640, 600), window.ClientSize);
                Assert.True(page.IsEffectivelyVisible);
                Assert.True(host.FindControl<Button>("SidePanelRevealButton")!.IsEffectivelyVisible);
                host.ShowSidePanel();
                PageScreenshots.Settle(window);
                var list = page.SidePanel.GetLogicalDescendants().OfType<ListBox>()
                    .Single(control => control.Name == "ProposalsRowsItems");
                Assert.True(list.IsEffectivelyVisible);
                Assert.NotEmpty(list.GetRealizedContainers());
                Assert.False(host.FindControl<Border>("InspectorPane")!.IsVisible);
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
