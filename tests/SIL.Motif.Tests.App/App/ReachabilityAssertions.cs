using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using SIL.Motif.App.Views;

namespace SIL.Motif.Tests.App;

internal static class ReachabilityAssertions
{
    internal const double Tolerance = LayoutAssertions.Tolerance;

    internal static IReadOnlyList<LayoutFinding> Inspect(Visual root, LayoutFindingContext context) =>
        InspectCore(root, context, includeTextChecks: true);

    internal static IReadOnlyList<LayoutFinding> InspectReachabilityAndTitleBar(
        Visual root, LayoutFindingContext context) => InspectCore(root, context, includeTextChecks: false);

    private static IReadOnlyList<LayoutFinding> InspectCore(
        Visual root, LayoutFindingContext context, bool includeTextChecks)
    {
        var findings = new List<LayoutFinding>();
        var window = TopLevel.GetTopLevel(root) ?? root as TopLevel;
        foreach (var surface in LayoutRoots(root))
        {
            var topLevel = TopLevel.GetTopLevel(surface) ?? surface as TopLevel;
            if (topLevel is null) continue;

            var modalCoversSurface = surface is not SetupDialog &&
                Controls(surface).OfType<SetupDialog>().Any(dialog => dialog.IsEffectivelyVisible);
            var popupControls = surface is TopLevel ? OpenPopupControls(surface) : [];
            var modalControls = surface is TopLevel ? OpenModalControls(surface) : [];
            var popupOverlays = surface is TopLevel ? OpenPopupOverlays(surface, topLevel) : [];
            var controls = Controls(surface)
                .Where(control => ReferenceEquals(TopLevel.GetTopLevel(control), topLevel))
                .Where(control => !popupControls.Contains(control))
                .Where(control => !modalControls.Contains(control))
                .ToArray();
            foreach (var control in controls.Where(IsInteractive))
            {
                if (IsReachable(control, topLevel)) continue;
                var bounds = BoundsIn(control, topLevel);
                findings.Add(new LayoutFinding(context.Scene, context.Size, context.Zoom, context.Theme,
                    LayoutCheckNames.UnreachableControl, Describe(control), LayoutBounds.From(bounds),
                    "outside-client-or-scroll-route",
                    $"The enabled control is outside its top-level client area or an ancestor clip with no scroll route. " +
                    $"Client bounds: {ClientBounds(topLevel)}. {ScrollRouteDiagnostics(control)}"));
            }

            findings.AddRange(NavCompletenessFindings(surface, context, topLevel));
            findings.AddRange(PageHorizontalScrollFindings(surface, context, topLevel));
            if (!includeTextChecks) continue;
            var texts = controls.OfType<TextBlock>().Where(IsRenderedText)
                .Where(_ => !modalCoversSurface)
                .Where(text => popupOverlays.All(overlay =>
                    !Contains(overlay.Bounds, BoundsIn(text, topLevel)))).ToArray();
            var paintedBounds = texts.ToDictionary(text => text, PaintedBounds);
            for (var firstIndex = 0; firstIndex < texts.Length; firstIndex++)
            for (var secondIndex = firstIndex + 1; secondIndex < texts.Length; secondIndex++)
            {
                var first = texts[firstIndex];
                var second = texts[secondIndex];
                if (IsAncestor(first, second) || IsAncestor(second, first) || IntentionallyLayered(first, second))
                    continue;

                var firstBounds = paintedBounds[first];
                var secondBounds = paintedBounds[second];
                if (!OverlapsBeyondTolerance(firstBounds, secondBounds, out var overlap)) continue;
                findings.Add(new LayoutFinding(context.Scene, context.Size, context.Zoom, context.Theme,
                    LayoutCheckNames.OverlappingText, Describe(first), LayoutBounds.From(firstBounds),
                    TextPairIdentity(first, second),
                    $"Text '{first.Text}' intersects text '{second.Text}' by {overlap.Width:0.#}×" +
                    $"{overlap.Height:0.#} px at {overlap}.", Describe(second)));
            }

            foreach (var text in texts)
            {
                findings.AddRange(MissingGlyphFindings(text, context, topLevel));
                findings.AddRange(TextBoxOverflowFindings(text, context, topLevel));
            }

        }

        findings.AddRange(TopBarFindings(root, context));
        if (window is not null) findings.AddRange(PopupBoundsFindings(root, window, context));
        return CollapseDuplicates(findings);
    }

