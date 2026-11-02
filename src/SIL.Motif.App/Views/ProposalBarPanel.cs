using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace SIL.Motif.App.Views;

/// <summary>What a child of the Proposal bar is, which decides where it goes when the bar has to wrap.</summary>
public enum ProposalBarPart
{
    /// <summary>The Proposal's name, which trims first when the buttons need the room.</summary>
    Name,

    /// <summary>The sentence about the Trial, which trims before anything else does.</summary>
    Sentence,

    /// <summary>The view switch, shown only on pages that follow it.</summary>
    View,

    /// <summary>A button that must stay inside the window, such as Try Again or Leave Proposal.</summary>
    Action,
}

/// <summary>
/// Lays the Proposal bar out in one row while its name, switch and buttons leave the sentence room to read, and in
/// two rows when they do not: the name and the buttons on the first, the sentence and the switch on the second.
/// The sentence trims first, then the name, and the buttons are never pushed past the right edge.
/// </summary>
public sealed class ProposalBarPanel : Panel
{
    /// <summary>The part of the bar a child is.</summary>
    public static readonly AttachedProperty<ProposalBarPart> PartProperty =
        AvaloniaProperty.RegisterAttached<ProposalBarPanel, Control, ProposalBarPart>("Part");

    /// <summary>The gap between neighbours in a row.</summary>
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<ProposalBarPanel, double>(nameof(Spacing));

    /// <summary>The least width the sentence must have for the bar to stay in one row.</summary>
    public static readonly StyledProperty<double> MinSentenceWidthProperty =
        AvaloniaProperty.Register<ProposalBarPanel, double>(nameof(MinSentenceWidth));

    static ProposalBarPanel() => AffectsMeasure<ProposalBarPanel>(SpacingProperty, MinSentenceWidthProperty);

    /// <summary>Sets the part of the bar that <paramref name="child"/> is.</summary>
    public static void SetPart(Control child, ProposalBarPart value) => child.SetValue(PartProperty, value);

    /// <summary>Gets the part of the bar that <paramref name="child"/> is.</summary>
    public static ProposalBarPart GetPart(Control child) => child.GetValue(PartProperty);

    /// <summary>The gap between neighbours in a row.</summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>The least width the sentence must have for the bar to stay in one row.</summary>
    public double MinSentenceWidth
    {
        get => GetValue(MinSentenceWidthProperty);
        set => SetValue(MinSentenceWidthProperty, value);
    }

    // The sizes the children want with no limit, kept so arranging decides as measuring did after children trim.
    private readonly Dictionary<Control, Size> _natural = [];

    /// <summary>Whether the last layout put the bar in two rows.</summary>
    public bool IsWrapped { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        _natural.Clear();
        foreach (var child in Children)
        {
            child.Measure(Size.Infinity);
            _natural[child] = child.DesiredSize;
        }
        var width = double.IsInfinity(availableSize.Width) ? OneRowWidth() : availableSize.Width;
        var (slots, height) = Place(width);
        foreach (var (child, slot) in slots) child.Measure(new Size(slot.Width, double.PositiveInfinity));
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (slots, _) = Place(finalSize.Width);
        foreach (var (child, slot) in slots) child.Arrange(slot);
        var tab = 0;
        foreach (var child in ReadingOrder()) KeyboardNavigation.SetTabIndex(child, tab++);
        return finalSize;
    }

    // Tab follows what is drawn: the wrapped bar reads its buttons on the first row before its sentence and switch.
    private IEnumerable<Control> ReadingOrder() => IsWrapped
        ? Shown(ProposalBarPart.Name).Concat(Shown(ProposalBarPart.Action)).Concat(Shown(ProposalBarPart.Sentence))
            .Concat(Shown(ProposalBarPart.View))
        : Shown(ProposalBarPart.Name).Concat(Shown(ProposalBarPart.Sentence)).Concat(Shown(ProposalBarPart.View))
            .Concat(Shown(ProposalBarPart.Action));

    private Size Natural(Control child) => _natural.GetValueOrDefault(child);

    private IEnumerable<Control> Shown(ProposalBarPart part) =>
        Children.Where(child => child.IsVisible && GetPart(child) == part);

    private double OneRowWidth()
    {
        var shown = Children.Where(child => child.IsVisible).ToArray();
        return shown.Sum(child => Natural(child).Width) + Spacing * Math.Max(0, shown.Length - 1);
    }

    // The slots are the one place that decides one row or two, so measuring and arranging cannot disagree.
    private (List<(Control Child, Rect Slot)> Slots, double Height) Place(double width)
    {
        var name = Shown(ProposalBarPart.Name).ToArray();
        var sentence = Shown(ProposalBarPart.Sentence).ToArray();
        var view = Shown(ProposalBarPart.View).ToArray();
        var actions = Shown(ProposalBarPart.Action).ToArray();
        double Desired(Control[] parts) => parts.Sum(child => Natural(child).Width) + Spacing * Math.Max(0, parts.Length - 1);
        double Tallest(IEnumerable<Control> parts) => parts.Select(child => Natural(child).Height).DefaultIfEmpty(0).Max();
        var slots = new List<(Control, Rect)>();
        var first = name.Concat(actions).ToArray();
        var others = Math.Max(0, name.Length + sentence.Length + view.Length + actions.Length - 1);
        var spare = width - (Desired(name) + Desired(view) + Desired(actions) + Spacing * others);
        IsWrapped = sentence.Length > 0 && spare < MinSentenceWidth;
        if (!IsWrapped)
        {
            var rowHeight = Tallest(Children.Where(child => child.IsVisible));
            var x = 0.0;
            foreach (var child in name.Concat(sentence).Concat(view).Concat(actions))
            {
                var w = GetPart(child) == ProposalBarPart.Sentence ? Math.Max(0, spare) : Natural(child).Width;
                slots.Add((child, new Rect(x, (rowHeight - Natural(child).Height) / 2, w, Natural(child).Height)));
                x += w + Spacing;
            }
            return (slots, rowHeight);
        }
        var top = Tallest(first);
        var bottom = Tallest(sentence.Concat(view));
        var right = width;
        foreach (var child in actions.Reverse())
        {
            right -= Natural(child).Width;
            slots.Add((child, new Rect(right, (top - Natural(child).Height) / 2, Natural(child).Width, Natural(child).Height)));
            right -= Spacing;
        }
        foreach (var child in name)
            slots.Add((child, new Rect(0, (top - Natural(child).Height) / 2,
                Math.Max(0, Math.Min(Natural(child).Width, actions.Length > 0 ? right : width)), Natural(child).Height)));
        var edge = width;
        foreach (var child in view)
        {
            edge -= Natural(child).Width;
            slots.Add((child, new Rect(edge, top + (bottom - Natural(child).Height) / 2, Natural(child).Width, Natural(child).Height)));
            edge -= Spacing;
        }
        foreach (var child in sentence)
            slots.Add((child, new Rect(0, top + (bottom - Natural(child).Height) / 2,
                Math.Max(0, view.Length > 0 ? edge : width), Natural(child).Height)));
        return (slots, top + bottom);
    }
}
