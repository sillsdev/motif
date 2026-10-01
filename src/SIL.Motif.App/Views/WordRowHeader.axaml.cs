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
    public static readonly StyledProperty<bool> ShowsMeaningProperty =
        AvaloniaProperty.Register<WordRowHeader, bool>(nameof(ShowsMeaning), true);

    public WordRowHeader() => AvaloniaXamlLoader.Load(this);

    /// <summary>Whether the meaning column's head shows; set it as the rows below set theirs.</summary>
    public bool ShowsMeaning
    {
        get => GetValue(ShowsMeaningProperty);
        set => SetValue(ShowsMeaningProperty, value);
    }

    /// <summary>The warning mark's own glyph, heading the column that counts warnings.</summary>
    public string WarningGlyph => GrammarLevelMarks.Of(GrammarDiagnosticLevel.Warning);
}
