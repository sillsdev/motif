using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the delta badge in the real sidebar: a page that has read its side while a Proposal is open shows its signed
/// change, named for what it counts, a page without one shows none, and leaving the Proposal takes it away. It also
/// saves the sidebar with three badges in Light and Dark.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class PageDeltaBadgeWindowTests
{
    private static ListBoxItem EntryOf(MainWindow window, WorkspacePage page) => window.GetVisualDescendants()
        .OfType<ListBoxItem>().First(entry => entry.DataContext is PageViewModel { } model && model.Page == page);

    private static Border? DeltaOf(MainWindow window, WorkspacePage page)
    {
        var item = window.GetVisualDescendants().OfType<ListBoxItem>()
            .First(entry => entry.DataContext is PageViewModel { } model && model.Page == page);
        return item.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("proposalDelta"));
    }

    [Fact]
    public void ThePageThatReadItsSideShowsItsDeltaNamedForWhatItCountsAndLeavingTheProposalTakesItAway()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await TextsProposalTabsWindowTests.OpenedAsync(TextsTab.Matrix);
            try
            {
                var texts = workspace.PageModel<TextsPageModel>();
                var delta = Assert.IsType<PageDelta>(texts.ProposalDelta);
                PageScreenshots.Settle(window);

                var badge = DeltaOf(window, WorkspacePage.Texts)!;
                Assert.True(badge.IsEffectivelyVisible);
                Assert.Equal(delta.Text, TextsProposalTabsWindowTests.Texts(badge).Single());
                Assert.Equal(delta.AccessibleName, AutomationProperties.GetName(badge));
                Assert.Equal("Texts: " + delta.AccessibleName, ToolTip.GetTip(EntryOf(window, WorkspacePage.Texts)));
                Assert.Equal("Overview", ToolTip.GetTip(EntryOf(window, WorkspacePage.Overview)));
                Assert.False(DeltaOf(window, WorkspacePage.Overview)!.IsEffectivelyVisible);
                Assert.False(DeltaOf(window, WorkspacePage.Timing)!.IsEffectivelyVisible);

                await workspace.Context.LeaveProposalAsync();
                PageScreenshots.Settle(window);
                Assert.False(DeltaOf(window, WorkspacePage.Texts)!.IsEffectivelyVisible);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [ScreenshotFact]
    public void CaptureTheSidebarWithThreeDeltaBadgesInLightAndDark()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await TextsProposalTabsWindowTests.OpenedAsync(TextsTab.Matrix);
            try
            {
                workspace.PageModel<WarningsPageModel>().ProposalDelta = PageDelta.Of(-2, PageDeltaKind.Warnings);
                workspace.PageModel<TimingPageModel>().ProposalDelta = PageDelta.Of(2, PageDeltaKind.WorkPercent);
                foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
                {
                    Application.Current!.RequestedThemeVariant = variant;
                    window.Width = 1240;
                    window.Height = 1000;
                    PageScreenshots.Save(window, Path.Combine(folder, $"sidebar-deltas-{theme}.png"));
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }
}
