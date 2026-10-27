using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
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
    public void NativeNavigationShowsItsDestinationAndRetainsTheInspection(int zoom, bool settings)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false,
                configure: (fake, _) => fake.OnInspect((request, _) => Task.FromResult(
                    CommandOutcome<InspectResponse>.Success(PageScreenshots.SampleInspection(request.Subject)))));
            try
            {
                window.Width = 640;
                window.Height = 600;
                SettingsTestAccess.Model(window).ZoomPercent = zoom;
                PageScreenshots.Settle(window);
                var morph = PageScreenshots.SampleMorph("wa-");
                var subject = InspectorSubject.Morpheme(morph.AllomorphId, morph.GrammaticalInfoId, morph.Form, "3PL")!;
                workspace.Context.OpenInspector(subject);
                await workspace.Inspector.Loading;
                var host = window.FindInWindowOrWorkspace<WorkspaceLayoutHost>("WorkspaceLayout")!;
                HeadlessClick.Click(window, host.FindControl<Button>("InspectorStrip")!, "Show Inspector");
                PageScreenshots.Settle(window);
                Assert.False(host.FindControl<Grid>("WorkingPageRegion")!.IsVisible);
                if (settings) HeadlessClick.Click(window, window.FindInWindowOrWorkspace<Button>("SettingsButton")!, "Settings");
                else
                {
                    var rail = window.FindInWindowOrWorkspace<ListBox>("PageList")!;
                    var index = rail.Items.OfType<PageViewModel>().ToList().FindIndex(page => page.Page == WorkspacePage.Timing);
                    var item = Assert.IsType<ListBoxItem>(rail.ContainerFromIndex(index));
                    item.BringIntoView();
                    PageScreenshots.Settle(window);
                    HeadlessClick.Click(window, item, "Timing");
                }
                PageScreenshots.Settle(window);
                Capture(window, zoom, settings);
                Assert.Equal(settings ? WorkspacePage.Settings : WorkspacePage.Timing, workspace.CurrentPage);
                Assert.True(host.FindControl<Grid>("WorkingPageRegion")!.IsEffectivelyVisible);
                Assert.False(host.FindControl<Border>("InspectorPane")!.IsVisible);
                Assert.True(workspace.Inspector.IsOpen);
                Assert.Equal(subject, workspace.Context.Inspector!.Subject);
                if (settings) Assert.True(SettingsTestAccess.Page(window).IsEffectivelyVisible);
                HeadlessClick.Click(window, host.FindControl<Button>("InspectorStrip")!, "Show Inspector");
                PageScreenshots.Settle(window);
                Assert.False(host.FindControl<Grid>("WorkingPageRegion")!.IsVisible);
                Assert.True(workspace.Inspector.IsOpen);
                Assert.Equal(subject, workspace.Context.Inspector!.Subject);
            }
            finally
            {
                SettingsTestAccess.Model(window).ZoomPercent = 100;
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    private static void Capture(Window window, int zoom, bool settings)
    {
        if (Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable) is not { } folder) return;
        Directory.CreateDirectory(folder);
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Navigation did not render.");
        frame.Save(Path.Combine(folder, $"focused-navigation-{(settings ? "settings" : "timing")}-640-600-{zoom}.png"),
            PngBitmapEncoderOptions.Default);
    }
}
