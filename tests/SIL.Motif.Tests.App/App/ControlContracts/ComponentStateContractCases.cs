using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace SIL.Motif.Tests.App.ControlContracts;

/// <summary>How a state case reaches its state: through the pointer, the keyboard, or the control's own availability.</summary>
[Flags]
internal enum StateStimulus
{
    None = 0,
    Pointer = 1,
    Press = 2,
    KeyboardFocus = 4,
    KeyboardFocusInside = 8,
    Disabled = 16,
}

/// <summary>Where a state shows: on the control itself, on its template's face, on its focus ring, or on text inside it.</summary>
internal enum StatePart
{
    Self,
    Face,
    Ring,
    Text,
}

/// <summary>
/// One interaction state of one component: the selector that styles it, the state it reaches, how it reaches it, and
/// the Intent token a person should see on the named part. <see cref="Build"/> returns the window's content and the
/// control the stimulus lands on; selection and opening are part of what it builds, since they are model state.
/// </summary>
internal sealed record ComponentStateCase(
    string Selector,
    string State,
    Func<(Control Content, Control Target)> Build,
    StateStimulus Stimulus,
    StatePart Part,
    AvaloniaProperty Property,
    string Key)
{
    public override string ToString() => $"{Selector} [{State}] {Part}.{Property.Name}";
}

/// <summary>
/// The authored table of interaction states the window's component styles declare, each with the token its state must
/// show in both themes, plus the selectors whose states another test already pins. Every interaction selector in a
/// component style must appear here once per comma-separated alternative, so a new state cannot go unchecked.
/// </summary>
internal static class ComponentStateContractCases
{
    /// <summary>A selector whose state another test pins, named so the completeness check can account for it.</summary>
    internal sealed record PinnedElsewhere(string Selector, string Test);

    internal static IReadOnlyList<PinnedElsewhere> Pinned { get; } =
    [
        new("HyperlinkButton:pointerover", nameof(InteractionCueTests.ALinkUnderlinesUnderThePointer)),
        new("Border.matrixCell.violation:pointerover", nameof(InteractionCueTests.AMeaningCellKeepsItsEdgeUnderThePointerAndWhenChosen)),
        new("Border.matrixCell.review:pointerover", nameof(InteractionCueTests.AMeaningCellKeepsItsEdgeUnderThePointerAndWhenChosen)),
        new("Border.matrixCell.new:pointerover", nameof(InteractionCueTests.AMeaningCellKeepsItsEdgeUnderThePointerAndWhenChosen)),
    ];

    /// <summary>
    /// A state the window does not yet paint with its token, with the reason, reported to the owner of the tokens.
    /// The state test expects each to fail in at least one theme, so a fixed gap must leave this list.
    /// </summary>
    internal sealed record ReportedGap(string Case, string Reason);

    internal static IReadOnlyList<ReportedGap> Gaps { get; } =
    [
        new("Button.actionChip:pointerover [hover] Face.Background", SemiHoverWins),
        new("Button.overviewTile:pointerover [hover] Face.Background", SemiHoverWins),
        new("Button.timingRuleRow:pointerover [hover] Face.Background", SemiHoverWins),
        new("Button.filterChip.active [selected under the pointer] Face.Background", SemiHoverWins),
        new("Button.findingGroup.chosen [selected under the pointer] Face.Background", SemiHoverWins),
        new("Button.timingRuleRow.chosen [selected under the pointer] Face.Background", SemiHoverWins),
        new("Button.stopGroup.chosen [selected under the pointer] Face.Background", SemiHoverWins),
        new("Button.tab.active [selected under the pointer] Face.BorderBrush", SemiHoverWins),
    ];

    private const string SemiHoverWins =
        "Semi's pointer-over brush on the button face outranks the brush the component style sets on the Button";

