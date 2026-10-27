using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using SIL.Motif.App.Views;
using DenseTable = SIL.Motif.App.Controls.DenseTable;
using static SIL.Motif.Tests.App.LayoutAssertions;

namespace SIL.Motif.Tests.App;

internal static class ReachabilityAssertions
{
    internal const double Tolerance = LayoutAssertions.Tolerance;

    /// <summary>
    /// Whether a <c>mid-word-wrap</c> finding fails the run that sees it. While false, the engine logs each one to
    /// the list a caller passes as <c>reportOnly</c> instead of returning it. Flip it to true once the known
    /// mid-word breaks are fixed.
    /// </summary>
    internal static readonly bool MidWordWrapFailsTheRun = false;

    /// <summary>
    /// Whether an <c>edge-crowding</c> finding fails the run that sees it. While false, the engine logs each one to
    /// the list a caller passes as <c>reportOnly</c> instead of returning it. Flip it to true once the known
    /// crowded edges are fixed.
    /// </summary>
    internal static readonly bool EdgeCrowdingFailsTheRun = false;

    /// <summary>The smallest gap between painted text and a drawn edge, equal to the Intent.Space.Related spacing.</summary>
    internal const double EdgeClearance = 6;

    // A line box already carries leading above and below the glyphs, so top and bottom need a third of it.
    private const double VerticalClearanceShare = 1.0 / 3;

    internal static IReadOnlyList<LayoutFinding> Inspect(Visual root, LayoutFindingContext context,
        LayoutTextMeasurements? measurements = null, List<LayoutFinding>? reportOnly = null) =>
        InspectCore(root, context, includeTextChecks: true, measurements: measurements, reportOnly: reportOnly);

    internal static IReadOnlyList<LayoutFinding> InspectQuick(Visual root, LayoutFindingContext context,
        List<LayoutFinding>? reportOnly = null) =>
        InspectCore(root, context, includeTextChecks: true, includeDetailedTextChecks: false, reportOnly: reportOnly);

    internal static IReadOnlyList<LayoutFinding> InspectReachabilityAndTitleBar(
        Visual root, LayoutFindingContext context) => InspectCore(root, context, includeTextChecks: false);

    private static IReadOnlyList<LayoutFinding> InspectCore(
        Visual root, LayoutFindingContext context, bool includeTextChecks, bool includeDetailedTextChecks = true,
        LayoutTextMeasurements? measurements = null, List<LayoutFinding>? reportOnly = null)
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
            var pageBehindLightDismiss = HasOpenLightDismissPopup(surface);
            var controls = Controls(surface)
                .Where(control => ReferenceEquals(TopLevel.GetTopLevel(control), topLevel))
                .Where(control => !popupControls.Contains(control))
                .Where(control => !modalControls.Contains(control))
                .ToArray();
            foreach (var control in controls.Where(IsInteractive).Where(_ => !modalCoversSurface)
                         .Where(_ => !pageBehindLightDismiss)
                         .Where(control => popupOverlays.All(overlay =>
                             !overlay.Bounds.Intersects(BoundsIn(control, topLevel)))))
            {
                if (!ReferenceEquals(TopLevel.GetTopLevel(control), topLevel) || IsReachable(control, topLevel)) continue;
                if (!ReferenceEquals(TopLevel.GetTopLevel(control), topLevel)) continue;
                var bounds = BoundsIn(control, topLevel);
                findings.Add(new LayoutFinding(context.Scene, context.Size, context.Zoom, context.Theme,
                    LayoutCheckNames.UnreachableControl, Describe(control), LayoutBounds.From(bounds),
                    "no-visible-hit-target-or-scroll-route",
                    $"The enabled control has no visible hit target in its client area or a valid scroll route. " +
                    $"Client bounds: {ClientBounds(topLevel)}. {ScrollRouteDiagnostics(control)}"));
            }

