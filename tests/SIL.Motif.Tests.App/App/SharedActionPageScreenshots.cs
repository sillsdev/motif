using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SharedActionPageScreenshots
{
    [ScreenshotFact]
    public void PageActionsStayVisibleBeforeHoverAcrossTheWindowMatrix()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(
                configure: OverviewTimingScreenshots.ReadOverviewAndTiming);
            var theme = Application.Current!.RequestedThemeVariant;
            var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
            Directory.CreateDirectory(folder);
            var failures = new List<string>();
            try
            {
                workspace.Context.TryWord("hawajafika");
                await workspace.Assess.Trace.TryCommand.ExecutionTask!;
                foreach (var dark in new[] { false, true })
                foreach (var size in new[] { (1920, 1080), (1040, 780), (640, 900), (1040, 600) })
                foreach (var zoom in new[] { 100, 150 })
                {
                    Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                    window.Width = size.Item1;
                    window.Height = size.Item2;
                    SettingsTestAccess.Model(window).ZoomPercent = zoom;
                    foreach (var page in new[] { WorkspacePage.Overview, WorkspacePage.Timing, WorkspacePage.TryAWord })
                    {
                        workspace.CurrentPage = page;
                        window.MouseMove(new Point(1, 1));
                        PageScreenshots.Settle(window);
                        var state = $"actions-{page}-{size.Item1}-{size.Item2}-{zoom}-{(dark ? "dark" : "light")}";
                        using var frame = window.CaptureRenderedFrame()!;
                        frame.Save(Path.Combine(folder, state + ".png"), PngBitmapEncoderOptions.Default);
                        var actions = window.GetVisualDescendants().OfType<Button>().Where(button =>
                            button.IsEffectivelyVisible && button.Classes.Contains("revealOnHover")).ToArray();
                        Assert.NotEmpty(actions);
                        foreach (var action in actions)
                            if (action.Opacity < 1 || !action.IsHitTestVisible)
                                failures.Add($"{state}: {action.Content} is hidden or cannot receive input.");
                    }
                }
                Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
            }
            finally
            {
                Application.Current.RequestedThemeVariant = theme;
                WindowZoomPolicy.Apply(Application.Current, ZoomPolicy.DefaultPercent);
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(5));
    }
}
