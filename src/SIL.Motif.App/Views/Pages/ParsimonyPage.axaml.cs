using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// The Parsimony page: the findings of the latest stored Parsimony Report, grouped by measure, with the evidence for
/// the finding selected. It only reads what is stored.
/// </summary>
public sealed partial class ParsimonyPage : UserControl
{
    public ParsimonyPage(ParsimonyPageModel page)
    {
        ArgumentNullException.ThrowIfNull(page);
        DataContext = page;
        AvaloniaXamlLoader.Load(this);
    }
}
