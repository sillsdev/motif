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
    public WordRowHeader() => AvaloniaXamlLoader.Load(this);

    /// <summary>The warning mark's own glyph, heading the column that counts warnings.</summary>
    public string WarningGlyph => GrammarLevelMarks.Of(GrammarDiagnosticLevel.Warning);
}
