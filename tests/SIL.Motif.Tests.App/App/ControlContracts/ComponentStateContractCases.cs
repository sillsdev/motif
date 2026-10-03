using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;

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
        new("Border.warningRow:pointerover :is(Control).warningHoverTitle", nameof(WarningsPageWordsTests.ARowOpensPanGlossGuidanceAndShowsItsFieldWorksLink)),
        new("Border.warningRow:focus-within :is(Control).warningHoverTitle", nameof(WarningsPageWordsTests.ARowOpensPanGlossGuidanceAndShowsItsFieldWorksLink)),
        new("Border.wordRowBody:pointerover Grid.wordRowNextHost", nameof(WordRowControlTests.NextStepsShowWhileThePointerIsOverTheRowAndWhileItIsOpen)),
        new("Border.wordRowBody:focus-within Grid.wordRowNextHost", nameof(WordRowControlTests.NextStepsStayOutOfTheRestingRowAndAppearWhenItGetsKeyboardFocus)),
        new("Border.wordRowBody.open Grid.wordRowNextHost", nameof(WordRowControlTests.NextStepsShowWhileThePointerIsOverTheRowAndWhileItIsOpen)),
    ];

    /// <summary>
    /// A state the window does not yet paint with its token, with the reason, reported to the owner of the tokens.
    /// The state test expects each to fail in at least one theme, so a fixed gap must leave this list.
    /// </summary>
    internal sealed record ReportedGap(string Case, string Reason);

    internal static IReadOnlyList<ReportedGap> Gaps { get; } = [];

    internal static IEnumerable<ComponentStateCase> All()
    {
        const string face = " /template/ ContentPresenter#PART_ContentPresenter";
        const string actionHover = "Button.actionChip:pointerover" + face;
        const string actionPressed = "Button.actionChip:pressed" + face;
        yield return new(actionHover, "hover", () => Alone(Press("actionChip")), StateStimulus.Pointer,
            StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Marking.Hover");
        yield return new(actionHover, "hover", () => Alone(Press("actionChip")), StateStimulus.Pointer,
            StatePart.Face, ContentPresenter.BorderBrushProperty, "Intent.Marking.Border");
        yield return new(actionHover, "focus", () => Alone(Press("actionChip")), StateStimulus.KeyboardFocus,
            StatePart.Ring, Border.BorderBrushProperty, "Intent.Focus");
        yield return new(actionPressed, "pressed", () => Alone(Press("actionChip")), StateStimulus.Press,
            StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Marking.Hover");
        yield return new(actionPressed, "pressed", () => Alone(Press("actionChip")), StateStimulus.Press,
            StatePart.Face, ContentPresenter.BorderBrushProperty, "Intent.Marking.Border");
        foreach (var (how, stimulus) in Hovers)
            yield return new($"Button.actionChip.primary:{how}" + face, $"leading action {how}",
                () => Alone(Press("actionChip", "primary")), stimulus, StatePart.Face,
                ContentPresenter.BorderBrushProperty, "Intent.Opinion.Approved.Accent");

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
        foreach (var (how, stimulus) in Hovers)
        {
            yield return new($"Button.filterChip:{how}" + face, how, () => Alone(Press("filterChip")), stimulus,
                StatePart.Face, ContentPresenter.BorderBrushProperty, "Intent.Border");
            foreach (var (property, key) in new[]
            {
                (ContentPresenter.BackgroundProperty, "Intent.Primary.Fill"),
                (ContentPresenter.BorderBrushProperty, "Intent.Primary"),
            })
                yield return new($"Button.filterChip.active:{how}" + face, $"selected and {how}",
                    () => Alone(Press("filterChip", "active")), stimulus, StatePart.Face, property, key);
        }
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
        const string revealAtRest = "Border.hoverReveal :is(Button).revealOnHover";
        const string revealOnPointer = "Border.hoverReveal:pointerover :is(Button).revealOnHover";
        const string revealOnFocus = "Border.hoverReveal:focus-within :is(Button).revealOnHover";
        const string revealWhenOpen = "Border.hoverReveal.open :is(Button).revealOnHover";
        yield return new(revealAtRest, "at rest", () => RevealOwner(), StateStimulus.None,
            StatePart.Self, Visual.OpacityProperty, "Component.HoverReveal.HiddenOpacity");
        yield return new(revealOnPointer, "revealed by pointer hover", () => RevealOwner(), StateStimulus.Pointer,
            StatePart.Self, Visual.OpacityProperty, "Component.HoverReveal.VisibleOpacity");
        yield return new(revealOnFocus, "revealed by keyboard focus", () => RevealOwner(), StateStimulus.KeyboardFocusInside,
            StatePart.Self, Visual.OpacityProperty, "Component.HoverReveal.VisibleOpacity");
        yield return new(revealWhenOpen, "revealed when the row is open", () => RevealOwner(open: true), StateStimulus.None,
            StatePart.Self, Visual.OpacityProperty, "Component.HoverReveal.VisibleOpacity");

        const string textRevealAtRest = "Border.hoverReveal :is(TextBlock).revealOnHover";
        const string textRevealOnPointer = "Border.hoverReveal:pointerover :is(TextBlock).revealOnHover";
        const string textRevealOnFocus = "Border.hoverReveal:focus-within :is(TextBlock).revealOnHover";
        const string textRevealWhenOpen = "Border.hoverReveal.open :is(TextBlock).revealOnHover";
        yield return new(textRevealAtRest, "at rest", () => RevealTextOwner(), StateStimulus.None,
            StatePart.Text, Visual.OpacityProperty, "Component.HoverReveal.HiddenOpacity");
        yield return new(textRevealOnPointer, "revealed by pointer hover", () => RevealTextOwner(), StateStimulus.Pointer,
            StatePart.Text, Visual.OpacityProperty, "Component.HoverReveal.VisibleOpacity");
        yield return new(textRevealOnFocus, "revealed by keyboard focus", () => RevealTextOwner(), StateStimulus.KeyboardFocusInside,
            StatePart.Text, Visual.OpacityProperty, "Component.HoverReveal.VisibleOpacity");
        yield return new(textRevealWhenOpen, "revealed when the row is open", () => RevealTextOwner(open: true), StateStimulus.None,
            StatePart.Text, Visual.OpacityProperty, "Component.HoverReveal.VisibleOpacity");

        yield return new("Button:pressed /template/ ContentPresenter#PART_ContentPresenter", "pressed",
            () => Alone(Press("timingRuleRow")), StateStimulus.Press, StatePart.Face, Visual.RenderTransformProperty,
            "Intent.Transform.Pressed");

        yield return new("Border.morph.inspectable:pointerover", "hover", () => Alone(Morpheme()), StateStimulus.Pointer,
            StatePart.Self, Border.BackgroundProperty, "Intent.Surface.Hover");
        yield return new("Border.matrixCell:pointerover", "hover", () => Alone(Cell()), StateStimulus.Pointer,
            StatePart.Self, Border.BorderBrushProperty, "Intent.Primary");
        yield return new("Border.matrixCell:pointerover", "hover", () => Alone(Cell()), StateStimulus.Pointer,
            StatePart.Self, Border.BoxShadowProperty, "Intent.Shadow.Hover");
        yield return new("Border.matrixCell:pointerover", "keyboard focus", () => Alone(Cell()), StateStimulus.KeyboardFocus,
            StatePart.Ring, Border.BorderBrushProperty, "Intent.Focus");
        yield return new("Border.overviewTile:pointerover", "hover", () => Alone(new Border
        {
            Classes = { "overviewTile" }, Width = 200, Height = 80,
            Child = new TextBlock { Text = "Summary" },
        }),
            StateStimulus.Pointer, StatePart.Self, Border.BackgroundProperty, "Intent.Surface.Hover");
        yield return new("Border.matrixCell.selected", "selected", () => Alone(Cell("selected")), StateStimulus.None,
            StatePart.Self, Border.BoxShadowProperty, "Intent.Shadow.Selected");
        yield return new("Border.matrixCell.selected", "selected under the pointer", () => Alone(Cell("selected")),
            StateStimulus.Pointer, StatePart.Self, Border.BoxShadowProperty, "Intent.Shadow.Selected");
        yield return new("Border.matrixCell.selected", "selected and keyboard focus", () => Alone(Cell("selected")),
            StateStimulus.KeyboardFocus, StatePart.Self, Border.BoxShadowProperty, "Intent.Shadow.Selected");

        foreach (var (how, stimulus) in Hovers)
        {
            yield return new($"Button.overviewTile:{how}" + face, how, () => Alone(Press("overviewTile")), stimulus,
                StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Surface.Hover");
            yield return new($"Button.overviewTile:{how}" + face, how, () => Alone(Press("overviewTile")), stimulus,
                StatePart.Face, ContentPresenter.BorderBrushProperty, "Intent.Border");
        }

        const string lookFirstHover = "Button.overviewLookFirstRow:pointerover /template/ ContentPresenter#PART_ContentPresenter";
        yield return new(lookFirstHover, "hover", () => Alone(Press("overviewLookFirstRow")), StateStimulus.Pointer,
            StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Surface.Hover");

        yield return new("Button.viewChip.active", "selected", () => Alone(Press("viewChip", "active")), StateStimulus.None,
            StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Surface.Neutral");
        yield return new("Button.viewChip.active", "selected under the pointer", () => Alone(Press("viewChip", "active")),
            StateStimulus.Pointer, StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Surface.Neutral");
        yield return new("Button.viewChip.active", "selected and keyboard focus", () => Alone(Press("viewChip", "active")),
            StateStimulus.KeyboardFocus, StatePart.Ring, Border.BorderBrushProperty, "Intent.Focus");

        yield return new("ListBox.sidebar ListBoxItem:pointerover" + face, "hover", () => Entry(collapsed: false, selected: false),
            StateStimulus.Pointer, StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Surface.Hover");
        yield return new("ListBox.sidebar ListBoxItem:selected" + face, "selected", () => Entry(collapsed: false, selected: true),
            StateStimulus.None, StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Selected.Fill");
        yield return new("ListBox.sidebar ListBoxItem:selected" + face, "selected", () => Entry(collapsed: false, selected: true),
            StateStimulus.None, StatePart.Face, ContentPresenter.ForegroundProperty, "Intent.Accent");
        yield return new("ListBox.sidebar ListBoxItem:selected" + face, "selected under the pointer",
            () => Entry(collapsed: false, selected: true), StateStimulus.Pointer, StatePart.Face,
            ContentPresenter.BackgroundProperty, "Intent.Selected.Fill");
        yield return new("ListBox.sidebar ListBoxItem:selected", "selected", () => Entry(collapsed: false, selected: true),
            StateStimulus.None, StatePart.Self, ListBoxItem.ForegroundProperty, "Intent.Accent");
        yield return new("ListBox.sidebar ListBoxItem:selected", "selected and keyboard focus", () => Entry(collapsed: false, selected: true),
            StateStimulus.KeyboardFocus, StatePart.Ring, Border.BorderBrushProperty, "Intent.Focus");
        yield return new("ListBox.sidebar.collapsed ListBoxItem:selected", "collapsed and selected", () => Entry(collapsed: true, selected: true),
            StateStimulus.None, StatePart.Self, ListBoxItem.BorderBrushProperty, "Intent.Emphasis.Fill");
        yield return new("ListBox.sidebar.collapsed ListBoxItem:selected" + face, "collapsed and selected",
            () => Entry(collapsed: true, selected: true), StateStimulus.None, StatePart.Face,
            ContentPresenter.BackgroundProperty, "Intent.Clear");
        yield return new("ListBox.sidebar.collapsed ListBoxItem:selected" + face, "collapsed, selected and keyboard focus",
            () => Entry(collapsed: true, selected: true), StateStimulus.KeyboardFocus, StatePart.Ring,
            Border.BorderBrushProperty, "Intent.Focus");

        yield return new("Button.tab.active", "selected", () => Alone(Press("tab", "active")), StateStimulus.None,
            StatePart.Face, ContentPresenter.ForegroundProperty, "Intent.Accent");
        yield return new("Button.tab.active", "selected", () => Alone(Press("tab", "active")), StateStimulus.None,
            StatePart.Face, ContentPresenter.BorderBrushProperty, "Intent.Accent");
        foreach (var (how, stimulus) in Hovers)
            yield return new($"Button.tab.active:{how}" + face, $"selected and {how}", () => Alone(Press("tab", "active")),
                stimulus, StatePart.Face, ContentPresenter.BorderBrushProperty, "Intent.Accent");
        yield return new("Button.tab.active", "selected and keyboard focus", () => Alone(Press("tab", "active")),
            StateStimulus.KeyboardFocus, StatePart.Ring, Border.BorderBrushProperty, "Intent.Focus");

        foreach (var (how, stimulus) in Hovers)
        {
            yield return new($"Button.timingRuleRow:{how}" + face, how, () => Alone(Press("timingRuleRow")), stimulus,
                StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Surface.Hover");
            yield return new($"Button.timingRuleRow:{how}" + face, how, () => Alone(Press("timingRuleRow")), stimulus,
                StatePart.Face, ContentPresenter.BorderBrushProperty, "Intent.Border");
            yield return new($"Button.timingRuleRow.chosen:{how}" + face, $"selected and {how}",
                () => Alone(Press("timingRuleRow", "chosen")), stimulus, StatePart.Face, ContentPresenter.BackgroundProperty,
                "Intent.Selected.Fill");
        }
        yield return new("Button.timingRuleRow.chosen", "selected", () => Alone(Press("timingRuleRow", "chosen")), StateStimulus.None,
            StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Selected.Fill");
        yield return new("Button.timingRuleRow.chosen", "selected and keyboard focus", () => Alone(Press("timingRuleRow", "chosen")),
            StateStimulus.KeyboardFocus, StatePart.Ring, Border.BorderBrushProperty, "Intent.Focus");

        yield return new("ToolTip:open :is(TextBlock)", "open", OpenTip, StateStimulus.None, StatePart.Self,
            TextBlock.ForegroundProperty, "Intent.Tooltip.Text");
        yield return new("ToolTip:open :is(TextBlock)", "open", OpenTip, StateStimulus.None, StatePart.Self,
            TextBlock.FontSizeProperty, "Intent.Type.Small");

        yield return new("Button.traceMode.chosen /template/ ContentPresenter#PART_ContentPresenter", "selected",
            () => Alone(Press("traceMode", "chosen")), StateStimulus.None,
            StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Emphasis.Fill");
        yield return new("Button.traceMode.chosen /template/ ContentPresenter#PART_ContentPresenter", "selected label",
            () => Alone(Press("traceMode", "chosen")), StateStimulus.None,
            StatePart.Face, ContentPresenter.ForegroundProperty, "Intent.Emphasis.Text");
        yield return new("Button.traceMode.chosen:pointerover /template/ ContentPresenter#PART_ContentPresenter", "selected and hovered",
            () => Alone(Press("traceMode", "chosen")), StateStimulus.Pointer, StatePart.Face,
            ContentPresenter.BackgroundProperty, "Intent.Emphasis.Fill");
        yield return new("Button.traceMode.chosen:pressed /template/ ContentPresenter#PART_ContentPresenter", "selected and pressed",
            () => Alone(Press("traceMode", "chosen")), StateStimulus.Press, StatePart.Face,
            ContentPresenter.BackgroundProperty, "Intent.Emphasis.Fill");

        yield return new("Button.stopGroup.chosen", "selected", () => Alone(Press("stopGroup", "chosen")), StateStimulus.None,
            StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Danger.Fill");
        foreach (var (how, stimulus) in Hovers)
            yield return new($"Button.stopGroup.chosen:{how}" + face, $"selected and {how}",
                () => Alone(Press("stopGroup", "chosen")), stimulus, StatePart.Face, ContentPresenter.BackgroundProperty,
                "Intent.Danger.Fill");
        const string warningSummaryHover = "Button.warningSummary:pointerover" + face;
        yield return new(warningSummaryHover, "hover", () => Alone(Press("warningSummary")), StateStimulus.Pointer,
            StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Surface.Subtle");
        const string warningSummaryPressed = "Button.warningSummary:pressed" + face;
        yield return new(warningSummaryPressed, "pressed", () => Alone(Press("warningSummary")), StateStimulus.Press,
            StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Surface.Subtle");

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
        yield return new("Border.wordStrip:focus-visible", "keyboard focus", () => Alone(Strip()), StateStimulus.KeyboardFocus,
            StatePart.Self, Border.BorderBrushProperty, "Intent.Focus");
        yield return new("Border.wordStrip:focus-visible", "keyboard focus", () => Alone(Strip()), StateStimulus.KeyboardFocus,
            StatePart.Self, Border.BorderThicknessProperty, "Intent.Stroke.Focus");

        const string rowsFace = " /template/ ContentPresenter#PART_ContentPresenter";
        yield return new("ListBox.wordRows ListBoxItem:pointerover" + rowsFace, "hover", () => WordRowsEntry(selected: false),
            StateStimulus.Pointer, StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Clear");
        yield return new("ListBox.wordRows ListBoxItem:selected" + rowsFace, "selected", () => WordRowsEntry(selected: true),
            StateStimulus.None, StatePart.Face, ContentPresenter.BackgroundProperty, "Intent.Clear");
        yield return new("Border.wordRowFrame:pointerover", "hover", () => Alone(WordRowLine()), StateStimulus.Pointer,
            StatePart.Self, Border.BackgroundProperty, "Intent.Surface.Hover");
        yield return new("Border.wordRowFrame.open", "opened", () => Alone(WordRowLine("open")), StateStimulus.None,
            StatePart.Self, Border.BackgroundProperty, "Intent.Selected.Fill");
        yield return new("Border.wordRowFrame.open", "opened under the pointer", () => Alone(WordRowLine("open")),
            StateStimulus.Pointer, StatePart.Self, Border.BackgroundProperty, "Intent.Selected.Fill");
        yield return new("Border.wordRowFrame.open", "opened and keyboard focus", OpenRowBody, StateStimulus.KeyboardFocus,
            StatePart.Ring, Border.BorderBrushProperty, "Intent.Focus");
        yield return new("Border.wordRowFrame.open Border.wordRowEdge", "opened", OpenRowEdge, StateStimulus.None,
            StatePart.Self, Border.BorderBrushProperty, "Intent.Accent");

        const string nextHover = "Border.wordRowFrame:pointerover StackPanel.wordRowNext HyperlinkButton";
        yield return new(nextHover, "a next step at rest, before the row is hovered", () => NextStep(), StateStimulus.None,
            StatePart.Text, TextBlock.ForegroundProperty, "Intent.TextSecondary");
        yield return new(nextHover, "a next step on row hover", () => NextStep(), StateStimulus.Pointer,
            StatePart.Text, TextBlock.ForegroundProperty, "Intent.Accent");
        yield return new("Border.wordRowFrame:focus-within StackPanel.wordRowNext HyperlinkButton", "a next step with focus in the row",
            () => NextStep(), StateStimulus.KeyboardFocus, StatePart.Text, TextBlock.ForegroundProperty, "Intent.Accent");
        yield return new("Border.wordRowFrame.open StackPanel.wordRowNext HyperlinkButton", "a next step on an opened row",
            () => NextStep("open"), StateStimulus.None, StatePart.Text, TextBlock.ForegroundProperty, "Intent.Accent");
        yield return new("StackPanel.wordRowNext HyperlinkButton:disabled", "a next step that cannot open, on an opened row",
            () => NextStep("open"), StateStimulus.Disabled, StatePart.Text, TextBlock.ForegroundProperty, "Intent.TextFaint");
    }

    // A press lands under the pointer, so each face style is reached once by hovering and once by pressing.
    private static readonly (string How, StateStimulus Stimulus)[] Hovers =
        [("pointerover", StateStimulus.Pointer), ("pressed", StateStimulus.Press)];

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

    private static (Control, Control) WordRowsEntry(bool selected)
    {
        var list = new ListBox { Classes = { "wordRows" } };
        var item = new ListBoxItem { Content = "kitabu" };
        list.Items.Add(item);
        if (selected) list.SelectedIndex = 0;
        return (list, item);
    }

    private static Border WordRowLine(params string[] classes)
    {
        var line = new Border { Classes = { "wordRowFrame" }, Width = 300, Height = 40, Child = new TextBlock { Text = "kitabu" } };
        line.Classes.AddRange(classes);
        return line;
    }

    private static (Control, Control) OpenRowBody()
    {
        var body = new Border { Classes = { "wordRowBody" }, Focusable = true, Child = new TextBlock { Text = "kitabu" } };
        return (new Border { Classes = { "wordRowFrame", "open" }, Width = 300, Height = 40, Child = body }, body);
    }

    private static (Control, Control) NextStep(params string[] classes)
    {
        var link = new HyperlinkButton { Content = "Try a Word" };
        var frame = new Border
        {
            Classes = { "wordRowFrame" }, Width = 300, Height = 40,
            Child = new StackPanel { Classes = { "wordRowNext" }, Children = { link } },
        };
        frame.Classes.AddRange(classes);
        return (frame, link);
    }

    private static (Control, Control) OpenRowEdge()
    {
        var edge = new Border { Classes = { "wordRowEdge" }, Child = new TextBlock { Text = "kitabu" } };
        return (new Border { Classes = { "wordRowFrame", "open" }, Width = 300, Height = 40, Child = edge }, edge);
    }

    private static (Control, Control) Alone(Control control) => (control, control);

    private static Button Press(params string[] classes)
    {
        var button = new Button { Content = "Go" };
        button.Classes.AddRange(classes);
        return button;
    }

    private static Border Morpheme() =>
        new() { Classes = { "morph", "inspectable" }, Focusable = true, Width = 80, Height = 40, Child = new TextBlock { Text = "kat" } };

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
    private static (Control, Control) RevealOwner(bool open = false)
    {
        var button = Press("revealControl", "revealButton", "revealOnHover");
        // Every owner in the window paints a fill, which is what makes the gap around its button take the pointer.
        var owner = new Border { Classes = { "hoverReveal" }, Background = Brushes.Transparent, Child = button, Width = 200, Height = 40 };
        if (open) owner.Classes.Add("open");
        return (owner, button);
    }

    private static (Control, Control) RevealTextOwner(bool open = false)
    {
        var action = new TextBlock { Classes = { "revealOnHover" }, Text = "See the words" };
        var button = Press("overviewLookFirstRow");
        button.Content = action;
        var owner = new Border { Classes = { "hoverReveal" }, Background = Brushes.Transparent, Child = button, Width = 200, Height = 40 };
        if (open) owner.Classes.Add("open");
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

/// <summary>A state of the sample window in which a group of tooltip owners shows.</summary>
internal enum TooltipScene
{
    Overview,
    OpenRecent,
    CollapsedSidebar,
    Matrix,
    Reader,
    AnalyzeWordList,
    ReaderDisapproved,
    TextPicker,
    WordCard,
    Lists,
    TryAWordEarlierTiming,
    TryAWordRecordedDetails,
    TryAWordRepeatedRecords,
    ExpertTrace,
    Timing,
    Statistics,
    Warnings,
    Handoff,
    ReaderStaged,
    MatrixStaged,
    ListsStaged,
    WordCardWithoutOccurrence,
    ReviewStaged,
}

/// <summary>
/// One tooltip the views declare: where it is declared, as the declaration spells its content, the scene of the sample
/// window that shows it, and how to tell its owner from every other control. <see cref="Pending"/> names what the
/// sample cannot yet reach, so an owner is either checked or visibly unchecked.
/// </summary>
internal sealed record TooltipOwner(string Key, string Source, string Declaration, TooltipScene Scene, Func<Control, bool> Is)
{
    public string? Pending { get; init; }

    /// <summary>A reported product gap: the owner is reached but its tip shows no words, for this reason.</summary>
    public string? Gap { get; init; }

    public override string ToString() => $"{Key} ({Source}: {Declaration})";
}

/// <summary>Every tooltip owner the views declare, so a realized tooltip that matches none, or an owner never seen, fails.</summary>
internal static class TooltipOwners
{
    internal static IReadOnlyList<TooltipOwner> All { get; } =
    [
        new("parse all words", "Views/MainWindow.axaml", "Parse the words you chose.", TooltipScene.Overview,
            control => control is Button && Name(control) == "Parse all words")
        {
            Pending = "the sample has just parsed and saved no default choice of words, so the button is hidden",
        },
        new("refresh", "Views/MainWindow.axaml", "Capture a new Baseline from FieldWorks' last save.", TooltipScene.Overview,
            control => control is Button && Name(control) == "Refresh the project"),
        new("collapsed sidebar entry", "Views/MainWindow.axaml", "{Binding Title}", TooltipScene.CollapsedSidebar,
            control => control is ListBoxItem && control.FindAncestorOfType<ListBox>() is { } list &&
                list.Classes.Contains("sidebar") && list.Classes.Contains("collapsed")),
        new("recent project", "Views/MainWindow.axaml.cs", "recent.FullFwDataPath", TooltipScene.OpenRecent,
            control => control is MenuItem),
        new("Matrix cell", "Views/ComparePanel.axaml", "{Binding Explanation}", TooltipScene.Matrix,
            control => control is MatrixCell && control.FindAncestorOfType<MiniMatrix>() is null),
        new("Parse stopped words again", "Views/ComparePanel.axaml", "Parse the stopped and unparsed words again",
            TooltipScene.Matrix,
            control => control is SplitButton && Name(control) == "Parse the stopped and unparsed words again"),
        new("add checked words as Unknown", "Views/ComparePanel.axaml", "{Binding Compare.CheckedWordText}", TooltipScene.Matrix,
            control => control is Button && Name(control) == "Add checked words as Unknown")
        {
            Pending = "the Matrix scene has no checked words, so the action is hidden",
        },
        new("mark checked words as incorrect spelling", "Views/ComparePanel.axaml", "{Binding Compare.CheckedWordText}", TooltipScene.Matrix,
            control => control is Button && Name(control) == "Mark checked words as incorrect spelling")
        {
            Pending = "the Matrix scene has no checked words, so the action is hidden",
        },
        new("FieldWorks column heading", "Views/WordRowHeader.axaml", "FieldWorks", TooltipScene.Lists,
            control => control is CopyableTextBlock && Name(control) == "FieldWorks" &&
                control.Classes.Contains("wordRowHeading") && control.Bounds.Width > 0 && control.Bounds.Height > 0 &&
                control.FindAncestorOfType<WordRowHeader>() is not null),
        new("PanGloss column heading", "Views/WordRowHeader.axaml", "PanGloss", TooltipScene.Lists,
            control => control is CopyableTextBlock && Name(control) == "PanGloss" &&
                control.Classes.Contains("wordRowHeading") && control.Bounds.Width > 0 && control.Bounds.Height > 0 &&
                control.FindAncestorOfType<WordRowHeader>() is not null),
        new("pending change in a Matrix cell", "Views/MatrixCell.axaml", "{Binding PendingChangeStatus}", TooltipScene.MatrixStaged,
            control => control is Ellipse && control.Classes.Contains("matrixPending")),
        new("word row", "Views/WordRow.axaml", "{Binding Summary}", TooltipScene.Matrix,
            control => control is Border && control.Classes.Contains("wordRowBody")),
        new("places in a word row", "Views/WordRow.axaml", "{Binding PlacesTooltip}", TooltipScene.Matrix,
            control => control is CopyableTextBlock && control.Classes.Contains("wordRowPlaces") &&
                control.FindAncestorOfType<WordRow>() is not null),
        new("unread word in a row", "Views/WordRow.axaml", "{Binding UnreadText}", TooltipScene.Matrix,
            control => control is Ellipse && control.FindAncestorOfType<WordRow>() is not null)
        {
            Pending = "the sample's Matrix words carry no read state until Analyze texts loads it",
        },
        new("Word Analyses on a row", "Views/WordRow.axaml", "{Binding WordAnalysesTip}", TooltipScene.Matrix,
            control => control is HyperlinkButton && control.FindAncestorOfType<WordRow>() is not null &&
                control.GetVisualAncestors().OfType<StackPanel>().Any(panel => panel.Classes.Contains("wordRowNext"))),
        new("compact Matrix cell", "Views/MiniMatrix.axaml", "{Binding AccessibleName}", TooltipScene.Matrix,
            control => control is MatrixCell && control.FindAncestorOfType<MiniMatrix>() is not null)
        {
            Pending = "the sample has one parse, so What changed draws no before-and-after Matrix",
        },
        new("text to read", "Views/ResultsInTextPanel.axaml", "{Binding Title}", TooltipScene.TextPicker,
            control => control is TextBlock && control.DataContext is ResultsTextViewModel),
        new("word strip", "Views/ResultsInTextPanel.axaml", "{Binding HoverSummary}", TooltipScene.Reader,
            control => control is Border { Name: "WordStrip" }),
        new("disapproved mark on a strip", "Views/ResultsInTextPanel.axaml", "{Binding DisapprovedTip}", TooltipScene.ReaderDisapproved,
            control => control is MarkChip && control.GetVisualAncestors().OfType<Border>().Any(border => border.Name == "WordStrip")),
        new("staged change", "Views/ResultsInTextPanel.axaml", "{Binding FitStatus}", TooltipScene.ReaderStaged,
            control => control is Border && control.Classes.Contains("stagedStrip")),
        new("opinion on a word card", "Views/ResultsInTextPanel.axaml", "{Binding OpinionLabel}", TooltipScene.WordCard,
            control => control is OpinionMark),
        new("mark unread without a text occurrence", "Views/ResultsInTextPanel.axaml", "{Binding MarkUnreadDisabledReason}",
            TooltipScene.WordCardWithoutOccurrence,
            control => control is Button && Name(control) == "Mark this occurrence as unread"),
        new("morpheme form that is its link", "Views/MorphemeRow.cs", "morph.FormLinkTip", TooltipScene.Matrix,
            control => control is HyperlinkButton && control.Classes.Contains("morphFormLink") &&
                !control.Classes.Contains("listCardFormLink"))
        {
            Pending = "no tooltip scene opens a word row's card",
        },
        new("FieldWorks link on a morpheme", "Views/MorphemeRow.cs", "morph.LinkName", TooltipScene.WordCard,
            control => control is HyperlinkButton && control.Classes.Contains("morphLink")),
        new("morpheme that opens the inspector", "Views/MorphemeRow.cs", "$\"Show {morph.Form} in the inspector\"",
            TooltipScene.WordCard, control => control is Border && control.Classes.Contains("inspectable")),
        new("closing the inspector", "Views/Inspector.axaml", "Close (Esc steps back)", TooltipScene.WordCard,
            control => control is Button && control.Classes.Contains("inspectorClose"))
        {
            Pending = "no tooltip scene opens the inspector",
        },
        new("freshness of the inspector's facts", "Views/Inspector.axaml", "{Binding FreshnessTip}", TooltipScene.WordCard,
            control => control is Ellipse && control.Classes.Contains("freshDot") && control.FindAncestorOfType<Inspector>() is not null)
        {
            Pending = "no tooltip scene opens the inspector",
        },
        new("freshness of what the inspected object is", "Views/Inspector.axaml", "{Binding FreshnessTip}", TooltipScene.WordCard,
            control => control is Ellipse && control.Classes.Contains("freshDot") && control.FindAncestorOfType<Inspector>() is not null)
        {
            Pending = "no tooltip scene opens the inspector",
        },
        new("FieldWorks link in the inspector", "Views/Inspector.axaml", "{Binding LinkName}", TooltipScene.WordCard,
            control => control is HyperlinkButton && control.Classes.Contains("revealLink") &&
                control.FindAncestorOfType<Inspector>() is not null)
        {
            Pending = "no tooltip scene opens the inspector",
        },
        new("pending change on a list chip", "Views/TextsListsPanel.axaml", "{Binding PendingChangeStatus}", TooltipScene.ListsStaged,
            control => control is Ellipse && control.Classes.Contains("freshDot") && control.FindAncestorOfType<TextsListsPanel>() is not null),
        new("the list's sentence", "Views/TextsListsPanel.axaml", "{Binding Lists.SelectedList.Sentence}", TooltipScene.Lists,
            control => control is TextBlock && ToolTip.GetTip(control) is not null &&
                control.GetVisualAncestors().OfType<DockPanel>().Any(panel => panel.Name == "ListHeader")),
        new("Parse again on a list", "Views/TextsListsPanel.axaml", "{Binding Lists.ParseAgainHelpText}", TooltipScene.Lists,
            control => control is Button && Name(control) == "Parse the selected list's stopped and unparsed words again")
        {
            Pending = "the Lists scene opens a list with no stopped or unparsed words, so Parse again is hidden",
        },
        new("morpheme form in a list card", "Views/ListWordCard.axaml.cs", "morph.FormLinkTip", TooltipScene.Lists,
            control => control is HyperlinkButton && control.Classes.Contains("listCardFormLink"))
        {
            Pending = "no tooltip scene opens a list row's card",
        },
        new("ticked words to AI Handoff", "Views/TextsListsPanel.axaml", "{Binding Lists.HandOffCheckedWordsHelpText}",
            TooltipScene.Lists, control => control is Button && Name(control) == "AI Handoff for ticked words in this list"),
        new("checked words to AI Handoff in Analyze texts", "Views/TextWordsPanel.axaml", "{Binding Words.HandOffCheckedWordsHelpText}",
            TooltipScene.AnalyzeWordList, control => control is Button &&
                Name(control) is { } name && name.StartsWith("AI Handoff", StringComparison.Ordinal) &&
                control.FindAncestorOfType<TextWordsPanel>() is not null),
        new("whole list to AI Handoff", "Views/TextsListsPanel.axaml", "{Binding Lists.HandOffListDisabledReason}",
            TooltipScene.Lists, control => control is Button && Name(control) == "AI Handoff for this list")
        {
            Gap = "while the list can go to AI Handoff its reason is empty, and an empty tooltip still opens",
        },
        new("Apply to FieldWorks project", "Views/ReviewPanel.axaml", "{Binding ApplyDisabledReason}", TooltipScene.ReviewStaged,
            control => control is Button && Name(control) == "Apply to FieldWorks project"),
        new("expert tree scope", "Views/ExpertTracePanel.axaml", "Turn off to show only the chosen attempt's recorded ancestors and terminal event.", TooltipScene.ExpertTrace,
            control => control.FindAncestorOfType<ExpertTracePanel>() is not null && ToolTip.GetTip(control) as string == "Turn off to show only the chosen attempt's recorded ancestors and terminal event."),
        new("expert event occurrence", "Views/ExpertTracePanel.axaml", "Select this occurrence to read its recorded details.", TooltipScene.ExpertTrace,
            control => control.FindAncestorOfType<ExpertTracePanel>() is not null && ToolTip.GetTip(control) as string == "Select this occurrence to read its recorded details."),
        new("expert input shape", "Views/ExpertTracePanel.axaml", "{Binding InputTip}", TooltipScene.ExpertTrace,
            control => control.FindAncestorOfType<ExpertTracePanel>() is not null &&
                control.FindAncestorOfType<Expander>() is null &&
                ToolTip.GetTip(control) is string inputTip && inputTip.Contains("input", StringComparison.OrdinalIgnoreCase)),
        new("expert output shape", "Views/ExpertTracePanel.axaml", "{Binding OutputTip}", TooltipScene.ExpertTrace,
            control => control.FindAncestorOfType<ExpertTracePanel>() is not null &&
                control.FindAncestorOfType<Expander>() is null &&
                ToolTip.GetTip(control) is string outputTip && outputTip.Contains("output", StringComparison.OrdinalIgnoreCase)),
        new("expert phonological input shape", "Views/ExpertTracePanel.axaml", "{Binding InputTip}", TooltipScene.ExpertTrace,
            control => control.FindAncestorOfType<ExpertTracePanel>() is not null &&
                control.FindAncestorOfType<Expander>() is { } expander &&
                AutomationProperties.GetName(expander) == "Expert phonological rules" &&
                ToolTip.GetTip(control) is string inputTip && inputTip.Contains("input", StringComparison.OrdinalIgnoreCase)),
        new("expert phonological output shape", "Views/ExpertTracePanel.axaml", "{Binding OutputTip}", TooltipScene.ExpertTrace,
            control => control.FindAncestorOfType<ExpertTracePanel>() is not null &&
                control.FindAncestorOfType<Expander>() is { } expander &&
                AutomationProperties.GetName(expander) == "Expert phonological rules" &&
                ToolTip.GetTip(control) is string outputTip && outputTip.Contains("output", StringComparison.OrdinalIgnoreCase)),
        new("expert subrule index", "Views/ExpertTracePanel.axaml", "The producer's subrule index. No allomorph identity is inferred from it.", TooltipScene.ExpertTrace,
            control => control.FindAncestorOfType<ExpertTracePanel>() is not null && ToolTip.GetTip(control) as string == "The producer's subrule index. No allomorph identity is inferred from it."),
        new("expert environment token", "Views/ExpertTracePanel.axaml", "{Binding Explanation}", TooltipScene.ExpertTrace,
            control => control is TraceNotationToken { DataContext: EnvironmentToken }),
        new("earlier timing kind", "Views/TryWordPanel.axaml", "{Binding KindTip}", TooltipScene.TryAWordEarlierTiming,
            control => control is TextBlock { DataContext: TryWordEarlierRuleTime }),
        new("parser's morphemes", "Views/TraceAnalysesView.axaml", "{Binding LegacyMorphemesTip}", TooltipScene.TryAWordRecordedDetails,
            control => control is CopyableTextBlock { DataContext: TraceAnalysisViewModel analysis } text && text.Text == analysis.Surface),
        new("repeated records", "Views/TraceAnalysesView.axaml", "The trace recorded this result more than once; distinct derivations are not established.",
            TooltipScene.TryAWordRepeatedRecords,
            control => control is CopyableTextBlock { DataContext: TraceAnalysisViewModel analysis } text && text.Text == analysis.RecordedCountText),
        new("analysis that could not be shown", "Views/TraceAnalysesView.axaml", "{Binding ProjectionError}", TooltipScene.TryAWordRecordedDetails,
            control => control is CopyableTextBlock { DataContext: TraceAnalysisViewModel analysis } text &&
                text.Text == analysis.ProjectionErrorText),
        new("WORDS column", "Views/Pages/TimingPage.axaml", "How many of these words the parser recorded time for this rule in",
            TooltipScene.Timing, control => control is CopyableTextBlock { Text: "WORDS" }),
        new("grammar warning named this rule", "Views/Pages/TimingPage.axaml",
            "{x:Static vm:TimingRuleRow.GrammarWarningTooltip}", TooltipScene.Timing,
            control => control is Button && Name(control) == "Open the grammar warning"),
        new("completion in detailed statistics", "Views/StatisticsPanel.axaml", "{Binding CompletionStatus}", TooltipScene.Statistics,
            control => control is MarkChip && control.FindAncestorOfType<StatisticsPanel>() is not null),
        new("FieldWorks link in a finding", "Views/GrammarWarningPartsBlock.cs",
            "$\"Open this item in {tool}\"", TooltipScene.Warnings,
            control => control is HyperlinkButton && control.Classes.Contains("warningObjectLink")),
        new("drag all files", "Views/HandoffPanel.axaml", "Drag into a chat. From the keyboard, press Enter to copy the folder path.",
            TooltipScene.Handoff, control => control is Button { Name: "AllFilesButton" }),
        new("question to copy", "Views/HandoffPanel.axaml", "Copy this question", TooltipScene.Handoff,
            control => control is Button && control.Classes.Contains("handoffQuestion")),
    ];

    private static string? Name(Control control) => Avalonia.Automation.AutomationProperties.GetName(control);
}
