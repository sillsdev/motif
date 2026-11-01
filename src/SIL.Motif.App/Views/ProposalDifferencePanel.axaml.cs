using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SIL.Motif.App.Views;

/// <summary>
/// The saved Difference of the selected Proposal, shown in the Proposal inspector. Bind a
/// <see cref="ViewModels.ProposalDifferenceViewModel"/> as its data context; while no Difference is saved it hides.
/// </summary>
public sealed partial class ProposalDifferencePanel : UserControl
{
    public ProposalDifferencePanel()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
