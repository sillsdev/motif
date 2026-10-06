using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// One mark as a chip: the mark's glyph, then the view's words. The <see cref="Mark"/> chooses the colour through
/// its kind and value classes, so a parser outcome, an opinion and a meaning never look alike. A chip with no mark
/// shows its words plainly.
/// </summary>
public sealed class MarkChip : Border
{
    public static readonly StyledProperty<Mark?> MarkProperty =
        AvaloniaProperty.Register<MarkChip, Mark?>(nameof(Mark));

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<MarkChip, string?>(nameof(Text));

    public static readonly StyledProperty<bool> CompactProperty =
        AvaloniaProperty.Register<MarkChip, bool>(nameof(Compact));

    public static readonly StyledProperty<bool> WrapTextProperty =
        AvaloniaProperty.Register<MarkChip, bool>(nameof(WrapText));

    public static readonly StyledProperty<TextTrimming> TextTrimmingProperty =
        AvaloniaProperty.Register<MarkChip, TextTrimming>(nameof(TextTrimming));

    static MarkChip()
    {
        MarkProperty.Changed.AddClassHandler<MarkChip>((chip, _) => chip.Rebuild());
        TextProperty.Changed.AddClassHandler<MarkChip>((chip, _) => chip.Rebuild());
        CompactProperty.Changed.AddClassHandler<MarkChip>((chip, _) => chip.Rebuild());
        WrapTextProperty.Changed.AddClassHandler<MarkChip>((chip, _) => chip.Rebuild());
        TextTrimmingProperty.Changed.AddClassHandler<MarkChip>((chip, _) => chip.Rebuild());
    }

    public MarkChip()
    {
        Classes.Add("markChip");
        Rebuild();
    }

    // Styled as a Border: Avalonia matches a style's type against this, not the subclass.
    protected override Type StyleKeyOverride => typeof(Border);

    /// <summary>The mark this chip shows, or <see langword="null"/> for words alone.</summary>
    public Mark? Mark
    {
        get => GetValue(MarkProperty);
        set => SetValue(MarkProperty, value);
    }

    /// <summary>The view's own words beside the mark; empty shows the mark alone.</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Whether the chip is the smaller size a list row uses.</summary>
    public bool Compact
    {
        get => GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    /// <summary>Whether the chip's words may wrap in a constrained cell.</summary>
    public bool WrapText
    {
        get => GetValue(WrapTextProperty);
        set => SetValue(WrapTextProperty, value);
    }

    /// <summary>Whether the words stop at the chip's edge when a containing cell is narrow.</summary>
    public TextTrimming TextTrimming
    {
        get => GetValue(TextTrimmingProperty);
        set => SetValue(TextTrimmingProperty, value);
    }

    // Inside a button the button owns the pointer, so the chip's words are plain there and selectable elsewhere.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var insideButton = this.FindAncestorOfType<Button>() is not null;
        if (insideButton == _insideButton) return;
        _insideButton = insideButton;
        Rebuild();
    }

    private bool _insideButton;

    private void Rebuild()
    {
        MarkClasses.SetMark(this, Mark);
        Classes.Set("plain", Mark is null);
        Classes.Set("compact", Compact);
        Classes.Set("wrapText", WrapText);

        var glyph = Mark is { } mark ? GlyphOf(mark) : null;
        var row = WrapText
            ? (Panel)new Grid
            {
                ColumnDefinitions = new ColumnDefinitions(glyph is null ? "*" : "Auto,*"),
                Classes = { "markChipRow" },
            }
            : new StackPanel { Orientation = Orientation.Horizontal, Classes = { "markChipRow" } };
        if (glyph is not null)
        {
            if (row is Grid grid) Grid.SetColumn(glyph, 0);
            row.Children.Add(glyph);
        }
        if (Text is { Length: > 0 } text)
        {
            var words = Words(text, _insideButton);
            words.TextTrimming = TextTrimming;
            words.Classes.Add("markWord");
            if (Mark is { } owner) words.Classes.AddRange([owner.KindClass, owner.KindClass + "Mark", owner.Value]);
            if (row is Grid gridRow) Grid.SetColumn(words, glyph is null ? 0 : 1);
            row.Children.Add(words);
        }
        Child = row;
        AutomationProperties.SetName(this, Text is { Length: > 0 } label ? label : Mark?.Word ?? string.Empty);
    }

    /// <summary>
    /// The control that draws <paramref name="mark"/>'s glyph: an opinion's letter box, a severity's own shape, or a
    /// sign in its kind's colour; <see langword="null"/> for a meaning, which has no glyph.
    /// </summary>
    internal static Control? GlyphOf(Mark mark)
    {
        switch (mark.Kind)
        {
            case MarkKind.Opinion:
                return new OpinionMark(mark.Opinion ?? OpinionMarkKind.None) { VerticalAlignment = VerticalAlignment.Center };
            case MarkKind.Severity:
                return new Border
                {
                    Classes = { "severityGlyph", mark.Value },
                    Child = new MarkGlyph { Mark = mark, Classes = { "inline" } },
                };
            case MarkKind.Meaning:
                return null;
            default:
                return new MarkGlyph { Mark = mark, Classes = { "inline" } };
        }
    }

    private static TextBlock Words(string text, bool insideButton) =>
        insideButton ? new TextBlock { Text = text } : new CopyableTextBlock { Text = text };
}
