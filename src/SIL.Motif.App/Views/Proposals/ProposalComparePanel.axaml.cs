using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>Sets two checked Proposals side by side, or says why they cannot be compared.</summary>
public sealed partial class ProposalComparePanel : UserControl
{
    public ProposalComparePanel() => AvaloniaXamlLoader.Load(this);

    /// <summary>Creates the panel over a comparison.</summary>
    public ProposalComparePanel(ProposalCompareViewModel comparison) : this() => DataContext = comparison;
}
