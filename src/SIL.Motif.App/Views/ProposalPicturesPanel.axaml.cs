using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SIL.Motif.App.Views;

/// <summary>
/// The pictures on the selected Proposal, shown in the Proposal inspector. Bind a
/// <see cref="ViewModels.ProposalPicturesViewModel"/> as its data context; it hides until a Proposal is shown.
/// </summary>
public sealed partial class ProposalPicturesPanel : UserControl
{
    public ProposalPicturesPanel() => AvaloniaXamlLoader.Load(this);
}
