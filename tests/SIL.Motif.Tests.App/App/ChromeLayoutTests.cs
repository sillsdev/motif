using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ChromeLayoutTests
{
    // Part names in Semi.Avalonia's WindowDrawnDecorations template, pinned by the metadata test below.
    private static readonly string[] CaptionPartNames =
        ["PART_FullScreenButton", "PART_MinimizeButton", "PART_MaximizeButton", "PART_CloseButton",
            "PART_PopoverFullScreenButton"];

    [Fact]
    public void TheDrawnCaptionTemplateInUseNamesTheButtonsTheTopBarStyleHides()
    {
        var themeAssembly = typeof(Semi.Avalonia.SemiTheme).Assembly.Location;
        using var stream = File.OpenRead(themeAssembly);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var strings = new HashSet<string>(StringComparer.Ordinal);
        for (var handle = MetadataTokens.UserStringHandle(1); !handle.IsNil;
             handle = metadata.GetNextHandle(handle))
            strings.Add(metadata.GetUserString(handle));

        Assert.Contains("avares://Semi.Avalonia/Controls/WindowDrawnDecorations.axaml", strings);
        Assert.All(CaptionPartNames, name => Assert.Contains(name, strings));
        Assert.Contains("PART_TitleTextPanel", strings);
    }

    [Fact]
    public void DrawnCaptionButtonsKeepMinimizeMaximizeAndCloseWithoutFullscreen()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var buttons = CaptionPartNames.Select(name => new Button { Name = name }).ToArray();
            var window = new Window
            {
                Width = 400,
                Height = 80,
                Content = new StackPanel
                {
                    Children = { buttons[0], buttons[1], buttons[2], buttons[3], buttons[4] },
                },
            };

            try
            {
                window.Show();
                window.UpdateLayout();

                Assert.Equal(
                    ["PART_MinimizeButton", "PART_MaximizeButton", "PART_CloseButton"],
                    buttons.Where(button => button.IsEffectivelyVisible).Select(button => button.Name));
                Assert.False(buttons.Single(button => button.Name == "PART_FullScreenButton").IsVisible);
                Assert.False(buttons.Single(button => button.Name == "PART_PopoverFullScreenButton").IsVisible);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void TheDrawnCaptionTitleIsHiddenSoItCannotOverlapTheTopBarBrand()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var theme = Assert.IsType<ControlTheme>(
                Application.Current!.FindResource(typeof(WindowDrawnDecorations)));
            var template = Assert.IsAssignableFrom<IWindowDrawnDecorationsTemplate>(theme.Setters.OfType<Setter>()
                .Single(setter => setter.Property == WindowDrawnDecorations.TemplateProperty).Value);
            var overlay = Assert.IsAssignableFrom<Control>(template.Build().Result.Overlay);
            var window = new Window { Width = 400, Height = 80, Title = "Motif (tech demo)", Content = overlay };
            try
            {
                window.Show();
                window.UpdateLayout();
                var panel = Assert.Single(overlay.GetSelfAndVisualDescendants().OfType<Panel>(),
                    candidate => candidate.Name == "PART_TitleTextPanel");
                var title = Assert.Single(panel.GetVisualDescendants().OfType<TextBlock>());
                Assert.False(title.IsVisible);
                Assert.False(string.IsNullOrWhiteSpace(window.Title));
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void SettingsControlsCanBeBroughtIntoViewAtMinimumWindowSize()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
            try
            {
                window.Width = 820;
                window.Height = 600;
                PageScreenshots.Settle(window);
                var popup = SettingsViewTests.OpenSettings(window);
                for (var index = 0; index < SettingsViewTests.GroupStates.Length; index++)
                {
                    SettingsTestAccess.Select(popup, SettingsViewTests.GroupStates[index].Group);
                    PageScreenshots.Settle(window);
                    var targets = popup.GetVisualDescendants().OfType<Control>()
                        .Where(IsSettingsControl)
                        .Where(control => control.IsEffectivelyVisible)
                        .ToArray();
                    Assert.NotEmpty(targets);

                    foreach (var target in targets)
                    {
                        target.BringIntoView();
                        PageScreenshots.Settle(window);
                        var scroll = target.GetVisualAncestors().OfType<ScrollViewer>()
                            .FirstOrDefault(viewer => viewer.IsEffectivelyVisible);
                        AssertInside(target, scroll is null ? popup : scroll,
                            scroll is null ? popup.Bounds.Size : scroll.Viewport,
                            $"{SettingsViewTests.GroupStates[index].Slug} / {ControlName(target)}");
                    }
                }
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void AdvancedAiControlsCanBeBroughtIntoViewAtMinimumWindowSize()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(
                parse: false, advancedAiModeEnabled: true);
            try
            {
                window.Width = 820;
                window.Height = 600;
                workspace.CurrentPage = WorkspacePage.AiAssistants;
                PageScreenshots.Settle(window);

                var page = Assert.Single(window.GetVisualDescendants().OfType<AssistantConnectionsPage>());
                var targets = page.GetVisualDescendants().OfType<Control>()
                    .Where(control => control is Button or CheckBox)
                    .Where(control => control.IsEffectivelyVisible)
                    .ToArray();
                Assert.Contains(targets, control => control is CheckBox { Content: "Turn on Advanced AI mode" });

                foreach (var target in targets)
                {
                    target.BringIntoView();
                    PageScreenshots.Settle(window);
                    var scroll = target.GetVisualAncestors().OfType<ScrollViewer>()
                        .FirstOrDefault(viewer => viewer.IsEffectivelyVisible);
                    Assert.NotNull(scroll);
                    AssertInside(target, scroll, scroll.Viewport, ControlName(target));
                }
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void SettingsGearIsACenteredSquareLineIconAtBothZoomLevels()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var (_, window) = FakeComposedWindow.Create();
            try
            {
                window.Show();
                var popup = SettingsViewTests.OpenSettings(window);
                var settings = Assert.IsType<SettingsViewModel>(popup.DataContext);
                var gear = Assert.Single(window.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>(),
                    path => path.Classes.Contains("settingsGearGlyph"));

                foreach (var zoom in new[] { 100, 150 })
                {
                    settings.ZoomPercent = zoom;
                    PageScreenshots.Settle(window);
                    Assert.Equal(Stretch.Uniform, gear.Stretch);
                    Assert.NotNull(gear.Stroke);
                    Assert.True(gear.StrokeThickness > 0, "The gear must be stroked like the sidebar's line icons.");
                    Assert.Equal(18, gear.Bounds.Width);
                    Assert.Equal(18, gear.Bounds.Height);
                    var geometry = Assert.IsAssignableFrom<Geometry>(gear.Data);
                    var bounds = geometry.Bounds;
                    Assert.True(Math.Abs(bounds.Width - bounds.Height) <= 0.1,
                        $"The gear geometry is not square: {bounds}.");
                    Assert.InRange(Math.Abs((bounds.Left + bounds.Right) / 2 - 12), 0, 0.1);
                    Assert.InRange(Math.Abs((bounds.Top + bounds.Bottom) / 2 - 12), 0, 0.1);
                }
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(45));
    }

    private static bool IsSettingsControl(Control control) => control switch
    {
        CheckBox or ComboBox or TextBox or ListBox or NumericUpDown => true,
        Button button => button.Content is not null ||
            !string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)),
        _ => false,
    };

    private static string ControlName(Control control) =>
        AutomationProperties.GetName(control) ?? (control as Button)?.Content?.ToString() ?? control.GetType().Name;

    private static void AssertInside(Control target, Control viewport, Size size, string description)
    {
        var origin = target.TranslatePoint(default, viewport);
        Assert.NotNull(origin);
        var bounds = new Rect(origin.Value, target.Bounds.Size);
        Assert.True(bounds.Left >= -0.5 && bounds.Top >= -0.5 &&
            bounds.Right <= size.Width + 0.5 && bounds.Bottom <= size.Height + 0.5,
            $"{description} is outside its viewport: control={bounds}, viewport={size}.");
    }
}