            controls = Controls(surface).Where(control => ReferenceEquals(TopLevel.GetTopLevel(control), topLevel))
                .Where(control => !popupControls.Contains(control) && !modalControls.Contains(control)).ToArray();
            var authored = controls.Where(control => control.IsEffectivelyVisible)
                .Where(control => control.TemplatedParent is null).ToArray();
            findings.AddRange(AutomationFindings(authored, context, topLevel));
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
                var firstBounds = paintedBounds[first];
                var secondBounds = paintedBounds[second];
                if (!OverlapsBeyondTolerance(firstBounds, secondBounds, out var overlap)) continue;
                if (IsAncestor(first, second) || IsAncestor(second, first) || IntentionallyLayered(first, second))
                    continue;
                findings.Add(new LayoutFinding(context.Scene, context.Size, context.Zoom, context.Theme,
                    LayoutCheckNames.OverlappingText, Describe(first), LayoutBounds.From(firstBounds),
                    TextPairIdentity(first, second),
                    $"Text '{first.Text}' intersects text '{second.Text}' by {overlap.Width:0.#}×" +
                    $"{overlap.Height:0.#} px at {overlap}.", Describe(second)));
            }

            foreach (var text in texts) findings.AddRange(MidWordWrapFindings(text, context, topLevel));
            foreach (var text in texts) findings.AddRange(EdgeCrowdingFindings(text, context, topLevel));

            foreach (var text in texts.Where(_ => includeDetailedTextChecks))
            {
                findings.AddRange(MissingGlyphFindings(text, context, topLevel));
                findings.AddRange(TextBoxOverflowFindings(text, context, topLevel, measurements));
            }

        }

        findings.AddRange(TopBarFindings(root, context));
        if (window is not null) findings.AddRange(PopupBoundsFindings(root, window, context));
        if (!MidWordWrapFailsTheRun)
        {
            reportOnly?.AddRange(CollapseDuplicates(findings.Where(finding =>
                finding.Check == LayoutCheckNames.MidWordWrap)));
            findings.RemoveAll(finding => finding.Check == LayoutCheckNames.MidWordWrap);
        }
        if (!EdgeCrowdingFailsTheRun)
        {
            reportOnly?.AddRange(CollapseDuplicates(findings.Where(finding =>
                finding.Check == LayoutCheckNames.EdgeCrowding)));
            findings.RemoveAll(finding => finding.Check == LayoutCheckNames.EdgeCrowding);
        }
        return CollapseDuplicates(findings);
    }

    internal static bool IsInteractive(Control control) =>
        control is Button or ToggleButton or CheckBox or TextBox or ComboBox or MenuItem or RadioButton or Slider ||
        control is InputElement { IsTabStop: true, Focusable: true } and not TextBlock;

    internal static bool IsReachable(Control control, TopLevel topLevel)
    {
        if (!control.IsEffectivelyVisible || !control.IsEffectivelyEnabled) return true;
        // Empty layout slots have no rendered hit target, as with inactive scrollbar buttons.
        if (control.Bounds.Width <= 0 || control.Bounds.Height <= 0) return true;
        var revealHost = control.GetVisualAncestors().OfType<Control>()
            .FirstOrDefault(ancestor => ancestor.Classes.Contains("hoverReveal"));
        if (revealHost is not null && !ReferenceEquals(revealHost, control))
            return IsReachableCore(revealHost, topLevel);
        if (!IsInteractive(control)) return IsReachableCore(control, topLevel);
        if (!HasVisibleOpacity(control) || control.GetSelfAndVisualAncestors().OfType<Control>()
                .Any(ancestor => !ancestor.IsHitTestVisible)) return false;
        if (HasHitTarget(control, topLevel)) return true;
        if (!IsReachableCore(control, topLevel)) return false;
        var viewers = control.GetVisualAncestors().OfType<ScrollViewer>()
            .Select(viewer => (Viewer: viewer, Offset: viewer.Offset)).ToArray();
        if (viewers.Length == 0) return false;
        try
        {
            foreach (var (viewer, _) in viewers)
            {
                if (viewer.Content is not Visual content) continue;
                var target = ExtentBounds(control, content);
                var x = viewer.Offset.X;
                var y = viewer.Offset.Y;
                if (viewer.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled)
                    x = RevealOffset(x, target.Left, target.Right, viewer.Viewport.Width, viewer.Extent.Width);
                if (viewer.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled)
                    y = RevealOffset(y, target.Top, target.Bottom, viewer.Viewport.Height, viewer.Extent.Height);
                var offset = new Vector(x, y);
                if (offset == viewer.Offset) continue;
                viewer.Offset = offset;
                SettleHitTesting(topLevel);
            }
            return HasHitTarget(control, topLevel);
        }
        finally
        {
            foreach (var (viewer, offset) in viewers) viewer.Offset = offset;
            SettleHitTesting(topLevel);
        }
    }

    // A scroll extent includes the content's margin, so a control's place in it is offset by that margin.
    internal static Rect ExtentBounds(Control control, Visual content)
    {
        var margin = content is Layoutable layoutable ? layoutable.Margin : default;
        return BoundsIn(control, content).Translate(new Vector(margin.Left, margin.Top));
    }

    private static double RevealOffset(double current, double start, double end, double viewport, double extent)
    {
        var offset = start < current ? start : end > current + viewport ? end - viewport : current;
        return Math.Clamp(offset, 0, Math.Max(0, extent - viewport));
    }

    internal static bool CanReachItem(ListBox list, int index, TopLevel topLevel)
    {
        if (index < 0 || index >= list.ItemCount) return false;
        if (list.ContainerFromIndex(index) is Control current && HasHitTarget(current, topLevel)) return true;
        var offsets = list.GetVisualDescendants().OfType<ScrollViewer>()
            .Select(viewer => (Viewer: viewer, Offset: viewer.Offset)).ToArray();
        try
        {
            list.ScrollIntoView(index);
            SettleHitTesting(topLevel);
            return list.ContainerFromIndex(index) is Control revealed && IsReachable(revealed, topLevel);
        }
        finally
        {
            foreach (var (viewer, offset) in offsets) viewer.Offset = offset;
            SettleHitTesting(topLevel);
        }
    }

    private static void SettleHitTesting(TopLevel topLevel)
    {
        Dispatcher.UIThread.RunJobs();
        topLevel.UpdateLayout();
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        // A newly realized container can need a second frame before hit testing sees it.
        Dispatcher.UIThread.RunJobs();
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    private static bool HasHitTarget(Control control, TopLevel topLevel)
    {
        var bounds = BoundsIn(control, topLevel);
        if (!Contains(ClientBounds(topLevel), bounds)) return false;
        foreach (var ancestor in control.GetVisualAncestors().OfType<Control>().Where(item => item.ClipToBounds))
            if (!Contains(BoundsIn(ancestor, topLevel), bounds)) return false;
        if (bounds.Width <= Tolerance || bounds.Height <= Tolerance) return false;
        foreach (var (x, y) in new[] { (0.5, 0.5), (0.25, 0.25), (0.75, 0.25), (0.25, 0.75), (0.75, 0.75) })
        {
            var point = new Point(bounds.X + bounds.Width * x, bounds.Y + bounds.Height * y);
            if (topLevel.InputHitTest(point) is Visual hit && (ReferenceEquals(hit, control) || IsAncestor(control, hit)))
                return true;
        }
        return false;
    }

    internal static IReadOnlyList<LayoutFinding> AutomationFindings(
        IReadOnlyList<Control> controls, LayoutFindingContext context, TopLevel topLevel)
    {
        var findings = new List<LayoutFinding>();
        foreach (var control in controls.Where(IsInteractive))
        {
            var name = ControlAutomationPeer.CreatePeerForElement(control)?.GetName();
            var typeFallback = control is ContentControl { Content: Control content } && name == content.ToString();
            if (!string.IsNullOrWhiteSpace(name) && !typeFallback) continue;
            findings.Add(new LayoutFinding(context.Scene, context.Size, context.Zoom, context.Theme,
                LayoutCheckNames.MissingAutomationName, Describe(control), LayoutBounds.From(BoundsIn(control, topLevel)),
                "runtime-peer-name", "The interactive control has no nonempty runtime automation-peer name."));
        }
        var identities = controls.Where(IsInteractive).ToArray();
        var scopes = identities.Select(control => control.GetVisualAncestors().OfType<Control>()
            .FirstOrDefault(ancestor => !string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(ancestor))))
            .OfType<Control>();
        foreach (var group in identities.Concat(scopes).Distinct().GroupBy(control => (
                     Scope: control.GetVisualAncestors().OfType<Control>().FirstOrDefault(ancestor =>
                         !string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(ancestor))) ?? topLevel,
                     Id: AutomationProperties.GetAutomationId(control)))
                     .Where(group => !string.IsNullOrWhiteSpace(group.Key.Id) && group.Count() > 1))
        {
            var first = group.First();
            foreach (var other in group.Skip(1))
                findings.Add(new LayoutFinding(context.Scene, context.Size, context.Zoom, context.Theme,
                    LayoutCheckNames.DuplicateAutomationId, Describe(first), LayoutBounds.From(BoundsIn(first, topLevel)),
                    group.Key.Id!, "Multiple visible controls share this automation ID in the same identified container.", Describe(other)));
        }
        return findings;
    }

    private static bool IsReachableCore(Control control, TopLevel topLevel)
    {
        if (!control.IsEffectivelyVisible || !control.IsEffectivelyEnabled) return true;
        var bounds = BoundsIn(control, topLevel);
        if (bounds.Width <= 0 || bounds.Height <= 0) return true;
        var needsScrolling = false;

        foreach (var ancestor in control.GetVisualAncestors().OfType<Control>()
                     .Where(item => item.ClipToBounds && item is not TopLevel))
        {
            var clipBounds = BoundsIn(ancestor, topLevel);
            if (Contains(clipBounds, bounds)) continue;

            if (ancestor is ScrollContentPresenter)
            {
                // Only the viewer that owns this presenter can scroll the control out of its clip.
                var owner = ancestor.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
                if (owner is not null && ScrollCanReveal(control, owner))
                {
                    needsScrolling = true;
                    continue;
                }
            }

            if (control.GetVisualAncestors().OfType<ScrollViewer>().Any(viewer =>
                    (ReferenceEquals(ancestor, viewer) || IsAncestor(ancestor, viewer)) &&
                    ScrollCanReveal(control, viewer)))
            {
                needsScrolling = true;
                continue;
            }

            return false;
        }

        var clientBounds = ClientBounds(topLevel);
        if (!Contains(clientBounds, bounds))
            return control.GetVisualAncestors().OfType<ScrollViewer>().Any(viewer => ScrollCanReveal(control, viewer));
        // A covered hit target matters only for a control a person acts on, not for a container such as a reveal host.
        if (needsScrolling || !IsInteractive(control)) return true;
        foreach (var (x, y) in new[] { (0.5, 0.5), (0.25, 0.25), (0.75, 0.25), (0.25, 0.75), (0.75, 0.75) })
        {
            var point = control.TranslatePoint(new Point(control.Bounds.Width * x, control.Bounds.Height * y), topLevel);
            if (point is null || topLevel.InputHitTest(point.Value) is not Visual hit) continue;
            if (ReferenceEquals(hit, control) || IsAncestor(control, hit)) return true;
        }
        return false;
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

    /// <summary>Flags a wrapped TextBlock that breaks a line between two letters or digits of one word.</summary>
    internal static IReadOnlyList<LayoutFinding> MidWordWrapFindings(
        TextBlock text, LayoutFindingContext context, Visual layoutRoot)
    {
        if (!IsRenderedText(text) || text.TextWrapping == TextWrapping.NoWrap || string.IsNullOrEmpty(text.Text))
            return [];
        var content = text.Text;
        var lines = text.TextLayout.TextLines;
        for (var index = 0; index + 1 < lines.Count; index++)
        {
            var line = lines[index];
            if (line.NewLineLength > 0) continue;
            var last = line.FirstTextSourceIndex + line.Length - 1;
            var next = lines[index + 1].FirstTextSourceIndex;
            if (last < 0 || next >= content.Length || next != last + 1) continue;
            if (!InsideWord(content[last]) || !InsideWord(content[next])) continue;
            var bounds = BoundsIn(text, layoutRoot);
            return [new LayoutFinding(context.Scene, context.Size, context.Zoom, context.Theme,
                LayoutCheckNames.MidWordWrap, Describe(text), LayoutBounds.From(bounds), ControlIdentity(text),
                $"Line {index + 1} ends '{WordAround(content, last, forward: false)}' and the next begins " +
                $"'{WordAround(content, next, forward: true)}' inside one word of '{Truncate(content)}'; " +
                $"the text box is {text.Bounds.Width:0.#} px wide.")];
        }

        return [];

        // A script written without spaces breaks between letters by design.
        static bool InsideWord(char character) => char.IsLetterOrDigit(character) && !IsUnspacedScript(character);

        static bool IsUnspacedScript(char character) => character is >= '\u0E00' and <= '\u0EFF'
            or >= '\u2E80' and <= '\u9FFF' or >= '\uAC00' and <= '\uD7AF' or >= '\uF900' and <= '\uFAFF';

        static string WordAround(string value, int position, bool forward)
        {
            var start = position;
            var end = position;
            if (forward) while (end + 1 < value.Length && char.IsLetterOrDigit(value[end + 1])) end++;
            else while (start > 0 && char.IsLetterOrDigit(value[start - 1])) start--;
            return value[start..(end + 1)];
        }

        static string Truncate(string value) => value.Length <= 60 ? value : value[..60] + "...";
    }

    /// <summary>The largest share of the client area that may hold no painted content before a view counts as sparse.</summary>
    internal const double MaxEmptyAreaRatio = 0.9;

    /// <summary>The side of one occupancy cell, in client pixels.</summary>
    internal const double EmptyAreaCell = 8;

    /// <summary>Flags a surface whose client area is mostly bare: the share of cells that no content covers.</summary>
    internal static IReadOnlyList<LayoutFinding> EmptyAreaFindings(
        Visual root, LayoutFindingContext context, double maxRatio = MaxEmptyAreaRatio)
    {
        var topLevel = TopLevel.GetTopLevel(root) ?? root as TopLevel;
        if (topLevel is null) return [];
        var client = ClientBounds(topLevel);
        if (client.Width <= 0 || client.Height <= 0) return [];
        var columns = (int)Math.Ceiling(client.Width / EmptyAreaCell);
        var rows = (int)Math.Ceiling(client.Height / EmptyAreaCell);
        var covered = new bool[columns, rows];
        var popupControls = OpenPopupControls(topLevel);
        foreach (var control in Controls(topLevel).Where(control => ReferenceEquals(TopLevel.GetTopLevel(control), topLevel))
                     .Where(control => !popupControls.Contains(control)).Where(IsPaintedContent))
        {
            var bounds = PaintedBounds(control).Intersect(client);
            if (bounds.Width <= 0 || bounds.Height <= 0) continue;
            var firstColumn = (int)Math.Floor(bounds.Left / EmptyAreaCell);
            var lastColumn = Math.Min(columns - 1, (int)Math.Ceiling(bounds.Right / EmptyAreaCell) - 1);
            var firstRow = (int)Math.Floor(bounds.Top / EmptyAreaCell);
            var lastRow = Math.Min(rows - 1, (int)Math.Ceiling(bounds.Bottom / EmptyAreaCell) - 1);
            for (var column = firstColumn; column <= lastColumn; column++)
            for (var row = firstRow; row <= lastRow; row++)
                covered[column, row] = true;
        }

        var empty = 0;
        foreach (var cell in covered) if (!cell) empty++;
        var ratio = empty / (double)(columns * rows);
        if (ratio <= maxRatio) return [];
        return [new LayoutFinding(context.Scene, context.Size, context.Zoom, context.Theme,
            LayoutCheckNames.EmptyArea, Describe(topLevel as Control ?? Controls(topLevel).First()),
            LayoutBounds.From(client), "empty-area-ratio",
            $"{ratio:P0} of the {client.Width:0}×{client.Height:0} client area holds no text, image or control; " +
            $"the most a view may leave empty is {maxRatio:P0}.")];
    }

    // Containers and list hosts fill their box whether or not anything is in it, so only what they hold counts.
    private static bool IsPaintedContent(Control control) =>
        control.IsEffectivelyVisible && HasVisibleOpacity(control) && control.Bounds.Width > 0 && control.Bounds.Height > 0 &&
        (control is TextBlock text ? IsRenderedText(text) :
            control is Avalonia.Controls.Image or Avalonia.Controls.Shapes.Shape ||
            IsInteractive(control) && control is not (ScrollViewer or ItemsControl or TabControl or Window));

    private static readonly string[] FramePaneNames =
        ["SidePanelBorder", "InspectorPane", "WorkingPageHost", "TopBar", "StatusBar"];

    /// <summary>Flags text whose painted bounds sit closer than the edge clearance to a drawn edge of its panel.</summary>
    internal static IReadOnlyList<LayoutFinding> EdgeCrowdingFindings(
        TextBlock text, LayoutFindingContext context, Visual layoutRoot, double clearance = EdgeClearance)
    {
        if (!IsRenderedText(text) || PaintedBounds(text) is not { Width: > 0, Height: > 0 }) return [];
        var ancestors = text.GetVisualAncestors().OfType<Control>().ToArray();
        // These templates pad their own content, and a table cell is flush by design.
        if (ancestors.Any(ancestor => ancestor is Button or ToggleButton or ComboBox or TextBox or CheckBox
                or RadioButton or MenuItem or TabItem or ListBoxItem or ComboBoxItem ||
                ancestor is Grid grid && !string.IsNullOrEmpty(DenseTable.GetColumns(grid)))) return [];

        var ink = InkBounds(text, layoutRoot);
        var findings = new List<LayoutFinding>();
        var found = new HashSet<string>();
        for (var index = 0; index < ancestors.Length && found.Count < 4; index++)
        {
            var ancestor = ancestors[index];
            var edges = DrawnEdges(ancestor, ancestors[(index + 1)..]);
            if (edges.Length == 0) continue;
            var box = BoundsIn(ancestor, layoutRoot);
            var thickness = ancestor is Border border ? border.BorderThickness : default;
            foreach (var edge in edges.Where(found.Add))
            {
                var distance = edge switch
                {
                    "left" => ink.Left - (box.Left + thickness.Left),
                    "right" => box.Right - thickness.Right - ink.Right,
                    "top" => ink.Top - (box.Top + thickness.Top),
                    _ => box.Bottom - thickness.Bottom - ink.Bottom,
                };
                var needed = edge is "left" or "right" ? clearance : clearance * VerticalClearanceShare;
                if (distance < -Tolerance || distance >= needed - Tolerance) continue;
                findings.Add(new LayoutFinding(context.Scene, context.Size, context.Zoom, context.Theme,
                    LayoutCheckNames.EdgeCrowding, Describe(text), LayoutBounds.From(ink),
                    $"{ControlIdentity(text)}:{edge}:{ControlIdentity(ancestor)}",
                    $"Text '{text.Text}' is {Math.Max(0, distance):0.#} px from the {edge} edge of " +
                    $"{ControlIdentity(ancestor)}; the clearance is {needed:0.#} px.", Describe(ancestor)));
            }
        }

        return findings;
    }

    // The sides on which this ancestor draws something the eye reads as an edge.
    private static string[] DrawnEdges(Control ancestor, IReadOnlyList<Control> outer)
    {
        if (ancestor.Name is { } name && FramePaneNames.Contains(name)) return ["left", "right", "top", "bottom"];
        var sides = new List<string>();
        if (ancestor is Border { BorderBrush: not null } border)
        {
            var thickness = border.BorderThickness;
            if (thickness.Left > 0) sides.Add("left");
            if (thickness.Right > 0) sides.Add("right");
            if (thickness.Top > 0) sides.Add("top");
            if (thickness.Bottom > 0) sides.Add("bottom");
        }

        if (sides.Count == 0 && SolidColor(ancestor) is { } own &&
            (outer.Select(SolidColor).FirstOrDefault(color => color is not null) is not { } parent || parent != own))
            return ["left", "right", "top", "bottom"];
        return [.. sides];
    }

    private static Color? SolidColor(Control control)
    {
        var brush = control switch
        {
            Border border => border.Background,
            Panel panel => panel.Background,
            TemplatedControl templated => templated.Background,
            _ => null,
        };
        return brush is ISolidColorBrush { Color.A: > 0 } solid ? solid.Color : null;
    }

    // Ink spans the arranged text, inset by its padding and placed by its alignment within the box.
    private static Rect InkBounds(TextBlock text, Visual layoutRoot)
    {
        var box = BoundsIn(text, layoutRoot);
        var layout = text.TextLayout;
        var contentWidth = Math.Max(0, box.Width - text.Padding.Left - text.Padding.Right);
        var inkWidth = Math.Min(contentWidth, layout.WidthIncludingTrailingWhitespace);
        var offset = text.TextAlignment switch
        {
            TextAlignment.Center => (contentWidth - inkWidth) / 2,
            TextAlignment.Right or TextAlignment.End => contentWidth - inkWidth,
            _ => 0,
        };
        var left = box.Left + text.Padding.Left + offset;
        var height = Math.Min(Math.Max(0, box.Height - text.Padding.Top - text.Padding.Bottom), layout.Height);
        return new Rect(left, box.Top + text.Padding.Top, inkWidth, height);
    }

    internal static IReadOnlyList<LayoutFinding> TextBoxOverflowFindings(
        TextBlock text, LayoutFindingContext context, Visual layoutRoot, LayoutTextMeasurements? measurements = null)
    {
        if (!IsRenderedText(text) || text.TextWrapping != TextWrapping.NoWrap ||
            text.TextTrimming != TextTrimming.None)
            return [];

        var desiredWidth = measurements?.UnwrappedWidth(text) ?? LayoutAssertions.MeasureWithoutWrapping(text);
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
            .Where(control => PaintedBounds(control) is { Width: > 0, Height: > 0 } painted &&
                !ContainsHorizontally(bodyBounds, painted))
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

    // An open light-dismiss popup puts its dismiss layer over whatever it opened from, page or parent popup.
    private static bool HasOpenLightDismissPopup(Visual surface)
    {
        foreach (var control in Controls(surface).OfType<Control>())
        {
            if (control is Button { Flyout: PopupFlyoutBase { IsOpen: true } flyout } &&
                flyout.Popup.IsLightDismissEnabled) return true;
            if (control.ContextFlyout is PopupFlyoutBase { IsOpen: true } contextFlyout &&
                contextFlyout.Popup.IsLightDismissEnabled) return true;
            if (control is Popup { IsOpen: true, IsLightDismissEnabled: true }) return true;
        }
        return false;
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

    private static IEnumerable<Control> Controls(Visual root)
    {
        if (!root.IsVisible) yield break;
        if (root is Control control) yield return control;
        foreach (var child in root.GetVisualChildren())
        foreach (var descendant in Controls(child)) yield return descendant;
    }

    private static bool IsRenderedText(TextBlock text) =>
        text.IsEffectivelyVisible && text.Bounds.Width > 0 && text.Bounds.Height > 0 &&
        text.TextLayout.TextLines.Any(line => line.TextRuns.OfType<ShapedTextRun>()
            .Any(run => run.Text.ToString().EnumerateRunes().Any(IsVisibleGlyph))) && HasVisibleOpacity(text);

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
        var bounds = ExtentBounds(control, content);
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
        ControlAutomationPeer.FromElement(control)?.GetName() ?? AutomationProperties.GetName(control),
        string.Join('.', control.Classes), NearestNamedAncestor(control));

    private static string? NearestNamedAncestor(Control control) => control.GetVisualAncestors()
        .OfType<Control>().Select(ancestor => ancestor.Name ?? AutomationProperties.GetAutomationId(ancestor))
        .FirstOrDefault(name => !string.IsNullOrEmpty(name) && !name.StartsWith("PART_", StringComparison.Ordinal));

    internal static IReadOnlyList<LayoutFinding> CollapseDuplicates(IEnumerable<LayoutFinding> findings) =>
        findings.GroupBy(finding => finding.Key)
            .Select(group => group.First() with { Count = group.Sum(finding => finding.Count) })
            .ToArray();
}
