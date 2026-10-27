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
    [Theory]
    [InlineData(WorkspacePage.AiAssistants)]
    [InlineData(WorkspacePage.AiHandoff)]
    public void InstructionsFitAtTheMinimumWidthAtEnlargedZoom(WorkspacePage destination) =>
        AiPageTextLayoutChecks.Verify(fixture, destination, false, 640, 600, 150);
}

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class AiPageTextLayoutSystemTests(AiPageLayoutSample fixture) : IClassFixture<AiPageLayoutSample>
{
    public static IEnumerable<object[]> Cells()
    {
        foreach (var page in new[] { WorkspacePage.AiAssistants, WorkspacePage.AiHandoff })
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
            var assistants = workspace.PageModel<AssistantConnectionsPageModel>();
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
                SettleLayout(window);
                Assert.InRange(window.ClientSize.Width, width - 1, width + 1);
                Assert.InRange(window.ClientSize.Height, height - 1, height + 1);
                var page = destination == WorkspacePage.AiAssistants
                    ? (Control)window.PageView<AssistantConnectionsPage>()
                    : window.PageView<AiHandoffPage>();
                if (destination == WorkspacePage.AiAssistants)
                {
                    var model = workspace.PageModel<AssistantConnectionsPageModel>();
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
                        $"ai-text-{destination}-{width}-{height}-{zoom}-{(dark ? "dark" : "light")}.png"));
                }
                LayoutAssertions.AssertCurrent(page);
            }
            finally
            {
                settings.ZoomPercent = oldZoom;
                Application.Current!.RequestedThemeVariant = oldTheme;
                assistants.IsEnabled = oldEnabled;
                workspace.CurrentPage = WorkspacePage.Overview;
                SettleLayout(window);
            }
        }, TimeSpan.FromMinutes(2));
    }

    private static void SettleLayout(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
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
            (_workspace, _window) = await PageScreenshots.OpenOverSampleData(parse: false);
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
