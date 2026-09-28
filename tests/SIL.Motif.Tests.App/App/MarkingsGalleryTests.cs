using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class MarkingsGalleryTests
{
    private readonly AvaloniaHeadlessFixture _avalonia;

    public MarkingsGalleryTests(AvaloniaHeadlessFixture avalonia) => _avalonia = avalonia;

    [Fact]
    public void GalleryRendersEveryOpinionAndParserState()
    {
        _avalonia.Invoke(() =>
        {
            var gallery = new MarkingsGallery();

            Assert.Contains("approved", gallery.ApprovedMark.Classes);
            Assert.Contains("disapproved", gallery.DisapprovedMark.Classes);
            Assert.Contains("unknown", gallery.UnknownMark.Classes);
            Assert.Contains("none", gallery.EmptyMark.Classes);
            Assert.Contains("same", gallery.SameLine.Classes);
            Assert.Contains("different", gallery.DifferentLine.Classes);
            Assert.Contains("none", gallery.NoneLine.Classes);
            Assert.Contains("capped", gallery.CappedLine.Classes);
            Assert.Equal("+2", ((TextBlock)gallery.ExtraCount.Child!).Text);
            Assert.Equal("✓", gallery.PrimaryAction.Content);
            Assert.Equal("Fix ▾", gallery.FixAction.Content);
            Assert.Equal("Staged", ((StackPanel)gallery.StagedStrip.Child!).Children[0] is TextBlock label ? label.Text : null);
            Assert.Equal("Compact default", gallery.CompactText.Text);
            Assert.Equal("Normal", gallery.NormalText.Text);
        });
    }

    [Fact]
    public void OpinionMarksHaveAccessibleNamesAndReadableResolvedColours()
    {
        _avalonia.Invoke(() =>
        {
            var gallery = new MarkingsGallery();
            var window = Show(gallery, ThemeVariant.Light);
            try
            {
                Assert.Equal("Approved", AutomationProperties.GetName(gallery.ApprovedMark));
                Assert.Equal("Disapproved", AutomationProperties.GetName(gallery.DisapprovedMark));
                Assert.Equal("Unknown", AutomationProperties.GetName(gallery.UnknownMark));
                Assert.Equal("Not in FieldWorks", AutomationProperties.GetName(gallery.EmptyMark));

                foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                foreach (var opinion in new[] { "Approved", "Disapproved", "Unknown" })
                {
                    var text = ColorResource($"Intent.Opinion.{opinion}.Text", variant);
                    var fill = ColorResource($"Intent.Opinion.{opinion}.Fill", variant);
                    Assert.True(Contrast(text, fill) >= 4.5,
                        $"{variant} {opinion} contrast was {Contrast(text, fill):F2}:1.");
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void SecondaryActionsRevealOnHoverAndFocusAndRemainKeyboardReachable()
    {
        _avalonia.Invoke(() =>
        {
            var gallery = new MarkingsGallery();
            var window = Show(gallery, ThemeVariant.Light);
            try
            {
                Assert.Equal(0d, gallery.StagedUndo.Opacity);
                Assert.Equal(0d, gallery.FieldWorksLink.Opacity);
                Assert.True(gallery.StagedUndo.IsTabStop);
                Assert.True(gallery.FieldWorksLink.IsTabStop);

                var stagedCenter = CentreOf(gallery.StagedStrip, window);
                window.MouseMove(stagedCenter);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1d, gallery.StagedUndo.Opacity);

                window.MouseMove(new Point(1, 1));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(0d, gallery.StagedUndo.Opacity);

                Assert.True(gallery.FieldWorksLink.Focus());
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1d, gallery.FieldWorksLink.Opacity);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void NormalDensityUsesLargerTextThanTheDefaultCompactDensity()
    {
        _avalonia.Invoke(() =>
        {
            var gallery = new MarkingsGallery();
            var window = Show(gallery, ThemeVariant.Light);
            try
            {
                Assert.Equal(12d, gallery.CompactText.FontSize);
                Assert.Equal(13d, gallery.NormalText.FontSize);
                Assert.True(gallery.NormalText.FontSize > gallery.CompactText.FontSize);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [ScreenshotFact]
    public void CaptureMarkingsGalleryInLightAndDarkThemes()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        _avalonia.Invoke(() =>
        {
            var gallery = new MarkingsGallery();
            var window = Show(gallery, ThemeVariant.Light);
            try
            {
                window.MouseMove(CentreOf(gallery.StagedStrip, window));
                Assert.True(gallery.FieldWorksLink.Focus());
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1d, gallery.StagedUndo.Opacity);
                Assert.Equal(1d, gallery.FieldWorksLink.Opacity);
                foreach (var (name, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
                {
                    window.RequestedThemeVariant = variant;
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var frame = window.CaptureRenderedFrame()
                        ?? throw new InvalidOperationException($"No frame rendered for the {name} gallery.");
                    frame.Save(Path.Combine(folder, $"markings-gallery-{name}.png"), PngBitmapEncoderOptions.Default);
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static Window Show(Control content, ThemeVariant variant)
    {
        var window = new Window { Content = content, RequestedThemeVariant = variant, Width = 760, Height = 460 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return window;
    }

    private static Point CentreOf(Control control, Window window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
        ?? throw new InvalidOperationException("The control is not positioned in the window.");

    private static Color ColorResource(string key, ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryGetResource(key, variant, out var value), $"Missing resource {key} for {variant}.");
        return Assert.IsType<SolidColorBrush>(value).Color;
    }

    private static double Contrast(Color first, Color second)
    {
        var light = Math.Max(Luminance(first), Luminance(second));
        var dark = Math.Min(Luminance(first), Luminance(second));
        return (light + 0.05) / (dark + 0.05);
    }

    private static double Luminance(Color color) =>
        0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);

    private static double Linear(byte value)
    {
        var channel = value / 255d;
        return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }
}
