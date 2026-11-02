using Avalonia;
using System.Globalization;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// One filter chip: a label, how many rows it would leave, and — when the filter names a mark — that mark's glyph
/// in its colour. Every stage's chips are this control, so a chip means the same everywhere.
/// </summary>
public sealed class FilterChip : Button
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<FilterChip, string?>(nameof(Label));

    public static readonly StyledProperty<int> CountProperty =
        AvaloniaProperty.Register<FilterChip, int>(nameof(Count));

    public static readonly StyledProperty<string?> CountTextProperty =
        AvaloniaProperty.Register<FilterChip, string?>(nameof(CountText));

    public static readonly StyledProperty<Mark?> MarkProperty =
        AvaloniaProperty.Register<FilterChip, Mark?>(nameof(Mark));

    public static readonly StyledProperty<string?> ChipNameProperty =
        AvaloniaProperty.Register<FilterChip, string?>(nameof(ChipName));

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<FilterChip, bool>(nameof(IsActive));

    static FilterChip()
    {
        LabelProperty.Changed.AddClassHandler<FilterChip>((chip, _) => chip.Rebuild());
        CountProperty.Changed.AddClassHandler<FilterChip>((chip, _) => chip.Rebuild());
        CountTextProperty.Changed.AddClassHandler<FilterChip>((chip, _) => chip.Rebuild());
        MarkProperty.Changed.AddClassHandler<FilterChip>((chip, _) => chip.Rebuild());
        IsActiveProperty.Changed.AddClassHandler<FilterChip>((chip, _) => chip.Rebuild());
        ChipNameProperty.Changed.AddClassHandler<FilterChip>((chip, _) => chip.Rebuild());
    }

    public FilterChip()
    {
        Classes.Add("filterChip");
        Rebuild();
    }

    // Styled and templated as a Button: Avalonia matches a style's type against this, not the subclass.
    protected override Type StyleKeyOverride => typeof(Button);

    /// <summary>What the chip filters to, in the view's own words.</summary>
    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>How many rows or words the filter would leave.</summary>
    public int Count
    {
        get => GetValue(CountProperty);
        set => SetValue(CountProperty, value);
    }

    /// <summary>The count in words when it is not one number, such as a pair; when set, it replaces <see cref="Count"/>.</summary>
    public string? CountText
    {
        get => GetValue(CountTextProperty);
        set => SetValue(CountTextProperty, value);
    }

    /// <summary>The mark this chip filters to, or <see langword="null"/> for a chip like All.</summary>
    public Mark? Mark
    {
        get => GetValue(MarkProperty);
        set => SetValue(MarkProperty, value);
    }

    /// <summary>What a screen reader says for the chip when the standard "Filter by" sentence would leave out whose number it is.</summary>
    public string? ChipName
    {
        get => GetValue(ChipNameProperty);
        set => SetValue(ChipNameProperty, value);
    }

    /// <summary>Whether this chip is the one in force.</summary>
    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>Exposes the chip as a button that is on while its filter is in force, as well as invokable.</summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new FilterChipAutomationPeer(this);

    private sealed class FilterChipAutomationPeer(FilterChip owner) : ButtonAutomationPeer(owner), IToggleProvider
    {
        public ToggleState ToggleState => owner.IsActive ? ToggleState.On : ToggleState.Off;

        public void Toggle() => ((IInvokeProvider)this).Invoke();
    }

    private void Rebuild()
    {
        Classes.Set("active", IsActive);
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Classes = { "filterChipRow" },
        };
        if (Mark is { } mark && MarkChip.GlyphOf(mark) is { } glyph)
        {
            row.Children.Add(glyph);
            Grid.SetColumn(glyph, 0);
        }
        var label = new TextBlock { Text = Label, TextTrimming = TextTrimming.CharacterEllipsis };
        row.Children.Add(label);
        Grid.SetColumn(label, 1);
        var count = new TextBlock { Text = CountText ?? Count.ToString("N0"), Classes = { "chipCount" } };
        row.Children.Add(count);
        Grid.SetColumn(count, 2);
        Content = row;
        AutomationProperties.SetName(this, ChipName ?? string.Format(CultureInfo.CurrentCulture,
            MotifAppStrings.FilterChipAccessibleNameFormat, Label));
        // A long label is trimmed to fit its chip, so the whole label is also the tooltip.
        ToolTip.SetTip(this, string.IsNullOrEmpty(Label) ? null : Label);
    }
}
