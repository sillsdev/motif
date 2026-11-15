using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views.Words;

/// <summary>
/// The places one word occurs, with a check box for each, the reading to keep, and Approve, Ask Claude and
/// Not a Word. Texts and Words share it; the Why This Analysis fold goes in the diagnostic slot.
/// </summary>
public sealed partial class WordOccurrencesPanel : UserControl
{
    /// <summary>Creates the panel over one word's occurrences and actions.</summary>
    public WordOccurrencesPanel(WordOccurrencesViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Model = model;
        AvaloniaXamlLoader.Load(this);
        DataContext = model;
        Loaded += async (_, _) =>
        {
            if (model.Page.Count == 0 && model.TotalCount > 0) await model.LoadPageAsync(0).ConfigureAwait(true);
        };
    }

    /// <summary>The model the panel shows.</summary>
    public WordOccurrencesViewModel Model { get; }
}
