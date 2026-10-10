using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Assistants;
using SIL.Motif.Commands.Preferences;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class AssistantConnectionsPageTests
{
    [Fact]
    public async Task AdvancedAiModePersistsAndOnlyConnectsCheckedAssistantsThroughTheFake()
    {
        var preferences = new FakeAdvancedAiPreferences();
        var connections = new FakeAssistantConnections();
        var page = new AssistantConnectionsPageModel(
            WorkspaceContextTests.NewContext(new FakeCommandClient(), preferences, connections));

        Assert.False(page.IsEnabled);
        Assert.False(page.ShowsAssistantList);
        Assert.Equal(2, page.Assistants.Count);

        page.IsEnabled = true;
        page.Assistants[1].IsSelected = true;
        await page.ConnectSelectedCommand.ExecuteAsync(null);

        Assert.True(preferences.IsEnabled);
        Assert.True(page.ShowsAssistantList);
        Assert.Equal([AssistantClient.Codex], connections.Connected);
        Assert.Contains("Connected Codex", page.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void AssistantDiscoveryIncludesClaudeDesktopWhenInstalled()
    {
        using var directory = new TemporaryDirectory();
        var package = new SIL.Motif.Host.Installation.MotifAgentPackageStore(
            directory.Path, directory.Path, Path.Combine(directory.Path, "motif"));
        var connections = new AssistantConnectionService(package, new FakeDesktopConfiguration(),
            new FakePluginCommands(), claudeDesktopFound: () => true);

        Assert.Contains(connections.FindAssistants(), assistant => assistant.Client == AssistantClient.ClaudeDesktop);
    }

    [Fact]
    public void ClaudeDesktopConfigurationPreservesOtherServersAndRemovesOnlyMotifsEntry()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "claude_desktop_config.json");
        File.WriteAllText(path, "{\"mcpServers\":{\"other\":{\"command\":\"other\"}}}");
        var configuration = new ClaudeDesktopMcpConfiguration(path);

        configuration.Add("/installed/motif");
        configuration.Remove("/different/motif");
        var afterDifferentPath = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal("other", afterDifferentPath["mcpServers"]!["other"]!["command"]!.GetValue<string>());
        Assert.Equal("/installed/motif", afterDifferentPath["mcpServers"]!["motif"]!["command"]!.GetValue<string>());

        configuration.Remove("/installed/motif");
        var afterRemoval = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Null(afterRemoval["mcpServers"]!["motif"]);
        Assert.NotNull(afterRemoval["mcpServers"]!["other"]);
    }

    [Fact]
    public void ClaudeDesktopConfigurationRefusesToReplaceAnotherMotifServer()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "claude_desktop_config.json");
        File.WriteAllText(path, "{\"mcpServers\":{\"motif\":{\"command\":\"other-motif\",\"args\":[]}}}");
        var configuration = new ClaudeDesktopMcpConfiguration(path);

        Assert.Throws<IOException>(() => configuration.Add("/installed/motif"));

        var saved = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal("other-motif", saved["mcpServers"]!["motif"]!["command"]!.GetValue<string>());
    }

    private sealed class FakeAdvancedAiPreferences : IAdvancedAiModePreferenceStore
    {
        public bool IsEnabled { get; private set; }
        public void SetEnabled(bool enabled) => IsEnabled = enabled;
    }

    private sealed class FakeAssistantConnections : IAssistantConnectionService
    {
        public List<AssistantClient> Connected { get; } = [];
        public IReadOnlyList<AssistantInstallation> FindAssistants() =>
            [new(AssistantClient.ClaudeCode, "Claude Code"), new(AssistantClient.Codex, "Codex")];
        public string PluginZipPath => string.Empty;
        public string? VersionWarning => null;
        public Task<AssistantConnectionResult> ConnectAsync(AssistantClient client, CancellationToken cancellationToken = default)
        {
            Connected.Add(client);
            return Task.FromResult(new AssistantConnectionResult($"Connected {client}"));
        }
    }

    private sealed class FakeDesktopConfiguration : IClaudeDesktopMcpConfiguration
    {
        public void Add(string commandPath) { }
        public void Remove(string commandPath) { }
    }

    private sealed class FakePluginCommands : IAssistantPluginCommands
    {
        public bool IsAvailable(AssistantClient client) => false;
        public Task InstallAsync(AssistantClient client, string marketplacePath, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test does not install an assistant plugin.");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
