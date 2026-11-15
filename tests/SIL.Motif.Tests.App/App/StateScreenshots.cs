using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views.Shell;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Captures interactive shell states in both themes and the supported window sizes.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class StateScreenshots
{
    public static TheoryData<int, int, int, bool> Sizes => new()
    {
        { 640, 600, 100, false }, { 640, 900, 150, false },
        { 1040, 780, 100, false }, { 1280, 800, 150, false },
        { 640, 600, 100, true }, { 640, 900, 150, true },
        { 1040, 780, 100, true }, { 1280, 800, 150, true },
    };

    [ScreenshotTheory]
    [MemberData(nameof(Sizes))]
    public void CaptureHealthAndPageStates(int width, int height, int zoom, bool dark)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
            Directory.CreateDirectory(folder);
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(
                configure: OverviewTimingScreenshots.ReadOverviewAndTiming);
            var previousTheme = Application.Current!.RequestedThemeVariant;
            try
            {
                workspace.Context.UseDiagnosticsPreferences(new MemoryUserPreferencesStore());
                workspace.Context.ToggleDiagnosticsCommand.Execute(null);
                Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                window.Width = width;
                window.Height = height;
                SettingsTestAccess.Model(window).ZoomPercent = zoom;
                foreach (var page in new[] { WorkspacePage.Progress, WorkspacePage.Texts, WorkspacePage.Words,
                             WorkspacePage.GrammarGuide, WorkspacePage.Proposals, WorkspacePage.Diagnostics, WorkspacePage.Settings })
                {
                    workspace.CurrentPage = page;
                    PageScreenshots.Settle(window);
                    PageScreenshots.Save(window, Path.Combine(folder,
                        $"state-{page}-{width}-{height}-{zoom}-{(dark ? "dark" : "light")}.png"));
                }

                workspace.CurrentPage = WorkspacePage.Progress;
                PageScreenshots.Settle(window);
                var strip = window.GetVisualDescendants().OfType<HealthStrip>().Single();
                var button = strip.FindControl<Button>("HealthButton")!;
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point);
                Dispatcher.UIThread.RunJobs();
                Assert.True(button.Flyout!.IsOpen);
                PageScreenshots.Save(window, Path.Combine(folder,
                    $"state-health-hover-{width}-{height}-{zoom}-{(dark ? "dark" : "light")}.png"));
                button.Flyout.Hide();
                Assert.True(button.Focus(NavigationMethod.Tab));
                Dispatcher.UIThread.RunJobs();
                Assert.True(button.Flyout.IsOpen);
                PageScreenshots.Save(window, Path.Combine(folder,
                    $"state-health-focus-{width}-{height}-{zoom}-{(dark ? "dark" : "light")}.png"));
            }
            finally
            {
                Application.Current.RequestedThemeVariant = previousTheme;
                SIL.Motif.App.WindowZoomPolicy.Apply(Application.Current, SIL.Motif.App.ZoomPolicy.DefaultPercent);
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(3));
    }
}
