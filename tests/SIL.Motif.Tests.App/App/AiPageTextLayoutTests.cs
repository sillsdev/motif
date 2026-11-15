using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class AiPageTextLayoutTests(AiPageLayoutSample fixture) : IClassFixture<AiPageLayoutSample>
{
    [Fact]
    public void DiagnosticsRemainsAvailableWhenAssistantToolsAreOff()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await fixture.GetAsync();
            var assistants = workspace.PageModel<SettingsPageModel>().Connections;
            var oldEnabled = assistants.IsEnabled;
            var oldDiagnostics = workspace.Context.Diagnostics;
            try
            {
                assistants.IsEnabled = false;
                workspace.ShowPageCommand.Execute(WorkspacePage.Settings);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var page = window.PageView<SettingsPage>();
                Assert.DoesNotContain(page.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    text => text.IsEffectivelyVisible &&
                        text.Text == "Parsimony is available when Advanced AI mode is on.");
                workspace.Context.Diagnostics = true;
                Assert.True(workspace.Context.CanOpenPage(WorkspacePage.Diagnostics));
            }
            finally
            {
                assistants.IsEnabled = oldEnabled;
                workspace.Context.Diagnostics = oldDiagnostics;
                workspace.CurrentPage = WorkspacePage.Progress;
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData(WorkspacePage.Settings)]
    public void InstructionsFitAtTheMinimumWidthAtEnlargedZoom(WorkspacePage destination) =>
        AiPageTextLayoutChecks.Verify(fixture, destination, false, 640, 600, 150);
}

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class AiPageTextLayoutSystemTests(AiPageLayoutSample fixture) : IClassFixture<AiPageLayoutSample>
{
    public static IEnumerable<object[]> Cells()
    {
        foreach (var page in new[] { WorkspacePage.Settings })
        // The 640x600 zoomed-in cell is the Unit case above; light theme only, since the text arrangement is the check.
        foreach (var (width, height, zoom) in new[] { (820, 600, 150), (1040, 780, 100) })
            yield return [page, false, width, height, zoom];
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void InstructionsFitTheirArrangedTextAtNarrowAndScaledWindowSizes(
        WorkspacePage destination, bool dark, int width, int height, int zoom) =>
        AiPageTextLayoutChecks.Verify(fixture, destination, dark, width, height, zoom);
}

internal static class AiPageTextLayoutChecks
{
    internal static void Verify(AiPageLayoutSample fixture, WorkspacePage destination, bool dark, int width, int height, int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await fixture.GetAsync();
            var settings = SettingsPageTests.WindowSettings(window);
            var oldZoom = settings.ZoomPercent;
            var oldTheme = Application.Current!.RequestedThemeVariant;
            var assistants = workspace.PageModel<SettingsPageModel>().Connections;
            var oldEnabled = assistants.IsEnabled;
            try
            {
                Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                assistants.IsEnabled = true;
                workspace.ShowPageCommand.Execute(destination);
                window.MinWidth = 0;
                window.MinHeight = 0;
                settings.ZoomPercent = zoom;
                window.Width = width;
                window.Height = height;
                PageScreenshots.SettleLayout(window);
                Assert.InRange(window.ClientSize.Width, width - 1, width + 1);
                Assert.InRange(window.ClientSize.Height, height - 1, height + 1);
                var page = window.PageView<SettingsPage>();
                if (destination == WorkspacePage.Settings)
                {
                    var model = workspace.PageModel<SettingsPageModel>().Connections;
                    Assert.True(model.IsEnabled);
                    Assert.True(model.HasNoAssistants);
                    Assert.Contains(page.GetVisualDescendants().OfType<CopyableTextBlock>(),
                        text => text.IsEffectivelyVisible && text.Text == model.EmptyAssistantText);
                }
                if (Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable) is { Length: > 0 } folder)
                {
                    using var frame = window.CaptureRenderedFrame() ??
                        throw new InvalidOperationException("The AI page did not render.");
                    frame.Save(Path.Combine(folder,
                        $"ai-text-{destination}-{width}-{height}-{zoom}-{(dark ? "dark" : "light")}.png"),
                        Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                }
                LayoutAssertions.AssertCurrent(page);
            }
            finally
            {
                settings.ZoomPercent = oldZoom;
                Application.Current!.RequestedThemeVariant = oldTheme;
                assistants.IsEnabled = oldEnabled;
                workspace.CurrentPage = WorkspacePage.Progress;
                PageScreenshots.SettleLayout(window);
            }
        }, TimeSpan.FromMinutes(2));
    }

}

/// <summary>Shares one pre-parse sample window between AI layout cases in a test class.</summary>
public sealed class AiPageLayoutSample : IDisposable
{
    private WorkspaceShellViewModel? _workspace;
    private MainWindow? _window;

    internal async Task<(WorkspaceShellViewModel Workspace, MainWindow Window)> GetAsync()
    {
        if (_window is null)
        {
            (_workspace, _window) = await PageScreenshots.OpenOverSampleData(parse: false);
            WindowLeakNetAttribute.Keep(_window);
        }
        return (_workspace!, _window);
    }

    public void Dispose()
    {
        if (_window is null) return;
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            _window.Close();
            await _workspace!.DisposeAsync();
        }, TimeSpan.FromMinutes(1));
    }
}
