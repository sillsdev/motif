using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The page that enables Advanced AI mode and connects supported assistants.</summary>
public sealed partial class AssistantConnectionsPage : UserControl
{
    /// <summary>Builds the page for the current window's assistant choices.</summary>
    public AssistantConnectionsPage(AssistantConnectionsPageModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        DataContext = model;
        AvaloniaXamlLoader.Load(this);
    }
}
