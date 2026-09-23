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

    static OutcomeBar()
    {
        SegmentsProperty.Changed.AddClassHandler<OutcomeBar>((bar, _) => bar.Rebuild());
    }

    public OutcomeBar()
    {
        Spacing = 8;
    }

    public IReadOnlyList<OutcomeSegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    private void Rebuild()
    {
        Children.Clear();
        var segments = Segments?.Where(segment => segment.Count > 0).ToList() ?? [];
        if (segments.Count == 0) return;

        var total = segments.Sum(segment => segment.Count);
        var bar = new Grid { Height = 14, ColumnSpacing = 2, ClipToBounds = true };
        var legend = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var segment in segments)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition(segment.Count, GridUnitType.Star));
            var part = new Border { Classes = { "outcomeSegment" }, CornerRadius = new CornerRadius(3) };
            VerdictClasses.SetVerdict(part, segment.Meaning);
            Grid.SetColumn(part, bar.ColumnDefinitions.Count - 1);
            bar.Children.Add(part);

            var share = (double)segment.Count / total;
            var swatch = new Border
            {
                Classes = { "outcomeSegment" }, Width = 10, Height = 10, CornerRadius = new CornerRadius(2),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
            };
            VerdictClasses.SetVerdict(swatch, segment.Meaning);
            legend.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 18, 0),
                Children =
                {
                    swatch,
                    new CopyableTextBlock { Text = segment.CountText, FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Thickness(0, 0, 4, 0) },
                    new CopyableTextBlock { Text = segment.Label, Margin = new Thickness(0, 0, 4, 0) },
                    new CopyableTextBlock { Classes = { "muted" }, Text = share.ToString("P0", CultureInfo.CurrentCulture) },
                },
            });
        }
        Children.Add(bar);
        Children.Add(legend);
    }
}
