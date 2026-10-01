using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Views;

/// <summary>One kind in a <see cref="TimingKindBar"/>'s legend, with the brush its part is drawn in.</summary>
/// <param name="Row">The kind and its share of the time.</param>
/// <param name="Brush">The brush the bar fills this kind's part with.</param>
public sealed record TimingKindLegendEntry(TimingAggregateRow Row, IBrush? Brush);

/// <summary>
/// Draws the command's kind shares across one full-width bar, and names each part's colour for a legend. Each kind
/// keeps one colour wherever it falls in the bar, so a kind looks the same however the shares sort.
/// </summary>
public sealed class TimingKindBar : Control
{
    public static readonly StyledProperty<IReadOnlyList<TimingAggregateRow>?> RowsProperty =
        AvaloniaProperty.Register<TimingKindBar, IReadOnlyList<TimingAggregateRow>?>(nameof(Rows));
    public static readonly StyledProperty<IBrush?> MorphRuleBrushProperty =
        AvaloniaProperty.Register<TimingKindBar, IBrush?>(nameof(MorphRuleBrush));
    public static readonly StyledProperty<IBrush?> PhonRuleBrushProperty =
        AvaloniaProperty.Register<TimingKindBar, IBrush?>(nameof(PhonRuleBrush));
    public static readonly StyledProperty<IBrush?> LexiconBrushProperty =
        AvaloniaProperty.Register<TimingKindBar, IBrush?>(nameof(LexiconBrush));
    public static readonly StyledProperty<IBrush?> RootLookupBrushProperty =
        AvaloniaProperty.Register<TimingKindBar, IBrush?>(nameof(RootLookupBrush));
    public static readonly StyledProperty<IBrush?> UnattributedBrushProperty =
        AvaloniaProperty.Register<TimingKindBar, IBrush?>(nameof(UnattributedBrush));
    public static readonly DirectProperty<TimingKindBar, IReadOnlyList<TimingKindLegendEntry>> LegendProperty =
        AvaloniaProperty.RegisterDirect<TimingKindBar, IReadOnlyList<TimingKindLegendEntry>>(nameof(Legend), bar => bar.Legend);

    private IReadOnlyList<TimingKindLegendEntry> _legend = [];

    static TimingKindBar() => AffectsRender<TimingKindBar>(RowsProperty, MorphRuleBrushProperty,
        PhonRuleBrushProperty, LexiconBrushProperty, RootLookupBrushProperty, UnattributedBrushProperty);

    public IReadOnlyList<TimingAggregateRow>? Rows
    {
        get => GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    /// <summary>The part for morphological rules.</summary>
    public IBrush? MorphRuleBrush
    {
        get => GetValue(MorphRuleBrushProperty);
        set => SetValue(MorphRuleBrushProperty, value);
    }

    /// <summary>The part for phonological rules.</summary>
    public IBrush? PhonRuleBrush
    {
        get => GetValue(PhonRuleBrushProperty);
        set => SetValue(PhonRuleBrushProperty, value);
    }

    /// <summary>The part for lexical entries.</summary>
    public IBrush? LexiconBrush
    {
        get => GetValue(LexiconBrushProperty);
        set => SetValue(LexiconBrushProperty, value);
    }

    /// <summary>The part for root lookup.</summary>
    public IBrush? RootLookupBrush
    {
        get => GetValue(RootLookupBrushProperty);
        set => SetValue(RootLookupBrushProperty, value);
    }

    /// <summary>The part for time no rule kind's timer accounts for, and for any kind the bar does not name.</summary>
    public IBrush? UnattributedBrush
    {
        get => GetValue(UnattributedBrushProperty);
        set => SetValue(UnattributedBrushProperty, value);
    }

    /// <summary>Each kind the bar draws, in order, with the brush of its part.</summary>
    public IReadOnlyList<TimingKindLegendEntry> Legend
    {
        get => _legend;
        private set => SetAndRaise(LegendProperty, ref _legend, value);
    }

    /// <summary>The brush for <paramref name="kind"/>, as PanGloss's statistics name it.</summary>
    public IBrush? BrushFor(string kind) => kind switch
    {
        "morph_rule" => MorphRuleBrush,
        "phon_rule" => PhonRuleBrush,
        "lex_entry" => LexiconBrush,
        "root_index" => RootLookupBrush,
        _ => UnattributedBrush,
    };

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RowsProperty || change.Property == MorphRuleBrushProperty ||
            change.Property == PhonRuleBrushProperty || change.Property == LexiconBrushProperty ||
            change.Property == RootLookupBrushProperty || change.Property == UnattributedBrushProperty)
            Legend = Rows is { } rows ? [.. rows.Select(row => new TimingKindLegendEntry(row, BrushFor(row.Kind)))] : [];
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Rows is not { Count: > 0 } || Bounds.Width <= 0) return;
        var x = 0d;
        foreach (var row in Rows)
        {
            var width = Math.Max(0, Math.Min(Bounds.Width - x, row.ShareOfTotal * Bounds.Width));
            if (BrushFor(row.Kind) is { } brush)
                context.FillRectangle(brush, new Rect(x, 0, width, Bounds.Height));
            x += width;
        }
    }
}