    internal static bool IsInteractive(Control control) =>
        control is Button or ToggleButton or CheckBox or TextBox or ComboBox or MenuItem or RadioButton or Slider ||
        control is InputElement { IsTabStop: true, Focusable: true } and not TextBlock;

    internal static bool IsReachable(Control control, TopLevel topLevel)
    {
        if (!control.IsEffectivelyVisible || !control.IsEffectivelyEnabled) return true;
        var revealHost = control.GetVisualAncestors().OfType<Control>()
            .FirstOrDefault(ancestor => ancestor.Classes.Contains("hoverReveal"));
        if (revealHost is not null && !ReferenceEquals(revealHost, control))
            return IsReachableCore(revealHost, topLevel);
        return IsReachableCore(control, topLevel);
    }

    private static bool IsReachableCore(Control control, TopLevel topLevel)
    {
        if (!control.IsEffectivelyVisible || !control.IsEffectivelyEnabled) return true;
        var bounds = BoundsIn(control, topLevel);
        // Empty layout slots have no rendered hit target, as with inactive scrollbar buttons.
        if (bounds.Width <= 0 || bounds.Height <= 0) return true;

        foreach (var ancestor in control.GetVisualAncestors().OfType<Control>()
                     .Where(item => item.ClipToBounds && item is not TopLevel))
        {
            var clipBounds = BoundsIn(ancestor, topLevel);
            if (Contains(clipBounds, bounds)) continue;

            if (ancestor is ScrollContentPresenter)
            {
                // Only the viewer that owns this presenter can scroll the control out of its clip.
                var owner = ancestor.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
                if (owner is not null && ScrollCanReveal(control, owner)) continue;
            }

            if (control.GetVisualAncestors().OfType<ScrollViewer>().Any(viewer =>
                    (ReferenceEquals(ancestor, viewer) || IsAncestor(ancestor, viewer)) &&
                    ScrollCanReveal(control, viewer)))
            {
                continue;
            }

            return false;
        }

        var clientBounds = ClientBounds(topLevel);
        return Contains(clientBounds, bounds) ||
            control.GetVisualAncestors().OfType<ScrollViewer>().Any(viewer => ScrollCanReveal(control, viewer));
    }

    internal static IReadOnlyList<LayoutFinding> MissingGlyphFindings(
        TextBlock text, LayoutFindingContext context, Visual layoutRoot)
    {
        var findings = new List<LayoutFinding>();
        var requestedFamily = text.FontFamily?.ToString() ?? "<default>";
        var bounds = BoundsIn(text, layoutRoot);
        foreach (var run in text.TextLayout.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>())
        {
            var chosenFamily = run.GlyphRun.GlyphTypeface.FamilyName;
            foreach (var rune in run.Text.ToString().EnumerateRunes().Where(IsVisibleGlyph))
            {
                if (run.GlyphRun.GlyphTypeface.CharacterToGlyphMap.TryGetGlyph(rune.Value, out var glyph) && glyph != 0)
                    continue;

                findings.Add(new LayoutFinding(context.Scene, context.Size, context.Zoom, context.Theme,
                    LayoutCheckNames.MissingGlyph, Describe(text), LayoutBounds.From(bounds), $"U+{rune.Value:X4}",
                    $"U+{rune.Value:X4} has no glyph in the typeface Avalonia chose. Requested family: " +
                    $"'{requestedFamily}'; chosen family: '{chosenFamily}'."));
            }
        }

        return findings;
    }

    internal static IReadOnlyList<LayoutFinding> TextBoxOverflowFindings(
        TextBlock text, LayoutFindingContext context, Visual layoutRoot)
    {
        if (!IsRenderedText(text) || text.TextWrapping != TextWrapping.NoWrap ||
            text.TextTrimming != TextTrimming.None)
            return [];

        var desiredWidth = LayoutAssertions.MeasureWithoutWrapping(text);
        if (desiredWidth <= text.Bounds.Width + Tolerance) return [];
        var bounds = BoundsIn(text, layoutRoot);
        return [new LayoutFinding(context.Scene, context.Size, context.Zoom, context.Theme,
            LayoutCheckNames.TextBoxOverflow, Describe(text), LayoutBounds.From(bounds), ControlIdentity(text),
            $"Untrimmed text needs {desiredWidth:0.#} px but its arranged box is {text.Bounds.Width:0.#} px.")];
    }

