using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App.Views;
using SIL.Motif.Tests.App.ControlContracts;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins that a tooltip opens where it hides no other control a person could use and stays inside the window. The test
/// opens the top bar's Refresh, the selected list's column headings, Compare's rerun, AI Handoff's drag and question
/// tips, a disabled Apply and AI Handoff with their reasons, the word strips and their marks,
/// a word card's links, opinion and disabled unread reason, a finding's FieldWorks link, the collapsed sidebar, the
/// Matrix's pending mark and the Timing page at both widths and in both themes. Each tip opens under the pointer.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class TooltipPlacementTests
{
    private static readonly string[] Owners =
    [
        "refresh", "project name", "word cell", "drag all files", "question to copy", "Apply to FieldWorks project", "ticked words to AI Handoff",
        "Parse stopped words again", "FieldWorks column heading", "PanGloss column heading",
        "word strip", "disapproved mark on a strip", "staged change", "opinion on a word card",
        "mark unread without a text occurrence", "FieldWorks link on a morpheme",
        "FieldWorks link in a finding", "collapsed sidebar entry", "pending change in a Matrix cell", "WORDS column",
        "completion in detailed statistics",
        "statistics object name", "statistics word", "statistics heat value",
    ];

    // Reported gaps: no side of these reader owners is clear, so each tip takes the side that covers fewest.
    private static readonly Dictionary<string, string> Gaps = new()
    {
        ["disapproved mark on a strip"] = "in the Letter, the tip overlaps the adjacent anapenda word strip",
    };

    [Fact]
    public void ATooltipClearsOtherInteractiveControlsAndItsViewport()
    {
        var failures = new List<string>();
        var gapsSeen = new HashSet<string>(StringComparer.Ordinal);
        var placed = 0;
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var scenes = await TooltipScenes.Open();
            var prior = Application.Current!.RequestedThemeVariant;
            var owners = TooltipOwners.All.Where(owner => Owners.Contains(owner.Key)).ToList();
            Assert.Equal(Owners.Length, owners.Count);
            try
            {
                foreach (var width in new[] { 1040, 1240 })
                {
                    scenes.Width = width;
                    foreach (var scene in owners.Select(owner => owner.Scene).Distinct().Order())
                    {
                        await scenes.Reach(scene);
                        PageScreenshots.Settle(scenes.Window);
                        foreach (var owner in owners.Where(owner => owner.Scene == scene))
                        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                        {
                            Application.Current!.RequestedThemeVariant = theme;
                            PageScreenshots.Settle(scenes.Window);
                            var where = $"{width} {theme} {owner.Key}";
                            if (scene == TooltipScene.Matrix)
                            {
                                var row = Assert.IsType<WordRow>(scenes.MatrixTargetRow);
                                var rowArea = Area(row, scenes.Window);
                                var shown = Shown(row, scenes.Window);
                                Assert.True(row.IsEffectivelyVisible &&
                                    new Rect(scenes.Window.ClientSize).Contains(rowArea) && shown.Contains(rowArea),
                                    $"{where}: the word row '{row.Row?.Word}' is not fully visible after the Assessment finished: " +
                                    $"{MatrixFailureDetails(scenes)}, visible part {shown}.");
                            }
                            var control = scenes.RealizedOwners(failures, scene).Where(found => found.Owner == owner)
                                .Select(found => found.Control).FirstOrDefault();
                            if (control is null)
                            {
                                failures.Add($"{where}: the {scene} scene showed no such owner");
                                continue;
                            }
                            if (owner.Key is "opinion on a word card" or "statistics object name" or "statistics word" or
                                "statistics heat value")
                                Assert.True(ClearTipPlacement.GetIsEnabled(control),
                                    $"{where}: the tooltip must use measured clear placement.");
                            if (await Hover(scenes, control) is not { } tip)
                            {
                                var blocked = string.Join(", ", control.GetVisualAncestors().OfType<Control>()
                                    .Where(ancestor => !ancestor.IsHitTestVisible)
                                    .Select(ancestor => ancestor.GetType().Name));
                                var underPointer = string.Join(", ", scenes.Window.GetVisualDescendants().OfType<Control>()
                                    .Where(candidate => candidate.IsPointerOver)
                                    .Select(candidate => $"{candidate.GetType().Name} '{NameOf(candidate)}'"));
                                var matrixDetails = scene == TooltipScene.Matrix ? MatrixFailureDetails(scenes) + "; " : string.Empty;
                                failures.Add($"{where}: the tooltip did not open under the pointer at {Area(control, scenes.Window)}; " +
                                    matrixDetails +
                                    $"owner hit testing is {control.IsHitTestVisible}; pointer is over: {underPointer}; " +
                                    $"blocked ancestors: {blocked}; tip: {ToolTip.GetTip(control)}");
                                continue;
                            }
                            placed++;
                            if (owner.Key == "Apply to FieldWorks project")
                                Assert.True(tip.MaxWidth <= 280,
                                    $"{where}: the Apply tooltip must stay within the Review side rail.");
                            var covered = Covered(scenes.Window, control, tip).ToList();
                            if (covered.Count > 0 && Gaps.ContainsKey(owner.Key)) gapsSeen.Add(owner.Key);
                            else failures.AddRange(covered.Select(name =>
                                $"{where}: covers {name}; {PlacementDetails(scenes.Window, control, tip)}"));
                            if (!new Rect(scenes.Window.Bounds.Size).Contains(Area(tip, scenes.Window)))
                                failures.Add($"{where}: leaves the window at {Area(tip, scenes.Window)}");
                            ToolTip.SetIsOpen(control, false);
                            control.ClearValue(ToolTip.ShowDelayProperty);
                        }
                        Application.Current!.RequestedThemeVariant = prior;
                        await scenes.Leave(scene);
                    }
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = prior;
                scenes.Close();
            }
        }, TimeSpan.FromMinutes(3));

        failures.AddRange(Gaps.Where(gap => !gapsSeen.Contains(gap.Key))
            .Select(gap => $"{gap.Key}: covers nothing now, so remove its reported gap ({gap.Value})"));
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Distinct()));
        Assert.Equal(2 * 2 * Owners.Length, placed);
    }

    [Fact]
    public void ACompletedAssessmentLeavesTheFirstMatrixWordFormHitTestableAtBothWidths()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var scenes = await TooltipScenes.Open();
            try
            {
                var wordForm = TooltipOwners.All.Single(owner => owner.Key == "word cell");
                foreach (var width in new[] { 1040, 1240 })
                {
                    scenes.Width = width;
                    await scenes.Reach(TooltipScene.Matrix);
                    PageScreenshots.Settle(scenes.Window);

                    var row = Assert.IsType<WordRow>(scenes.MatrixTargetRow);
                    var owner = row.GetVisualDescendants().OfType<Control>().Single(wordForm.Is);
                    PageScreenshots.Settle(scenes.Window);
                    var ownerBounds = Area(owner, scenes.Window);
                    var point = new Point(ownerBounds.X + Math.Min(1, ownerBounds.Width / 2),
                        ownerBounds.Y + ownerBounds.Height / 2);
                    var hit = scenes.Window.InputHitTest(point) as Control;
                    var reachesRow = hit?.GetSelfAndVisualAncestors().Contains(row) == true;

                    var band = scenes.ParseProgressBand;
                    Assert.False(band.IsVisible, $"{width} px after the Assessment completed: the parse-progress band is visible.");
                    Assert.False(band.IsHitTestVisible,
                        $"{width} px after the Assessment completed: the hidden parse-progress band still accepts input.");
                    Assert.Equal(0, band.Bounds.Height);

                    Assert.True(reachesRow,
                        $"{width} px after the Assessment completed: the first Matrix word form does not hit its row; " +
                        $"row {Area(row, scenes.Window)}, word form {ownerBounds}, " +
                        $"parse-progress band visible {scenes.ParseProgressBandIsVisible}; {DescribeHit(scenes.Window, point)}");
                }
            }
            finally
            {
                scenes.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }

    private static async Task<ToolTip?> Hover(TooltipScenes scenes, Control owner)
    {
        var window = scenes.Window;
        owner.BringIntoView();
        PageScreenshots.Settle(window);
        ToolTip.SetShowDelay(owner, 0);
        window.MouseMove(new Point(4, window.Bounds.Height - 4));
        PageScreenshots.Settle(window);
        if (owner.Classes.Contains("warningObjectLink") &&
            owner.GetVisualAncestors().OfType<Border>().FirstOrDefault(border =>
                border.Classes.Contains("warningSubjects")) is { } subjects)
        {
            window.MouseMove(subjects.TranslatePoint(
                new Point(subjects.Bounds.Width / 2, subjects.Bounds.Height / 2), window)!.Value);
            PageScreenshots.Settle(window);
        }
        window.MouseMove(CenterAfterLayout(owner, window));
        PageScreenshots.Settle(window);
        await Task.Yield();
        PageScreenshots.Settle(window);
        if (!ToolTip.GetIsOpen(owner)) return null;
        var tip = scenes.Visible<ToolTip>().SingleOrDefault();
        if (tip is not null) PageScreenshots.Settle(window);
        return tip;
    }

    private static Point CenterAfterLayout(Control owner, Window window)
    {
        PageScreenshots.Settle(window);
        var localPoint = owner.Classes.Contains("wordRowHeading")
            ? new Point(Math.Min(4, owner.Bounds.Width / 2), owner.Bounds.Height / 2)
            : new Point(owner.Bounds.Width / 2, owner.Bounds.Height / 2);
        if (owner.FindAncestorOfType<WordRow>() is { } row && owner.TranslatePoint(default, row) is { } inRow)
        {
            var pointInRow = new Point(inRow.X + localPoint.X, inRow.Y + localPoint.Y);
            return row.TranslatePoint(pointInRow, window) ?? default;
        }

        return owner.TranslatePoint(localPoint, window) ?? default;
    }

    private static string MatrixFailureDetails(TooltipScenes scenes)
    {
        var window = scenes.Window;
        var row = scenes.MatrixTargetRow;
        if (row is null) return $"window client size {window.ClientSize}; target row is missing";

        var rowBounds = Area(row, window);
        var rowCenter = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window) ?? default;
        var hit = window.InputHitTest(rowCenter) as Visual;
        var hitChain = hit is null
            ? "<none>"
            : string.Join(" -> ", hit.GetSelfAndVisualAncestors().OfType<Control>()
                .Select(control => $"{control.GetType().Name} '{NameOf(control)}'"));
        return $"window client size {window.ClientSize}; target row '{row.Row?.Word}' bounds in window coordinates " +
            $"{rowBounds}; parse-progress band visible {scenes.ParseProgressBandIsVisible}; " +
            $"hit-test chain at row centre {rowCenter}: {hitChain}";
    }

    private static string DescribeHit(Window window, Point point)
    {
        var hit = window.InputHitTest(point) as Control;
        if (hit is null) return $"hit at {point}: <none>";

        static string Describe(Control control, Window window) =>
            $"{control.GetType().Name} '{NameOf(control)}' (IsVisible {control.IsVisible}, " +
            $"IsHitTestVisible {control.IsHitTestVisible}, bounds {Area(control, window)})";

        return $"hit at {point}: {Describe(hit, window)}; chain: " +
            string.Join(" -> ", hit.GetSelfAndVisualAncestors().OfType<Control>()
                .Select(control => Describe(control, window)));
    }

    // What a person could press or type in, shown and not hidden by a scroll or a zero opacity.
    private static IEnumerable<string> Covered(Window window, Control owner, ToolTip tip)
    {
        var area = Area(tip, window);
        foreach (var other in window.GetVisualDescendants().OfType<Control>().Where(IsInteractive))
        {
            if (other == owner || !other.IsEffectivelyVisible || other.GetVisualAncestors().Contains(owner) ||
                owner.GetVisualAncestors().Contains(other) || other.GetVisualAncestors().Contains(tip)) continue;
            if (other.GetSelfAndVisualAncestors().OfType<Visual>().Any(visual => visual.Opacity == 0)) continue;
            var shown = Shown(other, window);
            if (shown.Width > 0 && shown.Height > 0 && Overlaps(shown, area))
                yield return $"{NameOf(other)} ({other.GetType().Name}) at {shown} overlaps tooltip at {area}";
        }
    }

    private static string PlacementDetails(Window window, Control owner, ToolTip tip)
    {
        var ownerArea = Area(owner, window);
        var obstacles = window.GetVisualDescendants().OfType<Control>()
            .Where(other => other != owner && IsInteractive(other) && other.IsEffectivelyVisible &&
                !other.GetVisualAncestors().Contains(owner) && !owner.GetVisualAncestors().Contains(other) &&
                !other.GetSelfAndVisualAncestors().OfType<Visual>().Any(visual => visual is ToolTip || visual.Opacity == 0))
            .Select(other => Shown(other, window))
            .Where(shown => shown.Width > 0 && shown.Height > 0)
            .ToList();
        var placement = ClearTipPlacement.Choose(ownerArea, tip.Bounds.Size, new Rect(window.Bounds.Size), obstacles);
        var planned = ClearTipPlacement.Area(ownerArea, tip.Bounds.Size, placement.Anchor, placement.Gravity);
        return $"owner at {ownerArea}; planned {placement} at {planned}; popup {tip.Bounds.Size}";
    }

    private static bool Overlaps(Rect first, Rect second) =>
        first.Left < second.Right && first.Right > second.Left &&
        first.Top < second.Bottom && first.Bottom > second.Top;

    private static string NameOf(Control control) =>
        AutomationProperties.GetName(control) is { Length: > 0 } name ? name
        : AutomationProperties.GetAutomationId(control) is { Length: > 0 } id ? id
        : control.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).FirstOrDefault(text => !string.IsNullOrEmpty(text))
            is { } words ? $"{control.GetType().Name} '{words}'"
        : control.GetType().Name;

    private static bool IsInteractive(Control control) => control is Button or SplitButton or TextBox or ComboBox or
        NumericUpDown or ListBoxItem or TreeViewItem or MenuItem or Slider or ScrollBar or MatrixCell ||
        control is Border { Focusable: true };

    // The part of a control left after every clipping ancestor, such as a scrolled list, has cut it.
    private static Rect Shown(Control control, Window window)
    {
        var shown = Area(control, window);
        foreach (var ancestor in control.GetVisualAncestors().OfType<Visual>().Where(visual => visual.ClipToBounds && visual != window))
            shown = shown.Intersect(Area(ancestor, window));
        return shown;
    }

    private static Rect Area(Visual visual, Window window) =>
        new(visual.TranslatePoint(default, window) ?? default, visual.Bounds.Size);
}
