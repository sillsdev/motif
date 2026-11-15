using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SIL.Motif.App.Views;

/// <summary>
/// The recorded Parsimony advice inside a Proposal's Diagnostics row. Its data context is a
/// <see cref="ViewModels.ProposalParsimonyViewModel"/> for the owning Proposal.
/// </summary>
public sealed partial class ProposalParsimonyDetails : UserControl
{
    public ProposalParsimonyDetails() => AvaloniaXamlLoader.Load(this);
}
