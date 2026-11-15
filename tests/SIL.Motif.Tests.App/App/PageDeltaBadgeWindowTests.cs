using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class PageDeltaBadgeWindowTests
{
    [Fact]
    public void ProposalEvidenceDoesNotAddGlobalDeltaBadgesToTheRail()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = FakeComposedWindow.Create();
            try
            {
                window.Show();
                workspace.PageModel<ProposalsPageModel>().BadgeCount = 2;
                Assert.Null(typeof(PageViewModel).GetProperty("DeltaBadge"));
                PageScreenshots.Settle(window);
                var rail = window.FindInWindowOrWorkspace<ListBox>("PageList")!;
                Assert.DoesNotContain(rail.GetVisualDescendants().OfType<Border>(),
                    border => border.Classes.Contains("proposalDelta"));
                var texts = rail.GetVisualDescendants().OfType<ListBoxItem>().First(item =>
                    item.DataContext is PageViewModel { Page: WorkspacePage.Texts });
                Assert.Equal("Texts", ToolTip.GetTip(texts));
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromSeconds(30));
    }
}
