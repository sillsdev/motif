using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// One verdict as a chip: its glyph, then the word the view uses for it. The <see cref="Verdict"/> chooses
/// the colour through a style class, so Approved in Texts and Matches in Results read as the same thing.
/// </summary>
public sealed class VerdictChip : Border
{
    public static readonly StyledProperty<Verdict> VerdictProperty =
        AvaloniaProperty.Register<VerdictChip, Verdict>(nameof(Verdict));

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<VerdictChip, string?>(nameof(Text));

    public static readonly StyledProperty<bool> CompactProperty =
        AvaloniaProperty.Register<VerdictChip, bool>(nameof(Compact));

    static VerdictChip()
    {
        VerdictProperty.Changed.AddClassHandler<VerdictChip>((chip, _) => chip.Rebuild());
        TextProperty.Changed.AddClassHandler<VerdictChip>((chip, _) => chip.Rebuild());
        CompactProperty.Changed.AddClassHandler<VerdictChip>((chip, _) => chip.Rebuild());
    }

    public VerdictChip()
    {
        Classes.Add("verdictChip");
        Rebuild();
    }

    /// <summary>Which of the six meanings this chip shows.</summary>
    public Verdict Verdict
    {
        get => GetValue(VerdictProperty);
        set => SetValue(VerdictProperty, value);
    }

    /// <summary>The view's own word for the meaning; empty shows the glyph alone.</summary>
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

    private TextBlock Words(string text) => _insideButton ? new TextBlock { Text = text } : new CopyableTextBlock { Text = text };

    private void Rebuild()
    {
        foreach (var name in new[] { "agrees", "differs", "new", "noresult", "limit", "several" }) Classes.Remove(name);
        Classes.Add(Verdicts.ClassOf(Verdict));
        Classes.Set("compact", Compact);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var glyph = Words(Verdicts.GlyphOf(Verdict));
        glyph.FontWeight = FontWeight.Bold;
        glyph.IsHitTestVisible = false;
        row.Children.Add(glyph);
        if (Text is { Length: > 0 } text) row.Children.Add(Words(text));
        Child = row;
        AutomationProperties.SetName(this, Text is { Length: > 0 } label ? label : Verdicts.LegendOf(Verdict));
    }
}
