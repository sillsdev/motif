using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using SIL.Motif.App.Controls;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class MarkingsGalleryTests
{
    private readonly AvaloniaHeadlessFixture _avalonia;

    public MarkingsGalleryTests(AvaloniaHeadlessFixture avalonia) => _avalonia = avalonia;

    [Fact]
    public void BoundOpinionMarkUpdatesItsLetterShapeAndAccessibleName()
    {
        _avalonia.Invoke(() =>
        {
            var mark = new OpinionMark { Kind = OpinionMarkKind.Unknown };

            Assert.Equal("U", Assert.IsType<TextBlock>(mark.Child).Text);
            Assert.Contains("unknown", mark.Classes);
            Assert.Equal("Unknown", AutomationProperties.GetName(mark));
        });
    }

    [Fact]
    public void GalleryRendersEveryOpinionAndParserState()
    {
        _avalonia.Invoke(() =>
        {
            var gallery = new MarkingsGallery();

            Assert.IsType<OpinionMark>(gallery.ApprovedMark);
            Assert.IsType<OpinionMark>(gallery.DisapprovedMark);
            Assert.IsType<OpinionMark>(gallery.UnknownMark);
            Assert.IsType<OpinionMark>(gallery.EmptyMark);
            Assert.Contains("approved", gallery.ApprovedMark.Classes);
            Assert.Contains("disapproved", gallery.DisapprovedMark.Classes);
            Assert.Contains("unknown", gallery.UnknownMark.Classes);
            Assert.Contains("none", gallery.EmptyMark.Classes);
            Assert.Equal("A", ((TextBlock)gallery.ApprovedMark.Child!).Text);
            Assert.Equal("D", ((TextBlock)gallery.DisapprovedMark.Child!).Text);
            Assert.Equal("U", ((TextBlock)gallery.UnknownMark.Child!).Text);
            Assert.IsType<Avalonia.Controls.Shapes.Rectangle>(
                ((Grid)gallery.EmptyMark.Child!).Children[0]);
            Assert.Contains("same", gallery.SameLine.Classes);
            Assert.Contains("different", gallery.DifferentLine.Classes);
            Assert.Contains("f", ((TextBlock)((Grid)gallery.DifferentLine.Child!).Children[1]!).Classes);
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
                Assert.Equal(0d, gallery.ApprovedMark.CornerRadius.TopLeft);
                Assert.Equal(0d, gallery.DisapprovedMark.CornerRadius.TopLeft);
                Assert.Equal(7d, gallery.UnknownMark.CornerRadius.TopLeft);
                var noneDash = Assert.IsType<Avalonia.Controls.Shapes.Rectangle>(
                    ((Grid)gallery.EmptyMark.Child!).Children[0]);
                Assert.Equal(new[] { 3d, 2d }, noneDash.StrokeDashArray);
                Assert.Equal(Color.Parse("#d5dce4"), Assert.IsType<SolidColorBrush>(noneDash.Stroke).Color);

                foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                foreach (var opinion in new[] { "Approved", "Disapproved", "Unknown" })
                {
                    var text = ColorResource($"Intent.Opinion.{opinion}.Text", variant);
                    var fill = ColorResource($"Intent.Opinion.{opinion}.Fill", variant);
                    Assert.True(Contrast(text, fill) >= 4.5,
                        $"{variant} {opinion} contrast was {Contrast(text, fill):F2}:1.");
                }

                foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    AssertReadable("Intent.Opinion.None.Text", "Intent.Marking.Surface", variant);
                    AssertReadable("Intent.Outcome.Same", "Intent.Marking.Surface", variant);
                    AssertReadable("Intent.Outcome.Different", "Intent.Outcome.Different.Fill", variant);
                    AssertReadable("Intent.Outcome.Different", "Intent.Marking.Surface", variant);
                    AssertReadable("Intent.Outcome.NoParse", "Intent.Marking.Surface", variant);
                    AssertReadable("Intent.Outcome.Stopped", "Intent.Marking.Surface", variant);
                    AssertReadable("Intent.Change.Text", "Intent.Change.Fill", variant);
                    AssertReadable("Intent.Marking.Text", "Intent.Marking.Surface", variant);
                    AssertReadable("Intent.Text", "Intent.Surface", variant);
                    AssertReadable("Intent.Outcome.Different", "Intent.Surface", variant);
                    AssertReadable("Intent.Opinion.Approved.Text", "Intent.Marking.Surface", variant);
                    AssertReadable("Intent.Marking.Link", "Intent.Marking.Surface", variant);
                    AssertReadable("Intent.Unread.Text", "Intent.Unread.Fill", variant);
                    Assert.Equal(Color.Parse(variant == ThemeVariant.Light ? "#54278f" : "#d9c2ff"),
                        ColorResource("Intent.Unread.Text", variant));
                    Assert.Equal(Color.Parse(variant == ThemeVariant.Light ? "#f6f0ff" : "#2b1c3b"),
                        ColorResource("Intent.Unread.Fill", variant));
                    Assert.Equal(Color.Parse(variant == ThemeVariant.Light ? "#d5dce4" : "#4d5865"),
                        ColorResource("Intent.Opinion.None.Outline", variant));
                }
                Assert.Equal(Color.Parse("#69737f"),
                    ColorResource("Intent.Opinion.None.Text", ThemeVariant.Light));
                Assert.Equal(Color.Parse("#164f91"), ColorResource("Intent.Marking.Link", ThemeVariant.Light));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void UnreadMarkHasVisibleTextAndAnAccessibleName()
    {
        _avalonia.Invoke(() =>
        {
            var mark = new UnreadMark();

            var contents = Assert.IsType<StackPanel>(mark.Child);
            Assert.IsType<Ellipse>(contents.Children[0]);
            Assert.Equal("Unread", Assert.IsType<TextBlock>(contents.Children[1]).Text);
            Assert.Equal("Unread", AutomationProperties.GetName(mark));
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
                Assert.True(gallery.StagedUndo.IsVisible);
                Assert.True(gallery.FieldWorksLink.IsVisible);
                Assert.True(gallery.StagedUndo.IsTabStop);
                Assert.True(gallery.FieldWorksLink.IsTabStop);
                Assert.False(gallery.StagedUndo.IsHitTestVisible);
                Assert.False(gallery.FieldWorksLink.IsHitTestVisible);
                Assert.Equal("Undo staged approval", ControlAutomationPeer.CreatePeerForElement(gallery.StagedUndo).GetName());
                Assert.Equal("Open in FieldWorks", ControlAutomationPeer.CreatePeerForElement(gallery.FieldWorksLink).GetName());

                var stagedCenter = CentreOf(gallery.StagedStrip, window);
                window.MouseMove(stagedCenter);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1d, gallery.StagedUndo.Opacity);

                window.MouseMove(new Point(1, 1));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(0d, gallery.StagedUndo.Opacity);

                for (var tab = 0; tab < 8 && !gallery.StagedUndo.IsFocused; tab++)
                    window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.None, null);
                Assert.True(gallery.StagedUndo.IsFocused);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1d, gallery.StagedUndo.Opacity);
                Assert.True(gallery.StagedUndo.IsVisible);
                Assert.True(gallery.FieldWorksLink.IsVisible);
                Assert.True(gallery.StagedUndo.IsHitTestVisible);
                Assert.Equal(0d, gallery.FieldWorksLink.Opacity);

                for (var tab = 0; tab < 8 && !gallery.FieldWorksLink.IsFocused; tab++)
                    window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.None, null);
                Assert.True(gallery.FieldWorksLink.IsFocused);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1d, gallery.FieldWorksLink.Opacity);
                Assert.True(gallery.FieldWorksLink.IsHitTestVisible);
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
                Assert.Equal(13d, gallery.CompactText.FontSize);
                Assert.Equal(15d, gallery.NormalText.FontSize);
                Assert.True(gallery.NormalText.FontSize > gallery.CompactText.FontSize);

                var compactMark = new Border { Classes = { "opinionMark", "approved" } };
                var normalMark = new Border { Classes = { "opinionMark", "approved" } };
                var compactChip = new Button { Content = "Fix", Classes = { "actionChip" } };
                var normalChip = new Button { Content = "Fix", Classes = { "actionChip" } };
                var compactWord = DensityWord(normal: false);
                var normalWord = DensityWord(normal: true);
                gallery.CompactRoot.Children.Add(compactMark);
                gallery.NormalRoot.Children.Add(normalMark);
                gallery.CompactRoot.Children.Add(compactChip);
                gallery.NormalRoot.Children.Add(normalChip);
                gallery.CompactRoot.Children.Add(compactWord);
                gallery.NormalRoot.Children.Add(normalWord);
                window.UpdateLayout();
                Assert.Equal(13d, compactMark.Width);
                Assert.Equal(15d, normalMark.Width);
                Assert.Equal(17d, compactChip.Height);
                Assert.Equal(19d, normalChip.Height);
                Assert.Equal(19d, compactWord.MinHeight);
                Assert.Equal(27d, normalWord.MinHeight);
                Assert.Equal(13d, ((TextBlock)compactWord.Child!).FontSize);
                Assert.Equal(15d, ((TextBlock)normalWord.Child!).FontSize);
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
                    LayoutAssertions.BeforeCapture(window);
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

    private static Border DensityWord(bool normal)
    {
        var word = new Border
        {
            Classes = { "wordVerdict", "analysisDensity" },
            Child = new TextBlock { Text = "word" },
        };
        if (normal) word.Classes.Add("normal");
        return word;
    }

    private static Color ColorResource(string key, ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryGetResource(key, variant, out var value), $"Missing resource {key} for {variant}.");
        return Assert.IsType<SolidColorBrush>(value).Color;
    }

    private static void AssertReadable(string foregroundKey, string backgroundKey, ThemeVariant variant)
    {
        var foreground = ColorResource(foregroundKey, variant);
        var background = ColorResource(backgroundKey, variant);
        Assert.True(Contrast(foreground, background) >= 4.5,
            $"{variant} {foregroundKey} on {backgroundKey} contrast was {Contrast(foreground, background):F2}:1.");
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
