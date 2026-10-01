using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SIL.Motif.App.Views;

/// <summary>
/// The card a word row opens inside itself in the Matrix, Fix these first and Lists: FieldWorks' analysis and its
/// opinions, every analysis PanGloss built, and any approved analysis it did not build, each morpheme linking to
/// FieldWorks.
/// </summary>
public sealed partial class WordRowCard : UserControl
{
    public WordRowCard() => AvaloniaXamlLoader.Load(this);
}