    internal static IReadOnlyList<LayoutFinding> PopupBoundsFindings(
        Visual root, TopLevel window, LayoutFindingContext context)
    {
        var topBar = Controls(root).OfType<Control>().FirstOrDefault(control => control.Name == "TopBar");
        var belowTopBar = topBar is null ? 0 : BoundsIn(topBar, window).Bottom;
        var client = ClientBounds(window);
        var findings = new List<LayoutFinding>();
        foreach (var overlay in OpenPopupOverlays(root, window))
        {
            if (Contains(client, overlay.Bounds) && overlay.Bounds.Top >= belowTopBar - Tolerance) continue;
            findings.Add(new LayoutFinding(context.Scene, context.Size, context.Zoom, context.Theme,
                LayoutCheckNames.PopupOutsideWindow, Describe(overlay.Content), LayoutBounds.From(overlay.Bounds),
                "popup-window-client-and-top-bar",
                $"The open popup bounds {overlay.Bounds} must fit client bounds {client} and start below " +
                $"the top bar at y={belowTopBar:0.#} px."));
        }

        return findings;
    }

    private static IReadOnlyList<(Control Content, Rect Bounds)> OpenPopupOverlays(Visual root, TopLevel window)
    {
        var overlays = new List<(Control Content, Rect Bounds)>();
        var inspected = new HashSet<Control>();

        void Add(Control? content)
        {
            if (content is null || !inspected.Add(content)) return;
            var popupRoot = TopLevel.GetTopLevel(content);
            if (popupRoot is null) return;
            var bounds = BoundsIn(content, popupRoot);
            if (!ReferenceEquals(popupRoot, window))
            {
                var popupScreen = popupRoot.PointToScreen(new Point(bounds.X, bounds.Y));
                var origin = window.PointToClient(popupScreen);
                bounds = new Rect(origin, bounds.Size);
            }

            overlays.Add((content, bounds));
        }

        foreach (var control in Controls(root).OfType<Control>())
        {
            if (ToolTip.GetIsOpen(control)) Add(ToolTip.GetTip(control) as Control);
            if (control is Button { Flyout: PopupFlyoutBase { IsOpen: true } flyout }) Add(flyout.Popup.Child);
            if (control.ContextFlyout is PopupFlyoutBase { IsOpen: true } contextFlyout)
                Add(contextFlyout.Popup.Child);
            if (control is Popup { IsOpen: true, Child: { } popupContent }) Add(popupContent);
        }

        return overlays;
    }

    internal static IReadOnlyList<LayoutFinding> NavCompletenessFindings(
        Visual root, LayoutFindingContext context, TopLevel topLevel)
    {
        var pageList = Controls(root).OfType<ListBox>().FirstOrDefault(list => list.Name == "PageList");
        if (pageList is null) return [];
        var listBounds = BoundsIn(pageList, topLevel);
        var viewer = pageList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        var canReachAllItems = viewer is not null &&
            viewer.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled &&
            viewer.Extent.Height > viewer.Viewport.Height + Tolerance;
        if (canReachAllItems) return [];

        var viewport = pageList.GetVisualDescendants().OfType<ScrollContentPresenter>().FirstOrDefault();
        var viewportBounds = viewport is null ? listBounds : BoundsIn(viewport, topLevel);
        var findings = new List<LayoutFinding>();
        for (var index = 0; index < pageList.Items.Count; index++)
        {
            var item = pageList.ContainerFromIndex(index) as ListBoxItem;
            var itemBounds = item is null ? listBounds : BoundsIn(item, topLevel);
            if (item is not null && item.IsEffectivelyVisible && Contains(viewportBounds, itemBounds)) continue;
            findings.Add(new LayoutFinding(context.Scene, context.Size, context.Zoom, context.Theme,
                LayoutCheckNames.NavItemHidden, Describe((Control?)item ?? pageList), LayoutBounds.From(itemBounds),
                $"page-item-index:{index}",
                $"Sidebar page item {index + 1} must fit the list viewport or be reachable by scrolling. " +
                $"Viewport bounds: {viewportBounds}."));
        }

        return findings;
    }

