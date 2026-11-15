using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.Commands.Assistants;

namespace SIL.Motif.App.ViewModels;

/// <summary>One assistant choice in the connection controls.</summary>
/// <param name="installation">The assistant detected by the connection service.</param>
public sealed partial class AssistantChoiceViewModel(AssistantInstallation installation) : ObservableObject
{
    /// <summary>The assistant kind.</summary>
    public AssistantClient Client { get; } = installation.Client;

    /// <summary>The name shown beside the checkbox.</summary>
    public string Name { get; } = installation.Name;

    /// <summary>Whether the person selected this assistant for connection.</summary>
    [ObservableProperty]
    private bool _isSelected;
}
