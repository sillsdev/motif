using Avalonia.Controls;

namespace SIL.Motif.App.Views;

/// <summary>
/// A word's two buttons: its opinion mark, which opens tiles for what FieldWorks holds, and the add count, which lists
/// the PanGloss readings FieldWorks does not hold. Both menus are built only when opened. It is a panel, not a
/// user control, so it adds no layers to the word strip it sits in.
/// </summary>
public partial class WordDispositionButtons : StackPanel
{
    /// <summary>The accessible name of the opinion mark button, which opens the tiles.</summary>
    public const string MarkButtonName = "Change what FieldWorks holds for this word";

    /// <summary>The accessible name of the add button, which lists PanGloss analyses to add.</summary>
    public const string AddButtonName = "Add a PanGloss analysis to FieldWorks";

    /// <summary>Creates the buttons; their data context is the word's <c>ResultsTokenViewModel</c>.</summary>
    public WordDispositionButtons() => InitializeComponent();
}
