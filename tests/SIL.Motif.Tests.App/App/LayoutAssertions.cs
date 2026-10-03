using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

internal static class LayoutAssertions
{
    private const double Tolerance = 2;
    private static readonly int[] Widths = [1040, 1240];
    private static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];

    internal static void BeforeCapture(Window window)
    {
        var originalWidth = window.Width;
        var originalHeight = window.Height;
        var originalTheme = window.RequestedThemeVariant;
        var scrollOffsets = window.GetVisualDescendants().OfType<ScrollViewer>()
            .ToDictionary(scroll => scroll, scroll => scroll.Offset);

        try
        {
            foreach (var theme in Themes)
            foreach (var width in Widths)
            {
                window.RequestedThemeVariant = theme;
                window.Width = width;
                PageScreenshots.Settle(window);
                try
                {
                    AssertCurrent(window);
                }
                catch (Exception failure)
                {
                    SaveFailureFrame(window, width, theme, failure.Message);
                    throw;
                }
            }
        }
        finally
        {
            window.Width = double.IsNaN(originalWidth) ? window.Bounds.Width : originalWidth;
            window.Height = double.IsNaN(originalHeight) ? window.Bounds.Height : originalHeight;
            window.RequestedThemeVariant = originalTheme;
            PageScreenshots.Settle(window);
            foreach (var (scroll, offset) in scrollOffsets)
                if (scroll.IsAttachedToVisualTree()) scroll.Offset = offset;
            PageScreenshots.Settle(window);
        }
    }

    internal static void AssertCurrent(Visual root)
    {
        var roots = LayoutRoots(root).Distinct().ToArray();
        foreach (var layoutRoot in roots)
        {
            AssertScrolledMorphemesAreReachable(layoutRoot);
            AssertGridChildrenStayInTheirColumns(layoutRoot);
            foreach (var text in Controls(layoutRoot).OfType<TextBlock>().Where(IsRenderedText))
                AssertTextFits(text);
        }
    }

    private static IEnumerable<Visual> LayoutRoots(Visual root)
    {
        yield return root;
        foreach (var control in Controls(root).OfType<Control>())
        {
            if (ToolTip.GetIsOpen(control) && ToolTip.GetTip(control) is Control tip)
                yield return TopLevel.GetTopLevel(tip) is { } tipRoot ? tipRoot : tip;

            if (control is Button { Flyout: { IsOpen: true } flyout } &&
                flyout is Flyout { Content: Control content })
                yield return TopLevel.GetTopLevel(content) is { } menuRoot ? menuRoot : content;
        }
    }

    private static IEnumerable<Visual> Controls(Visual root) =>
        root.GetSelfAndVisualDescendants();

    private static bool IsRenderedText(TextBlock text) =>
        text.IsEffectivelyVisible && !string.IsNullOrEmpty(text.Text) &&
        text.Bounds.Width > 0 && text.Bounds.Height > 0;

    private static void AssertTextFits(TextBlock text)
    {
        var layout = text.TextLayout;
        var name = AutomationProperties.GetAutomationId(text) ??
            AutomationProperties.GetName(text) ?? text.Classes.FirstOrDefault() ?? text.GetType().Name;
        var path = string.Join(" ← ", text.GetSelfAndVisualAncestors().OfType<Control>().Take(12).Select(Describe));
        var width = layout.WidthIncludingTrailingWhitespace;
        if (text.TextTrimming != TextTrimming.None)
        {
            var fullSize = MeasureUntrimmed(text);
            if ((fullSize.Width > text.Bounds.Width + Tolerance || fullSize.Height > text.Bounds.Height + Tolerance) &&
                !HasFullTextDisclosure(text))
                Assert.Fail($"Ellipsis on '{name}' hides text with no tooltip, accessible name or copy action.");
            return;
        }

        if (text.TextWrapping != TextWrapping.NoWrap)
        {
            // Wrapped text reflows to its arranged width, so it clips only when it can't fit vertically there.
            var wrapped = MeasureUntrimmed(text);
            Assert.True(wrapped.Height <= text.Bounds.Height + Tolerance || IsSeverityGlyph(text),
                $"Text '{text.Text}' ({name}) needs {wrapped.Height:0.#} px vertically at its width but has {text.Bounds.Height:0.#} px. Path: {path}");
        }
        else
        {
            Assert.True(width <= text.Bounds.Width + Tolerance,
                $"Text '{text.Text}' ({name}) needs {width:0.#} px but has {text.Bounds.Width:0.#} px. Path: {path}");
            Assert.True(layout.Height <= text.Bounds.Height + Tolerance || IsSeverityGlyph(text),
                $"Text '{text.Text}' ({name}) needs {layout.Height:0.#} px vertically but has {text.Bounds.Height:0.#} px. Path: {path}");
        }

        Visual layoutRoot = TopLevel.GetTopLevel(text) is { } topLevel ? topLevel : text;
        var textBounds = BoundsIn(text, layoutRoot);
        foreach (var ancestor in text.GetVisualAncestors().OfType<Visual>().Where(item => item.ClipToBounds))
        {
            if (Contains(BoundsIn(ancestor, layoutRoot), textBounds)) continue;
            var scrollCanRevealText = text.GetSelfAndVisualAncestors().OfType<ScrollViewer>()
                .Any(scroll => IsWithinScrollExtent(text, scroll)) || DataGridCanRevealText(text, ancestor);
            Assert.True(scrollCanRevealText,
                $"Text '{text.Text}' ({name}) is clipped outside a scroll viewport that can reveal it. " +
                $"Path: {path}.{TooltipDetails(text, layoutRoot)}");
        }
    }

    private static string TooltipDetails(TextBlock text, Visual layoutRoot)
    {
        var tooltip = text.GetVisualAncestors().OfType<ToolTip>().FirstOrDefault();
        var owner = Controls(layoutRoot).OfType<Control>().FirstOrDefault(control =>
            ToolTip.GetIsOpen(control) && ToolTip.GetTip(control) is string tip &&
            string.Equals(tip, text.Text, StringComparison.Ordinal));
        if (tooltip is null || owner is null) return string.Empty;
        return $" Tooltip owner {Describe(owner)} at {BoundsIn(owner, layoutRoot)}, " +
            $"placement {ToolTip.GetPlacement(owner)}, " +
            $"custom callback {ToolTip.GetCustomPopupPlacementCallback(owner) is not null}, " +
            $"popup at {BoundsIn(tooltip, layoutRoot)}.";
    }

    private static bool HasFullTextDisclosure(TextBlock text)
    {
        if (text is CopyableTextBlock) return true;
        if (ToolTip.GetTip(text) is string tip && tip.Contains(text.Text!, StringComparison.Ordinal)) return true;
        if (AutomationProperties.GetName(text) is { } name && name.Contains(text.Text!, StringComparison.Ordinal)) return true;
        return text.GetVisualAncestors().OfType<Control>().Any(parent =>
            AutomationProperties.GetName(parent) is { } parentName &&
            parentName.Contains(text.Text!, StringComparison.Ordinal));
    }

    private static bool IsSeverityGlyph(TextBlock text) =>
        text.GetVisualAncestors().OfType<Border>().Any(border => border.Classes.Contains("severityGlyph"));

    private static Size MeasureUntrimmed(TextBlock text)
    {
        var measure = new TextBlock
        {
            Text = text.Text,
            FontFamily = text.FontFamily,
            FontSize = text.FontSize,
            FontStyle = text.FontStyle,
            FontWeight = text.FontWeight,
            FontStretch = text.FontStretch,
            FlowDirection = text.FlowDirection,
            TextAlignment = text.TextAlignment,
            TextWrapping = text.TextWrapping,
            LineHeight = text.LineHeight,
            TextTrimming = TextTrimming.None,
        };
        measure.Measure(new Size(text.Bounds.Width, double.PositiveInfinity));
        return measure.DesiredSize;
    }

    private static void AssertGridChildrenStayInTheirColumns(Visual root)
    {
        foreach (var grid in Controls(root).OfType<Grid>().Where(grid => grid.ColumnDefinitions.Count > 1))
        {
            var origin = grid.TranslatePoint(default, grid);
            if (origin is null) continue;
            var widths = grid.ColumnDefinitions.Select(column => column.ActualWidth).ToArray();
            var spacing = grid.ColumnSpacing;
            foreach (var child in grid.Children.OfType<Control>().Where(child =>
                         child.IsEffectivelyVisible && child.Bounds.Width > 0 && child.Bounds.Height > 0))
            {
                var firstColumn = Math.Clamp(Grid.GetColumn(child), 0, widths.Length - 1);
                var span = Math.Clamp(Grid.GetColumnSpan(child), 1, widths.Length - firstColumn);
                var left = widths.Take(firstColumn).Sum() - Math.Max(0, -child.Margin.Left);
                var right = widths.Take(firstColumn + span).Sum() + spacing * span +
                    Math.Max(0, -child.Margin.Right);
                var bounds = BoundsIn(child, grid);
                Assert.True(bounds.Left >= left - Tolerance && bounds.Right <= right + Tolerance,
                    $"Grid '{Describe(grid)}' child " +
                    $"'{AutomationProperties.GetAutomationId(child) ?? Describe(child)}' " +
                    $"crosses column {firstColumn}: {bounds.Left:0.#}–{bounds.Right:0.#} px, " +
                    $"allocated {left:0.#}–{right:0.#} px. Path: " +
                    string.Join(" ← ", child.GetSelfAndVisualAncestors().OfType<Control>()
                        .Take(7).Select(Describe)));
            }
        }
    }

    private static void AssertScrolledMorphemesAreReachable(Visual root)
    {
        foreach (var scroll in Controls(root).OfType<ScrollViewer>().Where(viewer =>
                     viewer.Name == "FieldWorksMorphemeScroll" ||
                     AutomationProperties.GetAutomationId(viewer) == AutomationIds.FieldWorksMorphemeScroll))
        {
            Assert.True(scroll.ClipToBounds, "The FieldWorks morpheme strip must clip at its viewport.");
            Assert.True(scroll.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled,
                "The FieldWorks morpheme strip must allow horizontal scrolling.");
            var content = scroll.Content as Visual;
            if (content is null) continue;
            foreach (var morpheme in Controls(scroll).OfType<Border>().Where(border =>
                         border.Classes.Contains("wordRowMorph") && border.IsEffectivelyVisible))
            {
                var bounds = BoundsIn(morpheme, content);
                Assert.True(bounds.Left >= -Tolerance && bounds.Right <= scroll.Extent.Width + Tolerance,
                    $"FieldWorks morpheme '{morpheme.Tag}' cannot be reached within the strip's " +
                    $"{scroll.Extent.Width:0.#} px scroll extent.");
            }
        }
    }

    private static bool IsWithinScrollExtent(TextBlock text, ScrollViewer scroll)
    {
        if (scroll.Content is not Visual content) return false;
        var bounds = BoundsIn(text, content);
        var insideExtent = bounds.Left >= -Tolerance && bounds.Top >= -Tolerance &&
            bounds.Right <= scroll.Extent.Width + Tolerance && bounds.Bottom <= scroll.Extent.Height + Tolerance;
        if (!insideExtent) return false;
        var visibleX = bounds.Left >= scroll.Offset.X - Tolerance &&
            bounds.Right <= scroll.Offset.X + scroll.Viewport.Width + Tolerance;
        var visibleY = bounds.Top >= scroll.Offset.Y - Tolerance &&
            bounds.Bottom <= scroll.Offset.Y + scroll.Viewport.Height + Tolerance;
        var canScrollX = visibleX || scroll.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled;
        var canScrollY = visibleY || scroll.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled;
        return canScrollX && canScrollY;
    }

    private static bool DataGridCanRevealText(TextBlock text, Visual clippedBy)
    {
        var row = text.GetVisualAncestors().OfType<DataGridRow>().FirstOrDefault();
        var grid = row?.GetVisualAncestors().OfType<DataGrid>().FirstOrDefault();
        if (row is null || grid is null || !row.GetVisualAncestors().Contains(clippedBy) ||
            (!ReferenceEquals(clippedBy, grid) && !clippedBy.GetVisualAncestors().Contains(grid))) return false;
        if (grid.ItemsSource is not { } source || !source.Cast<object>().Any(item => Equals(item, row.DataContext)))
            return false;
        var presenter = grid.GetVisualDescendants().OfType<DataGridRowsPresenter>().Single();
        if (text.Bounds.Height > presenter.Bounds.Height + Tolerance) return false;
        var bounds = BoundsIn(text, presenter);
        var bars = grid.GetVisualDescendants().OfType<ScrollBar>()
            .Where(bar => bar.IsEffectivelyVisible && bar.Maximum > bar.Minimum).ToArray();
        var visibleX = bounds.Left >= -Tolerance && bounds.Right <= presenter.Bounds.Width + Tolerance;
        var visibleY = bounds.Top >= -Tolerance && bounds.Bottom <= presenter.Bounds.Height + Tolerance;
        return visibleX && (visibleY || bars.Any(bar => bar.Orientation == Avalonia.Layout.Orientation.Vertical));
    }

    private static Rect BoundsIn(Visual visual, Visual root)
    {
        var origin = visual.TranslatePoint(default, root);
        return origin is null ? default : new Rect(origin.Value, visual.Bounds.Size);
    }

    private static bool Contains(Rect container, Rect content) =>
        container.Left <= content.Left + Tolerance && container.Top <= content.Top + Tolerance &&
        container.Right >= content.Right - Tolerance && container.Bottom >= content.Bottom - Tolerance;

    private static string Describe(Control control)
    {
        var classes = string.Join(".", control.Classes);
        return $"{control.GetType().Name}#{control.Name}" + (classes.Length == 0 ? "" : $".{classes}");
    }

    private static void SaveFailureFrame(Window window, int width, ThemeVariant theme, string failure)
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable);
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(failure)))[..8].ToLowerInvariant();
        var path = Path.Combine(folder, $"layout-failure-{width}-{theme}-{hash}.png");
        using var frame = window.CaptureRenderedFrame();
        frame?.Save(path, PngBitmapEncoderOptions.Default);
    }
}
