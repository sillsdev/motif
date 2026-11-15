using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SIL.Motif.App.Views.Settings;

/// <summary>Client setup and explicit connection actions hosted by Settings.</summary>
public sealed partial class AssistantConnectionsPanel : UserControl
{
    public AssistantConnectionsPanel() => AvaloniaXamlLoader.Load(this);
}
