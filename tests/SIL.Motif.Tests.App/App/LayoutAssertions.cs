using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

internal static class LayoutAssertions
{
    private const double Tolerance = 2;
    private static readonly int[] Widths = [1040, 1240];
    private static readonly int[] Zooms = [100, 125, 150];
    private static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];
    internal const string WalkthroughWordRowExclusionReason =
        "ADR 0053 assigns word-presentation line metrics to the word-row module; " +
        "remove this exclusion when that module lands.";
    private static readonly LayoutSubtreeExclusion WalkthroughWordRowExclusion = new(
        "WordCard, MorphemeRow, ProgressiveItemsControl#ResultsInTextPanelFieldWorksAnalysesItems, " +
        "WordRow, WordRowHeader, and WordStrip subtrees",
        WalkthroughWordRowExclusionReason,
        control => control.GetSelfAndVisualAncestors().OfType<Control>()
            .Any(ancestor => ancestor is WordRow or WordRowHeader or MorphemeRow ||
                ancestor is ProgressiveItemsControl { Name: "ResultsInTextPanelFieldWorksAnalysesItems" } ||
                ancestor is Border { Name: "WordStrip" } ||
                ancestor is Border wordCard && wordCard.Classes.Contains("wordCard")));

    internal static void BeforeCapture(Window window) => BeforeCapture(window, WalkthroughWordRowExclusion);

    internal static void BeforeWalkthroughCapture(Window window) =>
        BeforeCapture(window, WalkthroughWordRowExclusion);

    private static void BeforeCapture(Window window, LayoutSubtreeExclusion? exclusion)
    {
        var originalWidth = window.Width;
        var originalHeight = window.Height;
        var originalTheme = window.RequestedThemeVariant;
        var application = Application.Current ?? throw new InvalidOperationException("The App is not initialized.");
        var originalApplicationTheme = application.RequestedThemeVariant;
        var settingsButton = window.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(button => button.Name == "SettingsButton");
        var settings = settingsButton?.Flyout is Flyout flyout &&
            flyout.Content is SettingsPopupView popup ? popup.DataContext as SettingsViewModel : null;
        var originalZoom = settings?.ZoomPercent ?? ZoomPolicy.DefaultPercent;
        var scrollOffsets = window.GetVisualDescendants().OfType<ScrollViewer>()
            .ToDictionary(scroll => scroll, scroll => scroll.Offset);

        try
        {
            foreach (var theme in Themes)
            foreach (var width in Widths)
            foreach (var zoom in Zooms)
            {
                application.RequestedThemeVariant = theme;
                if (settings is not null) settings.ZoomPercent = zoom;
                else WindowZoomPolicy.Apply(application, zoom);
                window.Width = width;
                window.Height = 780;
                try
                {
                    PageScreenshots.Settle(window);
                }
                catch (Exception failure)
                {
                    throw new InvalidOperationException(
                        $"Layout did not settle at {width}×780, {zoom}% zoom, {theme} theme.", failure);
                }
                try
                {
                    AssertCurrent(window, exclusion);
                }
                catch (Exception failure)
                {
                    SaveFailureFrame(window, width, theme, zoom, failure.Message);
                    var exclusionDetails = exclusion is null
                        ? string.Empty
                        : $" Excluded {exclusion.Name}: {exclusion.Reason}";
                    throw new Xunit.Sdk.XunitException(
                        $"Layout does not fit at {width}×780, {zoom}% zoom, {theme} theme.{exclusionDetails} " +
                        failure.Message);
                }
            }
        }
        finally
        {
            window.Width = double.IsNaN(originalWidth) ? window.Bounds.Width : originalWidth;
            window.Height = double.IsNaN(originalHeight) ? window.Bounds.Height : originalHeight;
            window.RequestedThemeVariant = originalTheme;
            application.RequestedThemeVariant = originalApplicationTheme;
            if (settings is not null) settings.ZoomPercent = originalZoom;
            else WindowZoomPolicy.Apply(application, originalZoom);
            PageScreenshots.Settle(window);
            foreach (var (scroll, offset) in scrollOffsets)
                if (scroll.IsAttachedToVisualTree()) scroll.Offset = offset;
            PageScreenshots.Settle(window);
        }
    }

    internal static void AssertCurrent(Visual root) => AssertCurrent(root, null);

    internal static void AssertWalkthroughCurrent(Visual root) =>
        AssertCurrent(root, WalkthroughWordRowExclusion);

    private static void AssertCurrent(Visual root, LayoutSubtreeExclusion? exclusion)
    {
        var roots = LayoutRoots(root, exclusion).Distinct().ToArray();
        foreach (var layoutRoot in roots)
        {
            AssertScrolledMorphemesAreReachable(layoutRoot, exclusion);
            AssertGridChildrenStayInTheirColumns(layoutRoot, exclusion);
            AssertStagedPanelsDoNotCoverText(layoutRoot, exclusion);
            foreach (var text in Controls(layoutRoot, exclusion).OfType<TextBlock>().Where(IsRenderedText))
                AssertTextFits(text, exclusion);
        }
    }

    private static void AssertStagedPanelsDoNotCoverText(Visual root, LayoutSubtreeExclusion? exclusion)
    {
        Visual layoutRoot = TopLevel.GetTopLevel(root) is { } topLevel ? topLevel : root;
        foreach (var runningText in Controls(root, exclusion).OfType<RunningTextPanel>())
        {
            var texts = Controls(runningText, exclusion).OfType<TextBlock>().Where(IsRenderedText).ToArray();
            foreach (var panel in Controls(runningText, exclusion).OfType<Border>().Where(border =>
                         border.Classes.Contains("stagedStrip") && border.IsEffectivelyVisible))
            {
                var panelBounds = BoundsIn(panel, layoutRoot);
                foreach (var text in texts.Where(text => !text.GetVisualAncestors().Contains(panel)))
                {
                    var textBounds = BoundsIn(text, layoutRoot);
                    var inkBounds = new Rect(textBounds.X - Tolerance, textBounds.Y,
                        textBounds.Width + Tolerance * 2, textBounds.Height);
                    Assert.False(panelBounds.Intersects(inkBounds),
                        $"Staged panel {Describe(panel)} at {panelBounds} intersects the {Tolerance:0.#} px " +
                        $"safety bounds around text '{text.Text}' at {textBounds} in {Describe(runningText)}.");
                }
            }
        }
    }

    internal static void AssertMorphemeGlyphsFitAnalysisRows(Visual root)
    {
        foreach (var row in Controls(root).OfType<Border>().Where(border =>
                     border.Classes.Contains("stripRow") && border.Classes.Contains("analysisRow") &&
                     border.IsEffectivelyVisible))
        {
            var thickness = row.BorderThickness;
            var padding = row.Padding;
            var content = new Rect(
                thickness.Left + padding.Left,
                thickness.Top + padding.Top,
                row.Bounds.Width - thickness.Left - thickness.Right - padding.Left - padding.Right,
                row.Bounds.Height - thickness.Top - thickness.Bottom - padding.Top - padding.Bottom);
            Visual layoutRoot = TopLevel.GetTopLevel(row) is { } topLevel ? topLevel : row;
            var rowBounds = BoundsIn(row, layoutRoot);
            var contentClipTop = rowBounds.Top + content.Top;
            var contentClipBottom = rowBounds.Top + content.Bottom;
            foreach (var text in Controls(row).OfType<TextBlock>().Where(text =>
                         (text.Classes.Contains("stripMorphForm") || text.Classes.Contains("stripMorphGloss")) &&
                         IsRenderedText(text)))
            {
                var typeface = new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch);
                Assert.True(FontManager.Current.TryGetGlyphTypeface(typeface, out var glyphTypeface),
                    $"Could not resolve the rendered font for morpheme text '{text.Text}'.");
                var metrics = glyphTypeface.Metrics;
                var scale = text.FontSize / metrics.DesignEmHeight;
                var ascent = Math.Abs(metrics.Ascent) * scale;
                var descent = Math.Abs(metrics.Descent) * scale;
                var lines = text.TextLayout.TextLines;
                var textBounds = BoundsIn(text, layoutRoot);
                var layoutTop = text.Padding.Top;
                foreach (var line in lines)
                {
                    var inkTop = layoutTop + line.Baseline - ascent;
                    var inkBottom = layoutTop + line.Baseline + descent;
                    Assert.True(inkTop >= text.Padding.Top - 0.05 &&
                                inkBottom <= text.Bounds.Height - text.Padding.Bottom + 0.05,
                        $"Morpheme text '{text.Text}' needs {ascent + descent:0.##} px of glyph height " +
                        $"(ascent {ascent:0.##}, descent {descent:0.##}) at line height {line.Height:0.##} px, " +
                        $"but its arranged text bounds are {text.Bounds.Height:0.##} px.");

                    var renderedTop = textBounds.Top + inkTop;
                    var renderedBottom = textBounds.Top + inkBottom;
                    Assert.True(renderedTop >= contentClipTop - 0.05 && renderedBottom <= contentClipBottom + 0.05,
                        $"Morpheme text '{text.Text}' renders at {renderedTop:0.##}–{renderedBottom:0.##} px " +
                        $"outside its analysis row's {contentClipTop:0.##}–{contentClipBottom:0.##} px content clip.");

                    var scrolls = text.GetVisualAncestors().OfType<ScrollViewer>().ToArray();
                    foreach (var scroll in scrolls)
                    {
                        if (scroll.Content is not Visual scrollContent) continue;
                        var textInContent = BoundsIn(text, scrollContent);
                        var glyphTop = textInContent.Top + inkTop;
                        var glyphBottom = textInContent.Top + inkBottom;
                        Assert.True(glyphTop >= -0.05 && glyphBottom <= scroll.Extent.Height + 0.05,
                            $"Morpheme text '{text.Text}' renders at {glyphTop:0.##}–{glyphBottom:0.##} px " +
                            $"outside its scroll content's {scroll.Extent.Height:0.##} px extent.");
                    }

                    var scrollContainers = scrolls.SelectMany(scroll =>
                            scroll.GetVisualAncestors().OfType<Control>())
                        .ToHashSet();
                    foreach (var ancestor in text.GetVisualAncestors().OfType<Control>()
                                 .Where(item => item.ClipToBounds &&
                                     item is not ScrollViewer and not ScrollContentPresenter &&
                                     !scrollContainers.Contains(item)))
                    {
                        var clip = BoundsIn(ancestor, layoutRoot);
                        Assert.True(renderedTop >= clip.Top - 0.05 && renderedBottom <= clip.Bottom + 0.05,
                            $"Morpheme text '{text.Text}' renders at {renderedTop:0.##}–{renderedBottom:0.##} px " +
                            $"outside the {Describe((Control)ancestor)} clip at {clip.Top:0.##}–{clip.Bottom:0.##} px.");
                    }

                    layoutTop += line.Height;
                }
            }
        }
    }

    private static IEnumerable<Visual> LayoutRoots(Visual root, LayoutSubtreeExclusion? exclusion)
    {
        yield return root;
        foreach (var control in Controls(root, exclusion).OfType<Control>())
        {
            if (ToolTip.GetIsOpen(control) && ToolTip.GetTip(control) is Control tip)
                yield return TopLevel.GetTopLevel(tip) is { } tipRoot ? tipRoot : tip;

            if (control is Button { Flyout: PopupFlyoutBase { IsOpen: true } flyout } &&
                flyout.Popup.Child is { } flyoutContent)
                yield return TopLevel.GetTopLevel(flyoutContent) is { } flyoutRoot ? flyoutRoot : flyoutContent;

            if (control.ContextFlyout is PopupFlyoutBase { IsOpen: true } contextFlyout &&
                contextFlyout.Popup.Child is { } contextContent)
                yield return TopLevel.GetTopLevel(contextContent) is { } contextRoot ? contextRoot : contextContent;

            foreach (var popup in control.GetVisualDescendants().OfType<Popup>().Where(popup => popup.IsOpen))
                if (popup.Child is { } popupContent)
                    yield return TopLevel.GetTopLevel(popupContent) is { } popupRoot ? popupRoot : popupContent;
        }

        if (root is Window mainWindow && Application.Current?.ApplicationLifetime is
            IClassicDesktopStyleApplicationLifetime desktop)
            foreach (var otherWindow in desktop.Windows.Where(window => window != mainWindow && window.IsVisible))
                yield return otherWindow;
    }

    private static IEnumerable<Visual> Controls(Visual root, LayoutSubtreeExclusion? exclusion = null) =>
        root.GetSelfAndVisualDescendants().Where(item =>
            item is not Control control || exclusion?.Excludes(control) != true);

    private static bool IsRenderedText(TextBlock text) =>
        text.IsEffectivelyVisible && !string.IsNullOrEmpty(text.Text) &&
        text.Bounds.Width > 0 && text.Bounds.Height > 0;

    internal static void AssertTextFits(TextBlock text) => AssertTextFits(text, null);

    private static void AssertTextFits(TextBlock text, LayoutSubtreeExclusion? exclusion)
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
                $"Text '{text.Text}' ({name}) needs {wrapped.Height:0.#} px vertically at its width but has {text.Bounds.Height:0.#} px. " +
                $"{LayoutSizeDiagnostics(text, "vertical")} Path: {path}");
        }
        else
        {
            Assert.True(width <= text.Bounds.Width + Tolerance,
                $"Text '{text.Text}' ({name}) needs {width:0.#} px but has {text.Bounds.Width:0.#} px. " +
                $"{LayoutSizeDiagnostics(text, "horizontal")} Path: {path}");
            Assert.True(layout.Height <= text.Bounds.Height + Tolerance || IsSeverityGlyph(text),
                $"Text '{text.Text}' ({name}) needs {layout.Height:0.#} px vertically but has {text.Bounds.Height:0.#} px. " +
                $"{LayoutSizeDiagnostics(text, "vertical")} Path: {path}");
        }

        Visual layoutRoot = TopLevel.GetTopLevel(text) is { } topLevel ? topLevel : text;
        var textBounds = BoundsIn(text, layoutRoot);
        foreach (var ancestor in text.GetVisualAncestors().OfType<Visual>().Where(item => item.ClipToBounds))
        {
            var clipBounds = BoundsIn(ancestor, layoutRoot);
            if (Contains(clipBounds, textBounds)) continue;
            var scrollCanRevealText = text.GetSelfAndVisualAncestors().OfType<ScrollViewer>()
                .Any(scroll => IsWithinScrollExtent(text, scroll)) || DataGridCanRevealText(text, ancestor);
            Assert.True(scrollCanRevealText,
                $"Text '{text.Text}' ({name}) is clipped outside a scroll viewport that can reveal it. " +
                $"{ClipDiagnostics(text, textBounds, ancestor, BoundsIn(ancestor, layoutRoot))} " +
                $"Path: {path}.{TooltipDetails(text, layoutRoot, exclusion)}");
        }
    }

    private static string ClipDiagnostics(TextBlock text, Rect textBounds, Visual clipper, Rect clipBounds)
    {
        var horizontal = textBounds.Left < clipBounds.Left - Tolerance ||
            textBounds.Right > clipBounds.Right + Tolerance;
        var vertical = textBounds.Top < clipBounds.Top - Tolerance ||
            textBounds.Bottom > clipBounds.Bottom + Tolerance;
        var axis = (horizontal, vertical) switch
        {
            (true, true) => "horizontal and vertical",
            (true, false) => "horizontal",
            (false, true) => "vertical",
            _ => "unknown",
        };
        var clipperName = clipper is Control control ? Describe(control) : clipper.GetType().Name;
        return $"{LayoutSizeDiagnostics(text, axis)} Clipper {clipperName} bounds: " +
            $"{clipBounds.Width:0.#} × {clipBounds.Height:0.#}.";
    }

    private static string LayoutSizeDiagnostics(TextBlock text, string axis)
    {
        var chip = text.GetVisualAncestors().OfType<MarkChip>().FirstOrDefault();
        var cell = text.GetVisualAncestors().OfType<DataGridCell>().FirstOrDefault();
        var column = cell is null ? null : DataGridColumn.GetColumnContainingElement(cell);
        var chipSize = chip is null ? "n/a" : $"{chip.DesiredSize.Width:0.#} × {chip.DesiredSize.Height:0.#}";
        var cellSize = cell is null ? "n/a" : $"{cell.Bounds.Width:0.#} × {cell.Bounds.Height:0.#}";
        var columnWidth = column is null ? "n/a" : $"{column.ActualWidth:0.#}";
        return $"Axis: {axis}; MarkChip DesiredSize: {chipSize}; " +
            $"DataGridCell arranged size: {cellSize}; column ActualWidth: {columnWidth}.";
    }

    private static string TooltipDetails(TextBlock text, Visual layoutRoot, LayoutSubtreeExclusion? exclusion)
    {
        var tooltip = text.GetVisualAncestors().OfType<ToolTip>().FirstOrDefault();
        var owner = Controls(layoutRoot, exclusion).OfType<Control>().FirstOrDefault(control =>
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

    private static void AssertGridChildrenStayInTheirColumns(Visual root, LayoutSubtreeExclusion? exclusion)
    {
        foreach (var grid in Controls(root, exclusion).OfType<Grid>().Where(grid => grid.ColumnDefinitions.Count > 1))
        {
            var origin = grid.TranslatePoint(default, grid);
            if (origin is null) continue;
            var widths = grid.ColumnDefinitions.Select(column => column.ActualWidth).ToArray();
            var spacing = grid.ColumnSpacing;
            foreach (var child in grid.Children.OfType<Control>().Where(child =>
                         exclusion?.Excludes(child) != true && child.IsEffectivelyVisible &&
                         child.Bounds.Width > 0 && child.Bounds.Height > 0))
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

    private static void AssertScrolledMorphemesAreReachable(Visual root, LayoutSubtreeExclusion? exclusion)
    {
        foreach (var scroll in Controls(root, exclusion).OfType<ScrollViewer>().Where(viewer =>
                     viewer.Name == "FieldWorksMorphemeScroll" ||
                     AutomationProperties.GetAutomationId(viewer) == AutomationIds.FieldWorksMorphemeScroll))
        {
            Assert.True(scroll.ClipToBounds, "The FieldWorks morpheme strip must clip at its viewport.");
            Assert.True(scroll.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled,
                "The FieldWorks morpheme strip must allow horizontal scrolling.");
            var content = scroll.Content as Visual;
            if (content is null) continue;
            foreach (var morpheme in Controls(scroll, exclusion).OfType<Border>().Where(border =>
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
        var bounds = visual.Bounds;
        var corners = new[]
        {
            new Point(0, 0),
            new Point(bounds.Width, 0),
            new Point(0, bounds.Height),
            new Point(bounds.Width, bounds.Height),
        }.Select(point => visual.TranslatePoint(point, root)).Where(point => point is not null)
            .Select(point => point!.Value).ToArray();
        if (corners.Length == 0) return default;
        var left = corners.Min(point => point.X);
        var top = corners.Min(point => point.Y);
        var right = corners.Max(point => point.X);
        var bottom = corners.Max(point => point.Y);
        return new Rect(left, top, right - left, bottom - top);
    }

    private static bool Contains(Rect container, Rect content) =>
        container.Left <= content.Left + Tolerance && container.Top <= content.Top + Tolerance &&
        container.Right >= content.Right - Tolerance && container.Bottom >= content.Bottom - Tolerance;

    private static string Describe(Control control)
    {
        var classes = string.Join(".", control.Classes);
        return $"{control.GetType().Name}#{control.Name}" + (classes.Length == 0 ? "" : $".{classes}");
    }

    private static void SaveFailureFrame(Window window, int width, ThemeVariant theme, int zoom, string failure)
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable);
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(failure)))[..8].ToLowerInvariant();
        var path = Path.Combine(folder, $"layout-failure-{width}-{zoom}-{theme}-{hash}.png");
        using var frame = window.CaptureRenderedFrame();
        frame?.Save(path, PngBitmapEncoderOptions.Default);
    }

    private sealed record LayoutSubtreeExclusion(string Name, string Reason, Func<Control, bool> Excludes);
}
