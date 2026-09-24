using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// The Warnings page: the grammar's findings as the grammar panel shows them, with an offer to check the
/// grammar when nothing has been checked for this Baseline yet.
/// </summary>
public sealed partial class WarningsPage : UserControl
{
    public WarningsPage(HandoffWorkspaceViewModel workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        DataContext = workspace;
        AvaloniaXamlLoader.Load(this);
        this.FindControl<ContentControl>("GrammarHost")!.Content = new GrammarPanel(workspace.Grammar);
    }
}
