using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// The Proposals page: every Proposal in cards grouped by what to do next, each opening Proposal mode or asking
/// for another try.
/// </summary>
public sealed partial class ProposalsPage : UserControl
{
    public ProposalsPage(ProposalsPageModel page)
    {
        ArgumentNullException.ThrowIfNull(page);
        DataContext = page;
        AvaloniaXamlLoader.Load(this);
    }
}
