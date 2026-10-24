using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class LongProjectTitleTests
{
    public static TheoryData<int, int, int, bool, bool> Cases
    {
        get
        {
            var cases = new TheoryData<int, int, int, bool, bool>();
            foreach (var (width, height) in new[] { (640, 600), (960, 1040), (1920, 1080), (1600, 600) })
            foreach (var zoom in new[] { 100, 150 })
            foreach (var dark in new[] { false, true })
                cases.Add(width, height, zoom, dark, false);
            cases.Add(640, 600, 150, false, true);
            cases.Add(640, 600, 150, true, true);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void LongProjectNamesLeaveTheParseChoiceVisibleAndClickable(
        int width, int height, int zoom, bool dark, bool windowsShaped)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            var settings = SettingsTestAccess.Model(window);
            var priorTheme = Application.Current!.RequestedThemeVariant;
            try
            {
                if (!windowsShaped)
                {
                    var name = "The northern coastal grammar project with a deliberately long descriptive project name.fwdata";
                    await workspace.SetProjectAsync(Path.Combine(Path.GetTempPath(), name));
                    workspace.Context.Setup?.SkipCommand.Execute(null);
                }
                Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                window.Width = width;
                window.Height = height;
                settings.ZoomPercent = zoom;
                workspace.CurrentPage = WorkspacePage.Warnings;
                PageScreenshots.Settle(window);
                Assert.True(workspace.ShowsChooseWhatToParseAction);
                Assert.Null(window.FindControl<Button>("ChooseWhatToParseButton"));
                var chip = ProjectCardAccess.Chip(window);
                var refresh = window.FindControl<Button>("RefreshIconButton")!;
                var nameText = window.FindControl<TextBlock>("ProjectNameLabel")!;
                var mainMenu = window.FindControl<Menu>("MainMenu")!;
                AssertInside(window, mainMenu);
                Assert.True(Bounds(mainMenu, window).Right <= Bounds(chip, window).Left + 1,
                    "The project chip overlaps the menu bar.");
                Capture(window, width, height, zoom, dark, windowsShaped);
                Assert.Equal(workspace.ProjectName, nameText.Text);
                Assert.Equal(workspace.ProjectChipAccessibleName, ToolTip.GetTip(chip));
                Assert.Contains(workspace.ProjectName, workspace.ProjectChipAccessibleName, StringComparison.Ordinal);
                AssertInside(window, chip);
                AssertInside(window, refresh);
                Assert.Same(workspace.RefreshCommand, refresh.Command);
                Assert.Equal(workspace.RefreshCommand.CanExecute(null), refresh.IsEffectivelyEnabled);
                var nameBounds = Bounds(nameText, window);
                Assert.True(Bounds(chip, window).Inflate(1).Contains(nameBounds),
                    $"Project text {nameBounds} exceeds its chip {Bounds(chip, window)}.");
                var chipBounds = Bounds(chip, window);
                var refreshBounds = Bounds(refresh, window);
                Assert.InRange(refreshBounds.Left - chipBounds.Right, -0.5, refreshBounds.Height);
                Assert.True(chipBounds.Top < refreshBounds.Bottom && refreshBounds.Top < chipBounds.Bottom,
                    "Refresh shares the chip's row.");
                // The top bar is one row: the chip and Refresh share it with the menus.
                var topBar = window.FindControl<Border>("TopBar")!;
                Assert.True(Bounds(topBar, window).Height <= (double)window.FindResource("Component.TopBar.Height")! *
                    (zoom / 100d) + 1, "The chip pushed the top bar past its one row.");
                var card = ProjectCardAccess.Open(window);
                var choose = card.FindControl<Button>("CardChooseButton")!;
                AssertInside(window, choose);
                var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(choose.Command);
                HeadlessClick.Click(window, choose, "Choose");
                Assert.NotNull(command.ExecutionTask);
                await command.ExecutionTask;
                PageScreenshots.Settle(window);
                Assert.True(workspace.Context.Setup!.IsOpen);
            }
            finally
            {
                settings.ZoomPercent = ZoomPolicy.DefaultPercent;
                Application.Current.RequestedThemeVariant = priorTheme;
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    private static Rect Bounds(Control control, Window window) => new(
        control.TranslatePoint(default, window)!.Value,
        control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window)!.Value);

    private static void AssertInside(Window window, Control control)
    {
        Assert.True(control.IsEffectivelyVisible && control.Bounds.Width > 0 && control.Bounds.Height > 0);
        Assert.True(new Rect(window.ClientSize).Inflate(1).Contains(Bounds(control, window)),
            $"{control.Name} at {Bounds(control, window)} exceeds actual client {window.ClientSize}.");
    }

    private static void Capture(Window window, int width, int height, int zoom, bool dark, bool windowsShaped)
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable);
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No header frame rendered.");
        frame.Save(Path.Combine(folder,
            $"long-project-{(windowsShaped ? "windows-fixture" : "native")}-{width}-{height}-{zoom}-{(dark ? "dark" : "light")}.png"),
            PngBitmapEncoderOptions.Default);
    }
}
