using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
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
/// a word card's links and opinion, a finding's FieldWorks link, the collapsed sidebar, the Matrix's pending mark and
/// the Timing page at both widths and in both themes. Each tooltip opens under the pointer, as a person meets it.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class TooltipPlacementTests
{
    private static readonly string[] Owners =
    [
        "refresh", "drag all files", "question to copy", "Apply to FieldWorks project", "ticked words to AI Handoff",
        "Parse stopped words again", "FieldWorks column heading", "PanGloss column heading",
        "word strip", "disapproved mark on a strip", "staged change", "opinion on a word card", "FieldWorks link on a morpheme",
        "FieldWorks link in a finding", "collapsed sidebar entry", "pending change in a Matrix cell", "WORDS column",
        "completion in detailed statistics",
    ];

    // Reported gaps: no side of these reader owners is clear, so each tip takes the side that covers fewest.
    private static readonly Dictionary<string, string> Gaps = new()
    {
        ["disapproved mark on a strip"] = "at 1040 it sits at the reader's foot, and every side inside the window meets another word strip",
        ["word strip"] = "the filter chips are above, the texts list to the left, and other word strips on every other side",
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
                        foreach (var owner in owners.Where(owner => owner.Scene == scene))
                        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                        {
                            Application.Current!.RequestedThemeVariant = theme;
                            PageScreenshots.Settle(scenes.Window);
                            var where = $"{width} {theme} {owner.Key}";
                            var control = scenes.RealizedOwners(failures, scene).Where(found => found.Owner == owner)
                                .Select(found => found.Control).FirstOrDefault();
                            if (control is null)
                            {
                                failures.Add($"{where}: the {scene} scene showed no such owner");
                                continue;
                            }
                            if (await Hover(scenes, control) is not { } tip)
                            {
                                var blocked = string.Join(", ", control.GetVisualAncestors().OfType<Control>()
                                    .Where(ancestor => !ancestor.IsHitTestVisible)
                                    .Select(ancestor => ancestor.GetType().Name));
                                var underPointer = string.Join(", ", scenes.Window.GetVisualDescendants().OfType<Control>()
                                    .Where(candidate => candidate.IsPointerOver)
                                    .Select(candidate => $"{candidate.GetType().Name} '{NameOf(candidate)}'"));
                                failures.Add($"{where}: the tooltip did not open under the pointer at {Area(control, scenes.Window)}; " +
                                    $"owner hit testing is {control.IsHitTestVisible}; pointer is over: {underPointer}; " +
                                    $"blocked ancestors: {blocked}; tip: {ToolTip.GetTip(control)}");
                                continue;
                            }
                            placed++;
                            var covered = Covered(scenes.Window, control, tip).ToList();
                            if (covered.Count > 0 && Gaps.ContainsKey(owner.Key)) gapsSeen.Add(owner.Key);
                            else failures.AddRange(covered.Select(name => $"{where}: covers {name}"));
                            if (!new Rect(scenes.Window.Bounds.Size).Contains(Area(tip, scenes.Window)))
                                failures.Add($"{where}: leaves the window at {Area(tip, scenes.Window)}");
                            ToolTip.SetIsOpen(control, false);
                            control.ClearValue(ToolTip.ShowDelayProperty);
                        }
                        Application.Current!.RequestedThemeVariant = prior;
                        scenes.Leave(scene);
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
        var point = owner.Classes.Contains("wordRowHeading")
            ? new Point(Math.Min(4, owner.Bounds.Width / 2), owner.Bounds.Height / 2)
            : new Point(owner.Bounds.Width / 2, owner.Bounds.Height / 2);
        window.MouseMove(owner.TranslatePoint(point, window)!.Value);
        PageScreenshots.Settle(window);
        await Task.Yield();
        PageScreenshots.Settle(window);
        return ToolTip.GetIsOpen(owner) ? scenes.Visible<ToolTip>().SingleOrDefault() : null;
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
            if (shown.Width > 0 && shown.Height > 0 && shown.Intersects(area))
                yield return NameOf(other);
        }
    }

    private static string NameOf(Control control) =>
        AutomationProperties.GetName(control) is { Length: > 0 } name ? name
        : AutomationProperties.GetAutomationId(control) is { Length: > 0 } id ? id
        : control.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).FirstOrDefault(text => !string.IsNullOrEmpty(text))
            is { } words ? $"{control.GetType().Name} '{words}'"
        : control.GetType().Name;

    private static bool IsInteractive(Control control) => control is Button or TextBox or ComboBox or NumericUpDown or
        ListBoxItem or TreeViewItem or MenuItem or Slider or MatrixCell || control is Border { Focusable: true };

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
