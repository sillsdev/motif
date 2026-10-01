using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// A whole split into parts, such as an Assessment's words by what the parser came to: one bar whose segments
/// are sized by count and coloured by verdict, over a legend giving each colour once with its parts' counts and,
/// unless the legend is only a key, their shares.
/// </summary>
public sealed class OutcomeBar : StackPanel
{
    public static readonly StyledProperty<IReadOnlyList<OutcomeSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<OutcomeBar, IReadOnlyList<OutcomeSegment>?>(nameof(Segments));
    public static readonly StyledProperty<bool> ShowLegendProperty =
        AvaloniaProperty.Register<OutcomeBar, bool>(nameof(ShowLegend), defaultValue: true);
    public static readonly StyledProperty<bool> ShowSharesProperty =
        AvaloniaProperty.Register<OutcomeBar, bool>(nameof(ShowShares), defaultValue: true);

    static OutcomeBar()
    {
        SegmentsProperty.Changed.AddClassHandler<OutcomeBar>((bar, _) => bar.Rebuild());
        ShowLegendProperty.Changed.AddClassHandler<OutcomeBar>((bar, _) => bar.Rebuild());
        ShowSharesProperty.Changed.AddClassHandler<OutcomeBar>((bar, _) => bar.Rebuild());
    }

    public OutcomeBar()
    {
        Classes.Add("outcomeBar");
    }

    public IReadOnlyList<OutcomeSegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    /// <summary>Whether the bar also shows each segment's count and share.</summary>
    public bool ShowLegend
    {
        get => GetValue(ShowLegendProperty);
        set => SetValue(ShowLegendProperty, value);
    }

    /// <summary>
    /// Whether the legend gives each segment's share as well as its count. Without shares the legend is a key
    /// under a compact bar, as on an Overview tile.
    /// </summary>
    public bool ShowShares
    {
        get => GetValue(ShowSharesProperty);
        set => SetValue(ShowSharesProperty, value);
    }

    // Inside a button the button owns the pointer, so the legend's words are plain there and selectable elsewhere.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var insideButton = this.FindAncestorOfType<Button>() is not null;
        if (insideButton == _insideButton) return;
        _insideButton = insideButton;
        Rebuild();
    }

    private bool _insideButton;

    private TextBlock Words(string text, string textClass) => _insideButton
        ? new TextBlock { Text = text, Classes = { textClass } }
        : new CopyableTextBlock { Text = text, Classes = { textClass } };

    private void Rebuild()
    {
        Children.Clear();
        var segments = Segments?.Where(segment => segment.Count > 0).ToList() ?? [];
        if (segments.Count == 0) return;

        var total = segments.Sum(segment => segment.Count);
        var bar = new Grid { Classes = { "outcomeTrack" }, ClipToBounds = true };
        bar.Classes.Set("compact", !ShowLegend || !ShowShares);
        var legend = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var segment in segments)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition(segment.Count, GridUnitType.Star));
            var part = new Border { Classes = { "outcomeSegment" } };
            MarkClasses.SetMark(part, segment.Mark);
            Grid.SetColumn(part, bar.ColumnDefinitions.Count - 1);
            bar.Children.Add(part);
        }

        // Parts that share a colour share one swatch, so the key names each colour once.
        foreach (var colour in segments.GroupBy(segment => segment.Mark))
        {
            var swatch = new Border { Classes = { "outcomeSegment", "swatch" } };
            MarkClasses.SetMark(swatch, colour.Key);
            var entry = new StackPanel { Orientation = Orientation.Horizontal, Classes = { "outcomeLegendEntry" } };
            entry.Children.Add(swatch);
            var parts = colour.ToList();
            for (var index = 0; index < parts.Count; index++)
            {
                var segment = parts[index];
                var separator = index < parts.Count - 1 ? "," : string.Empty;
                var count = Words(segment.CountText, "outcomeLegendText");
                count.FontWeight = Avalonia.Media.FontWeight.SemiBold;
                entry.Children.Add(count);
                if (!ShowShares)
                {
                    entry.Children.Add(Words(segment.Label + separator, "outcomeLegendText"));
                    continue;
                }
                entry.Children.Add(Words(segment.Label, "outcomeLegendText"));
                var share = ((double)segment.Count / total).ToString("P0", CultureInfo.CurrentCulture);
                entry.Children.Add(Words(share + separator, "muted"));
            }
            legend.Children.Add(entry);
        }
        Children.Add(bar);
        if (ShowLegend) Children.Add(legend);
    }
}
