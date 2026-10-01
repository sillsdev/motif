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
    public static readonly StyledProperty<WordRowColumns> ColumnsProperty =
        AvaloniaProperty.Register<WordRowHeader, WordRowColumns>(nameof(Columns), WordRowColumns.All);

    public static readonly StyledProperty<bool> ShowsMeaningProperty =
        AvaloniaProperty.Register<WordRowHeader, bool>(nameof(ShowsMeaning), true);

    private readonly WordRowLayout? _layout;

    public WordRowHeader()
    {
        AvaloniaXamlLoader.Load(this);
        _layout = new WordRowLayout(this);
        _layout.Apply(Columns, ShowsMeaning);
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
