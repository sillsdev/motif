using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class PaneWidthBudgetScreenshots
{
    [ScreenshotFact]
    public void ExplicitPaneRequestsShowTheirWorkingPageAtIntermediateWidths()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
            Directory.CreateDirectory(folder);
            var failures = new List<string>();
            foreach (var (width, zoom, theme) in new[] { (800, 100, "light"), (1000, 150, "dark") })
            {
                var prior = Application.Current!.RequestedThemeVariant;
                var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
                try
                {
                    window.Width = width;
                    window.Height = 600;
                    SettingsTestAccess.Model(window)
                        .ZoomPercent = zoom;
                    Application.Current.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
                    workspace.CurrentPage = WorkspacePage.Texts;
                    PageScreenshots.Settle(window);
                    var host = window.FindInWindowOrWorkspace<WorkspaceLayoutHost>("WorkspaceLayout")!;
                    var railEntry = window.GetVisualDescendants().OfType<ListBoxItem>().Single(item =>
                        item.DataContext is PageViewModel { Page: WorkspacePage.Texts });
                    HeadlessClick.Click(window, railEntry, "Texts side panel");
                    PageScreenshots.Settle(window);
                    Assert.True(host.FindControl<Border>("SidePanelBorder")!.IsVisible);
                    HeadlessClick.Click(window, host.FindControl<Button>("InspectorStrip")!, "Show Inspector");
                    PageScreenshots.Settle(window);
                    Assert.True(host.FindControl<Border>("InspectorPane")!.IsVisible);
                    using var frame = window.CaptureRenderedFrame()
                        ?? throw new InvalidOperationException("No rendered pane-request frame was available.");
                    frame.Save(Path.Combine(folder, $"pane-pair-{width}-600-{zoom}-{theme}.png"),
                        PngBitmapEncoderOptions.Default);
                    try
                    {
                        LayoutAssertions.AssertCurrent(window);
                    }
                    catch (Xunit.Sdk.XunitException failure)
                    {
                        failures.Add($"{width}/{zoom}/{theme}: {failure.Message}");
                    }
                }
                finally
                {
                    Application.Current.RequestedThemeVariant = prior;
                    window.Close();
                    await workspace.DisposeAsync();
                }
            }
            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        }, TimeSpan.FromMinutes(2));
    }
}
