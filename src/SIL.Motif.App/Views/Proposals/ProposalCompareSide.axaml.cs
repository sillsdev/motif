using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SIL.Motif.App.Views;

/// <summary>Shows one compared Proposal: who built it, what it changes, its proven effects and its Apply path.</summary>
public sealed partial class ProposalCompareSide : UserControl
{
    public ProposalCompareSide() => AvaloniaXamlLoader.Load(this);
}