    internal static IEnumerable<ComponentStateCase> All()
    {
        const string actionHover = "Button.actionChip:pointerover";
        yield return new(actionHover, "hover", () => Alone(Press("actionChip")), StateStimulus.Pointer,
            StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Marking.Hover");
        yield return new(actionHover, "focus", () => Alone(Press("actionChip")), StateStimulus.KeyboardFocus,
            StatePart.Ring, Border.BorderBrushProperty, "Intent.Focus");

        const string chipDisabled = "Button.filterChip:disabled /template/ ContentPresenter#PART_ContentPresenter";
        yield return new(chipDisabled, "disabled", () => Alone(Press("filterChip")), StateStimulus.Disabled,
            StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Clear");
        yield return new(chipDisabled, "disabled", () => Alone(Press("filterChip")), StateStimulus.Disabled,
            StatePart.Face, ContentPresenter.BorderBrushProperty, "Intent.Border");
        yield return new(chipDisabled, "disabled", () => Alone(Press("filterChip")), StateStimulus.Disabled,
            StatePart.Face, ContentPresenter.ForegroundProperty, "Intent.TextMuted");

        const string chipActive = "Button.filterChip.active";
        yield return new(chipActive, "selected", () => Alone(Press("filterChip", "active")), StateStimulus.None,
            StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Primary.Fill");
        yield return new(chipActive, "selected", () => Alone(Press("filterChip", "active")), StateStimulus.None,
            StatePart.Face, ContentPresenter.BorderBrushProperty, "Intent.Primary");
        yield return new(chipActive, "selected", () => Alone(Press("filterChip", "active")), StateStimulus.None,
            StatePart.Face, ContentPresenter.ForegroundProperty, "Intent.Primary");
        yield return new(chipActive, "selected and keyboard focus", () => Alone(Press("filterChip", "active")),
            StateStimulus.KeyboardFocus, StatePart.Ring, Border.BorderBrushProperty, "Intent.Focus");
        yield return new(chipActive, "selected under the pointer", () => Alone(Press("filterChip", "active")),
            StateStimulus.Pointer, StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Primary.Fill");
        yield return new(chipActive, "selected and keyboard focus", () => Alone(Press("filterChip", "active")),
            StateStimulus.KeyboardFocus, StatePart.Face, ContentPresenter.BorderBrushProperty, "Intent.Primary");
        yield return new(chipActive, "selected and pressed", () => Alone(Press("filterChip", "active")),
            StateStimulus.Press, StatePart.Face, Visual.RenderTransformProperty, "Intent.Transform.Pressed");
        yield return new("Button.filterChip.active TextBlock.chipCount", "selected", ChipWithCount,
            StateStimulus.None, StatePart.Text, TextBlock.ForegroundProperty, "Intent.Primary");

        const string revealFocus = ":is(Button).revealControl:focus-visible";
        yield return new(revealFocus, "keyboard focus", () => Alone(Press("revealControl", "revealButton")),
            StateStimulus.KeyboardFocus, StatePart.Self, Button.BackgroundProperty, "Intent.Surface.Hover");
        yield return new(revealFocus, "keyboard focus", () => Alone(Press("revealControl", "revealButton")),
            StateStimulus.KeyboardFocus, StatePart.Self, Button.BorderBrushProperty, "Intent.Focus");
        yield return new(revealFocus, "keyboard focus on a FieldWorks link", () => Alone(Press("revealControl", "revealLink")),
            StateStimulus.KeyboardFocus, StatePart.Self, Button.BorderBrushProperty, "Intent.Focus");
        foreach (var (owner, how, stimulus) in new[]
        {
            ("hoverReveal", "pointerover", StateStimulus.Pointer),
            ("hoverReveal", "focus-within", StateStimulus.KeyboardFocusInside),
            ("stagedStrip", "pointerover", StateStimulus.Pointer),
            ("stagedStrip", "focus-within", StateStimulus.KeyboardFocusInside),
        })
            yield return new($"Border.{owner}:{how} :is(Button).revealControl", $"revealed by {how}", () => RevealOwner(owner),
                stimulus, StatePart.Self, Visual.OpacityProperty, "Component.HoverReveal.VisibleOpacity");

        yield return new("Button:pressed /template/ ContentPresenter#PART_ContentPresenter", "pressed",
            () => Alone(Press("timingRuleRow")), StateStimulus.Press, StatePart.Face, Visual.RenderTransformProperty,
            "Intent.Transform.Pressed");

        yield return new("Border.matrixCell:pointerover", "hover", () => Alone(Cell()), StateStimulus.Pointer,
            StatePart.Self, Border.BorderBrushProperty, "Intent.Primary");
        yield return new("Border.matrixCell:pointerover", "hover", () => Alone(Cell()), StateStimulus.Pointer,
            StatePart.Self, Border.BoxShadowProperty, "Intent.Shadow.Hover");
        yield return new("Border.matrixCell:pointerover", "keyboard focus", () => Alone(Cell()), StateStimulus.KeyboardFocus,
            StatePart.Ring, Border.BorderBrushProperty, "Intent.Focus");
        yield return new("Border.matrixCell.selected", "selected", () => Alone(Cell("selected")), StateStimulus.None,
            StatePart.Self, Border.BoxShadowProperty, "Intent.Shadow.Selected");
        yield return new("Border.matrixCell.selected", "selected under the pointer", () => Alone(Cell("selected")),
            StateStimulus.Pointer, StatePart.Self, Border.BoxShadowProperty, "Intent.Shadow.Selected");
        yield return new("Border.matrixCell.selected", "selected and keyboard focus", () => Alone(Cell("selected")),
            StateStimulus.KeyboardFocus, StatePart.Self, Border.BoxShadowProperty, "Intent.Shadow.Selected");

        yield return new("Button.overviewTile:pointerover", "hover", () => Alone(Press("overviewTile")), StateStimulus.Pointer,
            StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Surface.Hover");

        yield return new("Button.viewChip.active", "selected", () => Alone(Press("viewChip", "active")), StateStimulus.None,
            StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Surface.Neutral");
        yield return new("Button.viewChip.active", "selected under the pointer", () => Alone(Press("viewChip", "active")),
            StateStimulus.Pointer, StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Surface.Neutral");
        yield return new("Button.viewChip.active", "selected and keyboard focus", () => Alone(Press("viewChip", "active")),
            StateStimulus.KeyboardFocus, StatePart.Ring, Border.BorderBrushProperty, "Intent.Focus");

        const string sidebarFace = " /template/ ContentPresenter#PART_ContentPresenter";
        yield return new("ListBox.sidebar ListBoxItem:pointerover" + sidebarFace, "hover", () => Entry(collapsed: false, selected: false),
            StateStimulus.Pointer, StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Surface.Hover");
        yield return new("ListBox.sidebar ListBoxItem:selected" + sidebarFace, "selected", () => Entry(collapsed: false, selected: true),
            StateStimulus.None, StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Selected.Fill");
        yield return new("ListBox.sidebar ListBoxItem:selected" + sidebarFace, "selected", () => Entry(collapsed: false, selected: true),
            StateStimulus.None, StatePart.Face, ContentPresenter.ForegroundProperty, "Intent.Accent");
        yield return new("ListBox.sidebar ListBoxItem:selected" + sidebarFace, "selected under the pointer",
            () => Entry(collapsed: false, selected: true), StateStimulus.Pointer, StatePart.Face,
            ContentPresenter.BackgroundProperty, "Intent.Selected.Fill");
        yield return new("ListBox.sidebar ListBoxItem:selected", "selected", () => Entry(collapsed: false, selected: true),
            StateStimulus.None, StatePart.Self, ListBoxItem.ForegroundProperty, "Intent.Accent");
        yield return new("ListBox.sidebar ListBoxItem:selected", "selected and keyboard focus", () => Entry(collapsed: false, selected: true),
            StateStimulus.KeyboardFocus, StatePart.Ring, Border.BorderBrushProperty, "Intent.Focus");
        yield return new("ListBox.sidebar.collapsed ListBoxItem:selected", "collapsed and selected", () => Entry(collapsed: true, selected: true),
            StateStimulus.None, StatePart.Self, ListBoxItem.BorderBrushProperty, "Intent.Emphasis.Fill");
        yield return new("ListBox.sidebar.collapsed ListBoxItem:selected" + sidebarFace, "collapsed and selected",
            () => Entry(collapsed: true, selected: true), StateStimulus.None, StatePart.Face,
            ContentPresenter.BackgroundProperty, "Intent.Clear");
        yield return new("ListBox.sidebar.collapsed ListBoxItem:selected" + sidebarFace, "collapsed, selected and keyboard focus",
            () => Entry(collapsed: true, selected: true), StateStimulus.KeyboardFocus, StatePart.Ring,
            Border.BorderBrushProperty, "Intent.Focus");

        yield return new("Button.tab.active", "selected", () => Alone(Press("tab", "active")), StateStimulus.None,
            StatePart.Face, ContentPresenter.ForegroundProperty, "Intent.Accent");
        yield return new("Button.tab.active", "selected", () => Alone(Press("tab", "active")), StateStimulus.None,
            StatePart.Face, ContentPresenter.BorderBrushProperty, "Intent.Accent");
        yield return new("Button.tab.active", "selected under the pointer", () => Alone(Press("tab", "active")),
            StateStimulus.Pointer, StatePart.Face, ContentPresenter.BorderBrushProperty, "Intent.Accent");
        yield return new("Button.tab.active", "selected and keyboard focus", () => Alone(Press("tab", "active")),
            StateStimulus.KeyboardFocus, StatePart.Ring, Border.BorderBrushProperty, "Intent.Focus");

        yield return new("Button.timingRuleRow:pointerover", "hover", () => Alone(Press("timingRuleRow")), StateStimulus.Pointer,
            StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Surface.Hover");
        yield return new("Button.timingRuleRow.chosen", "selected", () => Alone(Press("timingRuleRow", "chosen")), StateStimulus.None,
            StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Selected.Fill");
        yield return new("Button.timingRuleRow.chosen", "selected under the pointer", () => Alone(Press("timingRuleRow", "chosen")),
            StateStimulus.Pointer, StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Selected.Fill");
        yield return new("Button.timingRuleRow.chosen", "selected and keyboard focus", () => Alone(Press("timingRuleRow", "chosen")),
            StateStimulus.KeyboardFocus, StatePart.Ring, Border.BorderBrushProperty, "Intent.Focus");

        yield return new("ToolTip:open :is(TextBlock)", "open", OpenTip, StateStimulus.None, StatePart.Self,
            TextBlock.ForegroundProperty, "Intent.Tooltip.Text");
        yield return new("ToolTip:open :is(TextBlock)", "open", OpenTip, StateStimulus.None, StatePart.Self,
            TextBlock.FontSizeProperty, "Intent.Type.Small");

        yield return new("Button.stopGroup.chosen", "selected", () => Alone(Press("stopGroup", "chosen")), StateStimulus.None,
            StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Danger.Fill");
        yield return new("Button.stopGroup.chosen", "selected under the pointer", () => Alone(Press("stopGroup", "chosen")),
            StateStimulus.Pointer, StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Danger.Fill");
        yield return new("Button.findingGroup.chosen", "selected", () => Alone(Press("findingGroup", "chosen")), StateStimulus.None,
            StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Selected.Fill");
        yield return new("Button.findingGroup.chosen", "selected under the pointer", () => Alone(Press("findingGroup", "chosen")),
            StateStimulus.Pointer, StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Selected.Fill");
        yield return new("Button.findingGroup.chosen", "selected and keyboard focus", () => Alone(Press("findingGroup", "chosen")),
            StateStimulus.KeyboardFocus, StatePart.Ring, Border.BorderBrushProperty, "Intent.Focus");

        yield return new("Border.wordStrip:pointerover", "hover", () => Alone(Strip()), StateStimulus.Pointer,
            StatePart.Self, Border.BackgroundProperty, "Intent.Surface.Hover");
        yield return new("Border.wordStrip:pointerover", "hover", () => Alone(Strip()), StateStimulus.Pointer,
            StatePart.Self, Border.BorderBrushProperty, "Intent.Border");
        yield return new("Border.wordStrip.open", "open", () => Alone(Strip("open")), StateStimulus.None,
            StatePart.Self, Border.BackgroundProperty, "Intent.Selected.Fill");
        yield return new("Border.wordStrip.open", "open under the pointer", () => Alone(Strip("open")), StateStimulus.Pointer,
            StatePart.Self, Border.BorderBrushProperty, "Intent.Accent");
        yield return new("Border.wordStrip.open", "open and keyboard focus", () => Alone(Strip("open")), StateStimulus.KeyboardFocus,
            StatePart.Ring, Border.BorderBrushProperty, "Intent.Focus");
    }

    /// <summary>Reads the named part of <paramref name="target"/> once its state is reached.</summary>
    internal static AvaloniaObject? PartOf(Control target, StatePart part) => part switch
    {
        StatePart.Self => target,
        StatePart.Face => target.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault(face => face.Name == "PART_ContentPresenter"),
        StatePart.Ring => RingAround(target),
        StatePart.Text => target is TextBlock ? target : target.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(),
        _ => null,
    };

    internal static Border? RingAround(Visual target)
    {
        var layer = AdornerLayer.GetAdornerLayer(target);
        return layer?.Children.OfType<Control>()
            .Where(adorner => ReferenceEquals(AdornerLayer.GetAdornedElement(adorner), target))
            .Select(adorner => adorner as Border ?? adorner.GetVisualDescendants().OfType<Border>().FirstOrDefault())
            .FirstOrDefault(ring => ring is not null);
    }

    private static (Control, Control) Alone(Control control) => (control, control);

    private static Button Press(params string[] classes)
    {
        var button = new Button { Content = "Go" };
        button.Classes.AddRange(classes);
        return button;
    }

    private static Border Cell(params string[] classes)
    {
        var cell = new Border { Classes = { "matrixCell" }, Focusable = true, Width = 160, Height = 60, Child = new TextBlock { Text = "5" } };
        cell.Classes.AddRange(classes);
        return cell;
    }

    private static Border Strip(params string[] classes)
    {
        var strip = new Border { Classes = { "wordStrip" }, Focusable = true, Child = new TextBlock { Text = "Sungura" } };
        strip.Classes.AddRange(classes);
        return strip;
    }

    private static (Control, Control) ChipWithCount()
    {
        var count = new TextBlock { Classes = { "chipCount" }, Text = "12" };
        var chip = Press("filterChip", "active");
        chip.Content = count;
        return (chip, count);
    }

    // The revealed button is the target read; the owner border is where the pointer or the keyboard goes.
    private static (Control, Control) RevealOwner(string ownerClass)
    {
        var button = Press("revealControl", "revealButton");
        // Every owner in the window paints a fill, which is what makes the gap around its button take the pointer.
        var owner = new Border { Classes = { ownerClass }, Background = Brushes.Transparent, Child = button, Width = 200, Height = 40 };
        return (owner, button);
    }

    private static (Control, Control) Entry(bool collapsed, bool selected)
    {
        var list = new ListBox { Classes = { "sidebar" } };
        if (collapsed) list.Classes.Add("collapsed");
        var item = new ListBoxItem { Content = "Overview" };
        list.Items.Add(item);
        if (selected) list.SelectedIndex = 0;
        return (list, item);
    }

    private static (Control, Control) OpenTip()
    {
        var words = new TextBlock { Text = "Still fits the project." };
        var tip = new ToolTip { Content = words };
        ((IPseudoClasses)tip.Classes).Set(":open", true);
        return (new Border { Classes = { "stagedStrip" }, Child = tip }, words);
    }
}
