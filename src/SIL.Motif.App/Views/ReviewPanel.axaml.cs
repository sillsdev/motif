using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// The Review page: the one list of changes every page adds to, each with a way to take it back, before
/// anything is saved to FieldWorks.
/// </summary>
public sealed partial class ReviewPanel : UserControl
{
    public ReviewPanel(HandoffWorkspaceViewModel workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        Workspace = workspace;
        DataContext = workspace;
        AvaloniaXamlLoader.Load(this);
    }

    public HandoffWorkspaceViewModel Workspace { get; }
}
