using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SIL.Motif.App.Views;

/// <summary>The Settings sections shown in the workspace side panel.</summary>
public sealed partial class SettingsNavigation : UserControl
{
    public SettingsNavigation() => AvaloniaXamlLoader.Load(this);
}
