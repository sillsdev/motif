using System.Text.Json.Nodes;
using SIL.Motif.Host.Installation;
using Xunit;

namespace SIL.Motif.Tests.LibLcm.Installation;

public sealed class MotifAgentPackageStoreTests
{
    [Fact]
    public void RefreshCopiesThePackageRewritesTheServerAndRefreshesConnectedPlugins()
    {
        using var directory = new TemporaryDirectory();
        var bundle = Path.Combine(directory.Path, "bundle");
        var data = Path.Combine(directory.Path, "data");
        var commandPath = Path.Combine(directory.Path, "registered", "motif");
        var clientLifecycle = new FakeClientLifecycle();
        CreateBundle(bundle);
        var store = new MotifAgentPackageStore(bundle, data, commandPath, "/tmp/motif.AppImage", clientLifecycle);

        store.Refresh();
        store.MarkClientConnected("ClaudeCode");
        store.Refresh();
        Assert.Empty(clientLifecycle.Refreshed);
        File.WriteAllText(Path.Combine(bundle, "plugin", "plugin.json"), "{\"version\":\"0.3.0\"}");
        store.Refresh();

        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(store.Paths.PluginRoot, ".mcp.json")))!;
        Assert.Equal(store.Paths.CommandPath, config["mcpServers"]!["motif"]!["command"]!.GetValue<string>());
        Assert.True(File.Exists(store.Paths.PluginZip));
        Assert.Equal("ClaudeCode", Assert.Single(clientLifecycle.Refreshed));
        Assert.Equal("0.3.0", store.PluginVersion);
        if (OperatingSystem.IsLinux())
            Assert.Contains("/tmp/motif.AppImage", File.ReadAllText(store.Paths.CommandPath), StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveKeepsUserDataAndCallsTheClientRemovalSeam()
    {
        using var directory = new TemporaryDirectory();
        var bundle = Path.Combine(directory.Path, "bundle");
        var data = Path.Combine(directory.Path, "data");
        var clientLifecycle = new FakeClientLifecycle();
        CreateBundle(bundle);
        var store = new MotifAgentPackageStore(bundle, data, Path.Combine(directory.Path, "motif"), null, clientLifecycle);
        store.Refresh();
        store.MarkClientConnected("Codex");
        var userData = Path.Combine(data, "project.db");
        File.WriteAllText(userData, "user data");

        store.Remove();

        Assert.False(Directory.Exists(store.Paths.PackageRoot));
        Assert.Equal("user data", File.ReadAllText(userData));
        Assert.Equal("Codex", Assert.Single(clientLifecycle.Removed));
    }

    [Fact]
    public void RemoveRefusesAnUnownedPackageDirectory()
    {
        using var directory = new TemporaryDirectory();
        var data = Path.Combine(directory.Path, "data");
        var package = Path.Combine(data, "agent-packages");
        Directory.CreateDirectory(package);
        var userFile = Path.Combine(package, "user.txt");
        File.WriteAllText(userFile, "keep");
        var store = new MotifAgentPackageStore(Path.Combine(directory.Path, "missing"), data,
            Path.Combine(directory.Path, "motif"), clientLifecycle: new FakeClientLifecycle());

        Assert.Throws<IOException>(store.Remove);
        Assert.Equal("keep", File.ReadAllText(userFile));
    }

    private static void CreateBundle(string root)
    {
        var plugin = Path.Combine(root, "plugin");
        Directory.CreateDirectory(Path.Combine(plugin, ".claude-plugin"));
        Directory.CreateDirectory(Path.Combine(plugin, ".agents", "plugins"));
        Directory.CreateDirectory(Path.Combine(root, ".claude-plugin"));
        File.WriteAllText(Path.Combine(root, ".claude-plugin", "marketplace.json"), "{}");
        File.WriteAllText(Path.Combine(plugin, ".agents", "plugins", "marketplace.json"), "{}");
        File.WriteAllText(Path.Combine(plugin, ".claude-plugin", "plugin.json"), "{}");
        File.WriteAllText(Path.Combine(plugin, "plugin.json"), "{\"version\":\"0.2.0\"}");
        File.WriteAllText(Path.Combine(plugin, ".mcp.json"), ServerConfiguration());
        File.WriteAllText(Path.Combine(plugin, "mcp.json"), ServerConfiguration());
        File.WriteAllText(Path.Combine(root, "motif-plugin.zip"), "zip contents");
    }

    private static string ServerConfiguration() =>
        "{\"mcpServers\":{\"motif\":{\"command\":\"motif\",\"args\":[\"mcp\"]}}}";

    private sealed class FakeClientLifecycle : IMotifAssistantClientLifecycle
    {
        public List<string> Refreshed { get; } = [];
        public List<string> Removed { get; } = [];
        public void Refresh(string client, string marketplaceRoot, string commandPath) => Refreshed.Add(client);
        public void Remove(string client, string marketplaceRoot, string commandPath) => Removed.Add(client);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "motif-agent-package-" + Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
