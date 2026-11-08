using Avalonia.Controls;
using Avalonia.Data;
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

    internal void ConfigureHandoff(SettingsViewModel settings, AiHandoffPageModel handoff)
    {
        var section = this.FindControl<ContentControl>("HandoffSection")!;
        section.Content = new AiHandoffPage(handoff);
        section.Bind(IsVisibleProperty, new Binding(nameof(settings.ShowHandoffByCopyingFiles)) { Source = settings });
    }
}