    internal static IReadOnlyList<LayoutFinding> PageHorizontalScrollFindings(
        Visual root, LayoutFindingContext context, TopLevel topLevel)
    {
        var pageHost = Controls(root).OfType<Panel>().FirstOrDefault(control => control.Name == "PageHost");
        var page = pageHost?.Children.OfType<Control>().FirstOrDefault(control => control.IsEffectivelyVisible);
        if (pageHost is null || page is null) return [];

        var bodyBounds = BoundsIn(pageHost, topLevel);
        var findings = new List<LayoutFinding>();
        foreach (var viewer in Controls(page).OfType<ScrollViewer>()
                     .Where(viewer => viewer.IsEffectivelyVisible).Where(IsPageLevelScrollViewer))
        {
            if (viewer.Extent.Width <= viewer.Viewport.Width + Tolerance) continue;
            var bounds = BoundsIn(viewer, topLevel);
            findings.Add(new LayoutFinding(context.Scene, context.Size, context.Zoom, context.Theme,
                LayoutCheckNames.PageHorizontalScroll, Describe(viewer), LayoutBounds.From(bounds),
                $"extent:{viewer.Extent.Width:0.##};viewport:{viewer.Viewport.Width:0.##}",
                $"The page scroll viewer has horizontal extent {viewer.Extent.Width:0.#} px and " +
                $"viewport {viewer.Viewport.Width:0.#} px."));
        }

        var overflowing = Controls(page).OfType<Control>()
            .Where(control => control.IsEffectivelyVisible)
            .Where(control => !IsInsideInputControl(control))
            .Where(control => !ContainsHorizontally(bodyBounds, BoundsIn(control, topLevel)))
            .ToHashSet();
        foreach (var control in overflowing)
        {
            if (control.GetVisualAncestors().OfType<Control>().Any(overflowing.Contains)) continue;
            var bounds = BoundsIn(control, topLevel);
            findings.Add(new LayoutFinding(context.Scene, context.Size, context.Zoom, context.Theme,
                LayoutCheckNames.PageHorizontalScroll, Describe(control), LayoutBounds.From(bounds),
                $"page-body:{bodyBounds.Left:0.##}:{bodyBounds.Right:0.##}",
                $"Page content extends to x={bounds.Left:0.#}–{bounds.Right:0.#} px outside page-body " +
                $"bounds {bodyBounds.Left:0.#}–{bodyBounds.Right:0.#} px."));
        }

        return findings;
    }

    internal static IReadOnlyList<LayoutFinding> TopBarFindings(Visual root, LayoutFindingContext context)
    {
        var topBar = Controls(root).OfType<Control>().FirstOrDefault(control => control.Name == "TopBar");
        if (topBar is null) return [];

        var topLevel = TopLevel.GetTopLevel(topBar) ?? root as TopLevel;
        if (topLevel is null) return [];
        var panel = topBar.GetVisualDescendants().OfType<DockPanel>().FirstOrDefault();
        var children = panel?.Children.OfType<Control>().Where(child => child.IsEffectivelyVisible).ToArray() ?? [];
        var barBounds = BoundsIn(topBar, topLevel);
        var findings = new List<LayoutFinding>();

        foreach (var child in children)
        {
            var childBounds = BoundsIn(child, topLevel);
            if (Contains(barBounds, childBounds)) continue;
            findings.Add(new LayoutFinding(context.Scene, context.Size, context.Zoom, context.Theme,
                LayoutCheckNames.TitleBarOverflow, Describe(child), LayoutBounds.From(childBounds), "outside-top-bar",
                $"The visible top-bar child extends beyond TopBar bounds {barBounds}."));
        }

        for (var firstIndex = 0; firstIndex < children.Length; firstIndex++)
        for (var secondIndex = firstIndex + 1; secondIndex < children.Length; secondIndex++)
        {
            var first = children[firstIndex];
            var second = children[secondIndex];
            var firstBounds = BoundsIn(first, topLevel);
            var secondBounds = BoundsIn(second, topLevel);
            if (!OverlapsBeyondTolerance(firstBounds, secondBounds, out var overlap)) continue;
            findings.Add(new LayoutFinding(context.Scene, context.Size, context.Zoom, context.Theme,
                LayoutCheckNames.TitleBarOverlap, Describe(first), LayoutBounds.From(firstBounds),
                $"{ControlIdentity(first)}|{ControlIdentity(second)}",
                $"Top-bar children intersect by {overlap.Width:0.#}×{overlap.Height:0.#} px at {overlap}.",
                Describe(second)));
        }

        return findings;
    }

