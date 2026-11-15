using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SIL.Motif.App.Views;

/// <summary>The human confirmation and recorded outcome for one Proposal's Apply.</summary>
public sealed partial class ProposalApplyPanel : UserControl
{
    public ProposalApplyPanel() => AvaloniaXamlLoader.Load(this);
}
