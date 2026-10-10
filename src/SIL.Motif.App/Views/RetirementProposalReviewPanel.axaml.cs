using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The linked Proposal, Dry Run and affected-reading evidence shown on Review changes.</summary>
public sealed partial class RetirementProposalReviewPanel : UserControl
{
    public RetirementProposalReviewPanel()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
