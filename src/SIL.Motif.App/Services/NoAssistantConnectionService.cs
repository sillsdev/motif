using SIL.Motif.Commands.Assistants;

namespace SIL.Motif.App.Services;

internal sealed class NoAssistantConnectionService : IAssistantConnectionService
{
    internal static NoAssistantConnectionService Instance { get; } = new();

    public IReadOnlyList<AssistantInstallation> FindAssistants() => [];
    public string PluginZipPath => string.Empty;
    public string? VersionWarning => null;
    public Task<AssistantConnectionResult> ConnectAsync(AssistantClient client, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("This window was composed without assistant connections.");
}
