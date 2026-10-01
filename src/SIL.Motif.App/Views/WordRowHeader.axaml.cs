using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Views;

/// <summary>
/// The column heads above a list of <see cref="WordRow"/>s. Placed in the same shared-size scope as the rows, each
/// head sits over its column whatever the rows hold.
/// </summary>
public sealed partial class WordRowHeader : UserControl
{
    private const double CompactHeadingWidth = 1000;

    public static readonly StyledProperty<WordRowColumns> ColumnsProperty =
        AvaloniaProperty.Register<WordRowHeader, WordRowColumns>(nameof(Columns), WordRowColumns.All);

    public static readonly StyledProperty<bool> ShowsMeaningProperty =
        AvaloniaProperty.Register<WordRowHeader, bool>(nameof(ShowsMeaning), true);

    private readonly WordRowLayout? _layout;
    private readonly TextBlock _fieldWorksHeading;
    private readonly TextBlock _panGlossHeading;
    private bool? _compactEngineHeadings;

    public WordRowHeader()
    {
        AvaloniaXamlLoader.Load(this);
        _fieldWorksHeading = this.FindControl<TextBlock>("FieldWorksCell")
            ?? throw new InvalidOperationException("The FieldWorks heading is missing.");
        _panGlossHeading = this.FindControl<TextBlock>("PanGlossCell")
            ?? throw new InvalidOperationException("The PanGloss heading is missing.");
        _layout = new WordRowLayout(this);
        _layout.Apply(Columns, ShowsMeaning);
        SizeChanged += OnSizeChanged;
        SetCompactEngineHeadings(Bounds.Width < CompactHeadingWidth);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) =>
        SetCompactEngineHeadings(e.NewSize.Width < CompactHeadingWidth);

    private void SetCompactEngineHeadings(bool compact)
    {
        if (_compactEngineHeadings == compact) return;
        _compactEngineHeadings = compact;
        _fieldWorksHeading.Text = compact ? "FW" : "FIELDWORKS";
        _panGlossHeading.Text = compact ? "PG" : "PANGLOSS";
    }

    /// <summary>The columns the list's rows show, so each head stays over its column.</summary>
    public WordRowColumns Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ColumnsProperty || change.Property == ShowsMeaningProperty)
            _layout?.Apply(Columns, ShowsMeaning);
    }

    /// <summary>Whether the meaning column's head shows; set it as the rows below set theirs.</summary>
    public bool ShowsMeaning
    {
        get => GetValue(ShowsMeaningProperty);
        set => SetValue(ShowsMeaningProperty, value);
    }

    /// <summary>The warning mark's own glyph, heading the column that counts warnings.</summary>
    public string WarningGlyph => GrammarLevelMarks.Of(GrammarDiagnosticLevel.Warning);
}
