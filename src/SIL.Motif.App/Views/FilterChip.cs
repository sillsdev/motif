using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// One filter chip: a label, how many rows it would leave, and — when the filter names a verdict — that
/// verdict's glyph in its colour. Every stage's chips are this control, so a chip means the same everywhere.
/// </summary>
public sealed class FilterChip : Button
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<FilterChip, string?>(nameof(Label));

    public static readonly StyledProperty<int> CountProperty =
        AvaloniaProperty.Register<FilterChip, int>(nameof(Count));

    public static readonly StyledProperty<Verdict?> VerdictProperty =
        AvaloniaProperty.Register<FilterChip, Verdict?>(nameof(Verdict));

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<FilterChip, bool>(nameof(IsActive));

    static FilterChip()
    {
        LabelProperty.Changed.AddClassHandler<FilterChip>((chip, _) => chip.Rebuild());
        CountProperty.Changed.AddClassHandler<FilterChip>((chip, _) => chip.Rebuild());
        VerdictProperty.Changed.AddClassHandler<FilterChip>((chip, _) => chip.Rebuild());
        IsActiveProperty.Changed.AddClassHandler<FilterChip>((chip, _) => chip.Rebuild());
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

    /// <summary>The meaning this chip filters to, or <see langword="null"/> for a chip like All.</summary>
    public Verdict? Verdict
    {
        get => GetValue(VerdictProperty);
        set => SetValue(VerdictProperty, value);
    }

    /// <summary>Whether this chip is the one in force.</summary>
    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    private void Rebuild()
    {
        Classes.Set("active", IsActive);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (Verdict is { } verdict)
        {
            var glyph = new TextBlock { Text = Verdicts.GlyphOf(verdict), FontWeight = FontWeight.Bold };
            glyph.Classes.Add("verdictGlyph");
            glyph.Classes.Add(Verdicts.ClassOf(verdict));
            row.Children.Add(glyph);
        }
        row.Children.Add(new TextBlock { Text = Label });
        var count = new TextBlock { Text = Count.ToString("N0"), FontWeight = FontWeight.SemiBold };
        count.Classes.Add("chipCount");
        row.Children.Add(count);
        Content = row;
        AutomationProperties.SetName(this, $"Show {Label}");
    }
}
