using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SIL.Motif.App.Views;

/// <summary>
/// The automatic Parsimony advice for the selected Proposal, shown in the Proposal inspector. Bind a
/// <see cref="ViewModels.ProposalParsimonyViewModel"/> as its data context; it hides until a Proposal is watched.
/// </summary>
public sealed partial class ProposalParsimonyPanel : UserControl
{
    public ProposalParsimonyPanel() => AvaloniaXamlLoader.Load(this);
}
