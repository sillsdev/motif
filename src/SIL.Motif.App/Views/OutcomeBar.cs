using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// A whole split into parts, such as an Assessment's words by what the parser came to: one bar whose segments
/// are sized by count and coloured by verdict, over a legend giving each part's count and share.
/// </summary>
public sealed class OutcomeBar : StackPanel
{
    public static readonly StyledProperty<IReadOnlyList<OutcomeSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<OutcomeBar, IReadOnlyList<OutcomeSegment>?>(nameof(Segments));
    public static readonly StyledProperty<bool> ShowLegendProperty =
        AvaloniaProperty.Register<OutcomeBar, bool>(nameof(ShowLegend), defaultValue: true);

    static OutcomeBar()
    {
        SegmentsProperty.Changed.AddClassHandler<OutcomeBar>((bar, _) => bar.Rebuild());
        ShowLegendProperty.Changed.AddClassHandler<OutcomeBar>((bar, _) => bar.Rebuild());
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

    private void Rebuild()
    {
        Children.Clear();
        var segments = Segments?.Where(segment => segment.Count > 0).ToList() ?? [];
        if (segments.Count == 0) return;

        var total = segments.Sum(segment => segment.Count);
        var bar = new Grid { Classes = { "outcomeTrack" }, ClipToBounds = true };
        bar.Classes.Set("compact", !ShowLegend);
        var legend = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var segment in segments)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition(segment.Count, GridUnitType.Star));
            var part = new Border { Classes = { "outcomeSegment" } };
            VerdictClasses.SetVerdict(part, segment.Meaning);
            Grid.SetColumn(part, bar.ColumnDefinitions.Count - 1);
            bar.Children.Add(part);

            var share = (double)segment.Count / total;
            var swatch = new Border { Classes = { "outcomeSegment", "swatch" } };
            VerdictClasses.SetVerdict(swatch, segment.Meaning);
            legend.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Classes = { "outcomeLegendEntry" },
                Children =
                {
                    swatch,
                    new CopyableTextBlock { Classes = { "outcomeLegendText" }, Text = segment.CountText, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                    new CopyableTextBlock { Classes = { "outcomeLegendText" }, Text = segment.Label },
                    new CopyableTextBlock { Classes = { "muted" }, Text = share.ToString("P0", CultureInfo.CurrentCulture) },
                },
            });
        }
        Children.Add(bar);
        if (ShowLegend) Children.Add(legend);
    }
}
