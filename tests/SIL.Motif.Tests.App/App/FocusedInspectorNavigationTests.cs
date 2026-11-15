using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class FocusedInspectorNavigationTests
{
    [Theory]
    [InlineData(100, false)]
    [InlineData(100, true)]
    [InlineData(150, false)]
    [InlineData(150, true)]
    public void NativeNavigationShowsItsDestinationAtNarrowWidths(int zoom, bool settings)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
            try
            {
                window.Width = 640;
                window.Height = 600;
                SettingsTestAccess.Model(window).ZoomPercent = zoom;
                PageScreenshots.Settle(window);
                if (settings) HeadlessClick.Click(window, window.FindInWindowOrWorkspace<Button>("SettingsButton")!, "Settings");
                else
                {
                    var rail = window.FindInWindowOrWorkspace<ListBox>("PageList")!;
                    var index = rail.Items.OfType<PageViewModel>().ToList().FindIndex(page => page.Page == WorkspacePage.Words);
                    var item = Assert.IsType<ListBoxItem>(rail.ContainerFromIndex(index));
                    item.BringIntoView();
                    PageScreenshots.Settle(window);
                    HeadlessClick.Click(window, item, "Words");
                }
                PageScreenshots.Settle(window);
                Assert.Equal(settings ? WorkspacePage.Settings : WorkspacePage.Words, workspace.CurrentPage);
                var host = window.FindInWindowOrWorkspace<WorkspaceLayoutHost>("WorkspaceLayout")!;
                Assert.True(host.FindControl<Grid>("WorkingPageRegion")!.IsEffectivelyVisible);
                if (settings) Assert.True(SettingsTestAccess.Page(window).IsEffectivelyVisible);
            }
            finally
            {
                SettingsTestAccess.Model(window).ZoomPercent = 100;
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }
}
