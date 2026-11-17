using Avalonia.Controls;

namespace SIL.Motif.App.Views;

/// <summary>
/// A word's two buttons: its opinion mark, which opens tiles for what FieldWorks holds, and the readings count, which opens
/// the word panel where a reading is chosen. The opinion menu is built only when opened. It is a panel, not a
/// user control, so it adds no layers to the word strip it sits in.
/// </summary>
public partial class WordDispositionButtons : StackPanel
{
    /// <summary>The accessible name of the opinion mark button, which opens the tiles.</summary>
    public static string MarkButtonName => MotifAppStrings.SetAnalysis;

    /// <summary>The accessible name of the readings count, which opens the word panel.</summary>
    public static string ReadingsButtonName => MotifAppStrings.WordReadingsButtonName;

    /// <summary>Creates the buttons; their data context is the word's <c>ResultsTokenViewModel</c>.</summary>
    public WordDispositionButtons() => InitializeComponent();
}