    private static IEnumerable<Visual> LayoutRoots(Visual root)
    {
        var pending = new Queue<Visual>();
        var seen = new HashSet<Visual>();
        var initial = TopLevel.GetTopLevel(root) ?? root as TopLevel ?? root;

        void Add(Control? control, bool preservePopupRoot = false)
        {
            if (control is null) return;
            var surface = preservePopupRoot ? control : (Visual?)TopLevel.GetTopLevel(control) ?? control;
            if (seen.Add(surface)) pending.Enqueue(surface);
        }

        if (seen.Add(initial)) pending.Enqueue(initial);
        if (root is Window && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            foreach (var window in desktop.Windows.Where(window => window != root && window.IsVisible)) Add(window);

        while (pending.TryDequeue(out var surface))
        {
            yield return surface;
            foreach (var control in Controls(surface).OfType<Control>())
            {
                if (ToolTip.GetIsOpen(control)) Add(ToolTip.GetTip(control) as Control, preservePopupRoot: true);
                if (control is Button { Flyout: PopupFlyoutBase { IsOpen: true } flyout })
                    Add(flyout.Popup.Child, preservePopupRoot: true);
                if (control.ContextFlyout is PopupFlyoutBase { IsOpen: true } contextFlyout)
                    Add(contextFlyout.Popup.Child, preservePopupRoot: true);
                if (control is Popup { IsOpen: true, Child: { } popupChild })
                    Add(popupChild, preservePopupRoot: true);
                if (control is SetupDialog { IsEffectivelyVisible: true }) Add(control, preservePopupRoot: true);
            }
        }
    }

    private static HashSet<Control> OpenModalControls(Visual surface)
    {
        var modalControls = new HashSet<Control>();
        if (surface is SetupDialog) return modalControls;
        foreach (var dialog in Controls(surface).OfType<SetupDialog>().Where(dialog => dialog.IsEffectivelyVisible))
            foreach (var control in Controls(dialog).OfType<Control>())
                modalControls.Add(control);
        return modalControls;
    }

    private static HashSet<Control> OpenPopupControls(Visual surface)
    {
        var excluded = new HashSet<Control>();
        var pending = new Queue<Visual>();
        pending.Enqueue(surface);

        while (pending.TryDequeue(out var current))
        foreach (var control in Controls(current).OfType<Control>())
        {
            if (ToolTip.GetIsOpen(control)) Add(ToolTip.GetTip(control) as Control);
            if (control is Button { Flyout: PopupFlyoutBase { IsOpen: true } flyout }) Add(flyout.Popup.Child);
            if (control.ContextFlyout is PopupFlyoutBase { IsOpen: true } contextFlyout)
                Add(contextFlyout.Popup.Child);
            if (control is Popup { IsOpen: true, Child: { } popupChild }) Add(popupChild);

            void Add(Control? child)
            {
                if (child is null) return;
                foreach (var nested in Controls(child).OfType<Control>())
                    if (excluded.Add(nested)) pending.Enqueue(nested);
            }
        }

        return excluded;
    }

    private static IEnumerable<Control> Controls(Visual root) => root.GetSelfAndVisualDescendants().OfType<Control>();

    private static bool IsRenderedText(TextBlock text) =>
        text.IsEffectivelyVisible && text.Bounds.Width > 0 && text.Bounds.Height > 0 &&
        text.TextLayout.TextLines.Count > 0 && HasVisibleOpacity(text);

    private static bool HasVisibleOpacity(Visual visual) =>
        visual.Opacity > 0 && visual.GetVisualAncestors().OfType<Visual>().All(ancestor => ancestor.Opacity > 0);

    private static bool IsVisibleGlyph(Rune rune)
    {
        if (Rune.IsWhiteSpace(rune)) return false;
        return Rune.GetUnicodeCategory(rune) is not UnicodeCategory.Control and not UnicodeCategory.Format and
            not UnicodeCategory.Surrogate;
    }

    private static bool IsAncestor(Visual ancestor, Visual descendant) =>
        descendant.GetVisualAncestors().Any(item => ReferenceEquals(item, ancestor));

    // A non-default ZIndex in one Grid cell marks a deliberate overlay.
    private static bool IntentionallyLayered(TextBlock first, TextBlock second)
    {
        if (first.GetVisualParent() is not Grid grid || !ReferenceEquals(second.GetVisualParent(), grid)) return false;
        var firstZ = first.ZIndex;
        var secondZ = second.ZIndex;
        return firstZ != secondZ && (firstZ != 0 || secondZ != 0) &&
            Grid.GetRow(first) == Grid.GetRow(second) && Grid.GetColumn(first) == Grid.GetColumn(second) &&
            Grid.GetRowSpan(first) == Grid.GetRowSpan(second) && Grid.GetColumnSpan(first) == Grid.GetColumnSpan(second);
    }

    private static bool ScrollCanReveal(Control control, ScrollViewer viewer)
    {
        if (viewer.Content is not Visual content) return false;
        var bounds = BoundsIn(control, content);
        if (bounds.Left < -Tolerance || bounds.Top < -Tolerance ||
            bounds.Right > viewer.Extent.Width + Tolerance || bounds.Bottom > viewer.Extent.Height + Tolerance)
            return false;

        var visibleX = bounds.Left >= viewer.Offset.X - Tolerance &&
            bounds.Right <= viewer.Offset.X + viewer.Viewport.Width + Tolerance;
        var visibleY = bounds.Top >= viewer.Offset.Y - Tolerance &&
            bounds.Bottom <= viewer.Offset.Y + viewer.Viewport.Height + Tolerance;
        var canScrollX = visibleX || viewer.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled &&
            viewer.Extent.Width > viewer.Viewport.Width + Tolerance;
        var canScrollY = visibleY || viewer.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled &&
            viewer.Extent.Height > viewer.Viewport.Height + Tolerance;
        return canScrollX && canScrollY;
    }

    private static string ScrollRouteDiagnostics(Control control)
    {
        var viewers = control.GetVisualAncestors().OfType<ScrollViewer>().ToArray();
        if (viewers.Length == 0) return "No ScrollViewer ancestor.";
        return "ScrollViewer ancestors: " + string.Join("; ", viewers.Select(viewer =>
            $"content={viewer.Content?.GetType().Name ?? "none"}, extent={viewer.Extent}, " +
            $"viewport={viewer.Viewport}, offset={viewer.Offset}, bars=" +
            $"{viewer.HorizontalScrollBarVisibility}/{viewer.VerticalScrollBarVisibility}, " +
            $"route={ScrollCanReveal(control, viewer)}")) + ".";
    }

    private static bool IsPageLevelScrollViewer(ScrollViewer viewer) =>
        !viewer.GetVisualAncestors().OfType<ScrollViewer>().Any() &&
        !viewer.GetVisualAncestors().OfType<ItemsControl>().Any() &&
        !IsInsideInputControl(viewer);

    private static bool IsInsideInputControl(Control control) =>
        control.GetVisualAncestors().OfType<Control>().Any(ancestor => ancestor is TextBox or ComboBox);

    private static bool OverlapsBeyondTolerance(Rect first, Rect second, out Rect overlap)
    {
        var left = Math.Max(first.Left, second.Left);
        var top = Math.Max(first.Top, second.Top);
        var right = Math.Min(first.Right, second.Right);
        var bottom = Math.Min(first.Bottom, second.Bottom);
        overlap = right > left && bottom > top ? new Rect(left, top, right - left, bottom - top) : default;
        return overlap.Width > Tolerance && overlap.Height > Tolerance;
    }

    private static Rect PaintedBounds(Visual visual) =>
        visual.GetTransformedBounds() is { } bounds
            ? bounds.Bounds.TransformToAABB(bounds.Transform).Intersect(bounds.Clip)
            : default;

    private static Rect BoundsIn(Visual visual, Visual root)
    {
        var bounds = visual.Bounds;
        var corners = new[]
        {
            new Point(0, 0), new Point(bounds.Width, 0),
            new Point(0, bounds.Height), new Point(bounds.Width, bounds.Height),
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

    private static bool ContainsHorizontally(Rect container, Rect content) =>
        container.Left <= content.Left + Tolerance && container.Right >= content.Right - Tolerance;

    private static Rect ClientBounds(TopLevel topLevel) => new(0, 0, topLevel.ClientSize.Width, topLevel.ClientSize.Height);

    private static string ControlIdentity(Control control) =>
        $"{control.GetType().Name}#{control.Name}#{AutomationProperties.GetAutomationId(control)}" +
        $".{string.Join('.', control.Classes)}";

    private static string TextPairIdentity(Control first, Control second)
    {
        var secondAncestors = second.GetVisualAncestors().OfType<Control>().ToHashSet();
        var sharedContainer = first.GetVisualAncestors().OfType<Control>()
            .FirstOrDefault(ancestor => secondAncestors.Contains(ancestor));
        var container = sharedContainer is null ? "none" : ControlIdentity(sharedContainer);
        return $"{ControlIdentity(first)}|{ControlIdentity(second)}|container:{container}";
    }

    private static ControlDescription Describe(Control control) => new(
        control.GetType().Name, control.Name, AutomationProperties.GetAutomationId(control),
        AutomationProperties.GetName(control), string.Join('.', control.Classes), NearestNamedAncestor(control));

    private static string? NearestNamedAncestor(Control control) => control.GetVisualAncestors()
        .OfType<Control>().Select(ancestor => ancestor.Name ?? AutomationProperties.GetAutomationId(ancestor))
        .FirstOrDefault(name => !string.IsNullOrEmpty(name) && !name.StartsWith("PART_", StringComparison.Ordinal));

    internal static IReadOnlyList<LayoutFinding> CollapseDuplicates(IEnumerable<LayoutFinding> findings) =>
        findings.GroupBy(finding => finding.Key)
            .Select(group => group.First() with { Count = group.Sum(finding => finding.Count) })
            .ToArray();
}

internal sealed record LayoutFindingContext(string Scene, string Size, int Zoom, string Theme);

/// <summary>
/// What a layout finding says about one control. <paramref name="NamedAncestor"/> is the nearest visual ancestor
/// that has a Name or AutomationId and is not a template part, which names a control that cannot carry an id itself, such as the
/// AccessText a Button creates for its label.
/// </summary>
internal sealed record ControlDescription(
    string Type, string? Name, string? AutomationId, string? AutomationName, string Classes,
    string? NamedAncestor = null);

internal sealed record LayoutBounds(double X, double Y, double Width, double Height)
{
    internal static LayoutBounds From(Rect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
}

internal sealed record LayoutFinding(
    string Scene,
    string Size,
    int Zoom,
    string Theme,
    string Check,
    ControlDescription Control,
    LayoutBounds Bounds,
    string Identity,
    string Detail,
    ControlDescription? RelatedControl = null,
    int Count = 1)
{
    internal LayoutFindingKey Key => new(Scene, Size, Zoom, Theme, Check, StableControlKey(Control), Identity,
        RelatedControl is null ? null : StableControlKey(RelatedControl));

    private static string StableControlKey(ControlDescription control) =>
        $"{control.Type}#{control.Name}#{control.AutomationId}.{control.Classes}";
}

internal sealed record LayoutFindingKey(
    string Scene,
    string Size,
    int Zoom,
    string Theme,
    string Check,
    string Control,
    string Identity,
    string? RelatedControl);

internal static class LayoutCheckNames
{
    internal const string UnreachableControl = "unreachable-control";
    internal const string OverlappingText = "overlapping-text";
    internal const string MissingGlyph = "missing-glyph";
    internal const string TitleBarOverflow = "title-bar-overflow";
    internal const string TitleBarOverlap = "title-bar-overlap";
    internal const string TextBoxOverflow = "text-own-box-overflow";
    internal const string PopupOutsideWindow = "popup-outside-window";
    internal const string NavItemHidden = "nav-item-hidden";
    internal const string PageHorizontalScroll = "page-horizontal-scroll";

    /// <summary>The checks that <c>InspectReachabilityAndTitleBar</c> can report.</summary>
    internal static IReadOnlySet<string> ReachabilityAndTitleBar { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        UnreachableControl, NavItemHidden, PageHorizontalScroll, TitleBarOverflow, TitleBarOverlap,
        PopupOutsideWindow,
    };
}
