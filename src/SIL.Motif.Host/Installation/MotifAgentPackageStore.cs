using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace SIL.Motif.Host.Installation;

/// <summary>Prepares the installed skill package and its assistant launch paths for the current user.</summary>
public sealed class MotifAgentPackageStore
{
    private const string OwnershipMarker = "Motif owns this directory.\n";
    private readonly string _bundleRoot;
    private readonly string _dataRoot;
    private readonly string _cliPath;
    private readonly string? _appImagePath;
    private readonly IMotifAssistantClientLifecycle _clientLifecycle;

    /// <summary>Creates a package store from a product payload and a per-user data directory.</summary>
    public MotifAgentPackageStore(
        string bundleRoot, string dataRoot, string cliPath, string? appImagePath = null,
        IMotifAssistantClientLifecycle? clientLifecycle = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(cliPath);
        _bundleRoot = Path.GetFullPath(bundleRoot);
        _dataRoot = Path.GetFullPath(dataRoot);
        _cliPath = Path.GetFullPath(cliPath);
        _appImagePath = string.IsNullOrWhiteSpace(appImagePath) ? null : Path.GetFullPath(appImagePath);
        _clientLifecycle = clientLifecycle ?? new MotifAssistantClientLifecycle();
    }

    /// <summary>The package copy, plugin ZIP and registered Motif command for this user.</summary>
    public MotifAgentPackagePaths Paths => new(
        Path.Combine(_dataRoot, "agent-packages"),
        Path.Combine(_dataRoot, "agent-packages", "plugin"),
        Path.Combine(_dataRoot, "agent-packages", "motif-plugin.zip"),
        OperatingSystem.IsLinux() ? Path.Combine(_dataRoot, "bin", "motif") : _cliPath);

    /// <summary>Whether the package and its two client marketplace catalogs are present beside Motif.</summary>
    public bool IsBundled =>
        File.Exists(Path.Combine(_bundleRoot, "plugin", "plugin.json")) &&
        File.Exists(Path.Combine(_bundleRoot, "plugin", "mcp.json")) &&
        File.Exists(Path.Combine(_bundleRoot, "plugin", ".mcp.json")) &&
        File.Exists(Path.Combine(_bundleRoot, "plugin", ".agents", "plugins", "marketplace.json")) &&
        File.Exists(Path.Combine(_bundleRoot, ".claude-plugin", "marketplace.json")) &&
        File.Exists(Path.Combine(_bundleRoot, "motif-plugin.zip"));

    /// <summary>The version in the bundled plugin manifest, or an empty string when the bundle is incomplete.</summary>
    public string PluginVersion
    {
        get
        {
            var path = Path.Combine(Paths.PluginRoot, "plugin.json");
            if (!File.Exists(path)) return string.Empty;
            return JsonNode.Parse(File.ReadAllText(path))?["version"]?.GetValue<string>() ?? string.Empty;
        }
    }

    /// <summary>Copies the release package to its stable per-user location and rewrites its server command.</summary>
    /// <param name="refreshConnectedClients">Whether to run the connected clients' plugin update commands.</param>
    public void Refresh(bool refreshConnectedClients = false)
    {
        if (!IsBundled) return;
        var paths = Paths;
        var previousPluginVersion = PluginVersion;
        var connectedClients = ConnectedClients();
        var bundledPluginVersion = ReadPluginVersion(Path.Combine(_bundleRoot, "plugin", "plugin.json"));
        var shouldRefreshClients = refreshConnectedClients ||
            connectedClients.Length > 0 && !string.Equals(previousPluginVersion, bundledPluginVersion, StringComparison.Ordinal);
        EnsureOwnedDirectory(paths.PackageRoot);
        var staging = paths.PackageRoot + ".staging-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        CopyDirectory(Path.Combine(_bundleRoot, "plugin"), Path.Combine(staging, "plugin"));
        CopyDirectory(Path.Combine(_bundleRoot, ".claude-plugin"), Path.Combine(staging, ".claude-plugin"));
        CopyFile(Path.Combine(_bundleRoot, "motif-plugin.zip"), Path.Combine(staging, "motif-plugin.zip"));
        File.WriteAllText(Path.Combine(staging, ".motif-owned"), OwnershipMarker);
        RewriteServerCommand(Path.Combine(staging, "plugin", ".mcp.json"), paths.CommandPath);
        RewriteServerCommand(Path.Combine(staging, "plugin", "mcp.json"), paths.CommandPath);

        if (Directory.Exists(paths.PackageRoot)) Directory.Delete(paths.PackageRoot, recursive: true);
        Directory.Move(staging, paths.PackageRoot);
        if (OperatingSystem.IsLinux()) WriteLinuxLauncher(paths.CommandPath);
        if (shouldRefreshClients)
            foreach (var client in connectedClients)
                _clientLifecycle.Refresh(client, paths.PackageRoot, paths.CommandPath);
    }

    /// <summary>Removes only the Motif-owned package copy and Linux launcher.</summary>
    public void Remove()
    {
        var paths = Paths;
        if (Directory.Exists(paths.PackageRoot))
        {
            var marker = Path.Combine(paths.PackageRoot, ".motif-owned");
            if (!File.Exists(marker) || File.ReadAllText(marker) != OwnershipMarker)
                throw new IOException("The assistant package directory is not owned by Motif.");
        }
        foreach (var client in ConnectedClients())
            _clientLifecycle.Remove(client, paths.PackageRoot, paths.CommandPath);
        if (Directory.Exists(paths.PackageRoot))
        {
            Directory.Delete(paths.PackageRoot, recursive: true);
        }
        var clientsPath = Path.Combine(_dataRoot, "assistant-clients.json");
        if (File.Exists(clientsPath)) File.Delete(clientsPath);
        if (OperatingSystem.IsLinux()) RemoveLinuxLauncher(paths.CommandPath);
    }

    /// <summary>Records an assistant after its Motif connection has completed.</summary>
    public void MarkClientConnected(string client)
    {
        if (client is not ("ClaudeDesktop" or "ClaudeCode" or "Codex"))
            throw new ArgumentException("Unknown assistant client.", nameof(client));
        var clients = ConnectedClients().Append(client).Distinct(StringComparer.Ordinal).Order().ToArray();
        var path = Path.Combine(_dataRoot, "assistant-clients.json");
        Directory.CreateDirectory(_dataRoot);
        var temporary = path + ".motif-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new AssistantClients(1, clients),
                new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    /// <summary>Builds the store using the running package and the operating system's per-user data location.</summary>
    public static MotifAgentPackageStore ForInstallation()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dataRoot = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SIL", "Motif")
            : Path.Combine(home, ".local", "share", "SIL", "Motif");
        var suffix = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        var cliPath = Path.Combine(AppContext.BaseDirectory, "motif" + suffix);
        var appImage = OperatingSystem.IsLinux() ? Environment.GetEnvironmentVariable("APPIMAGE") : null;
        return new MotifAgentPackageStore(AppContext.BaseDirectory, dataRoot, cliPath, appImage);
    }

    private void EnsureOwnedDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            var marker = Path.Combine(path, ".motif-owned");
            if (!File.Exists(marker) || File.ReadAllText(marker) != OwnershipMarker)
                throw new IOException("The assistant package directory is not owned by Motif.");
            return;
        }
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, ".motif-owned"), OwnershipMarker);
    }

    private string[] ConnectedClients()
    {
        var path = Path.Combine(_dataRoot, "assistant-clients.json");
        if (!File.Exists(path)) return [];
        var saved = JsonSerializer.Deserialize<AssistantClients>(File.ReadAllText(path))
            ?? throw new InvalidDataException("The assistant connection record is empty.");
        if (saved.SchemaVersion != 1 || saved.Clients.Any(client => client is not ("ClaudeDesktop" or "ClaudeCode" or "Codex")))
            throw new InvalidDataException("The assistant connection record has an unsupported shape.");
        return saved.Clients;
    }

    private static void RewriteServerCommand(string path, string commandPath)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new InvalidDataException($"The MCP configuration is invalid: {path}");
        var server = root["mcpServers"]?["motif"]?.AsObject()
            ?? throw new InvalidDataException($"The Motif server is missing from {path}.");
        server["command"] = commandPath;
        File.WriteAllText(path, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }

    private void WriteLinuxLauncher(string path)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("The Motif launcher is available only on Linux.");
        EnsureOwnedDirectory(Path.GetDirectoryName(path)!);
        var target = _appImagePath is null
            ? "exec " + ShellQuote(_cliPath) + " \"$@\"\n"
            : "if [ ! -f " + ShellQuote(_appImagePath) + " ]; then\n" +
              "  printf '%s\\n' 'Motif is not installed.' >&2\n  exit 127\nfi\n" +
              "exec " + ShellQuote(_appImagePath) + " --appimage-extract-and-run --cli \"$@\"\n";
        File.WriteAllText(path, "#!/bin/sh\n" + target);
        File.SetUnixFileMode(path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, ".motif-launcher.sha256"), HashFile(path));
    }

    private static void RemoveLinuxLauncher(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var marker = Path.Combine(directory, ".motif-owned");
        var hashFile = Path.Combine(directory, ".motif-launcher.sha256");
        if (!File.Exists(marker) || File.ReadAllText(marker) != OwnershipMarker ||
            !File.Exists(hashFile) || !File.Exists(path)) return;
        if (!string.Equals(File.ReadAllText(hashFile), HashFile(path), StringComparison.Ordinal)) return;
        File.Delete(path);
        File.Delete(hashFile);
        File.Delete(marker);
        if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }

    private static string HashFile(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static string ReadPluginVersion(string path) =>
        JsonNode.Parse(File.ReadAllText(path))?["version"]?.GetValue<string>() ?? string.Empty;

    private sealed record AssistantClients(int SchemaVersion, string[] Clients);

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static void CopyFile(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}

/// <summary>Paths the window uses to connect assistant clients to Motif.</summary>
/// <param name="PackageRoot">The Motif-owned copy of its local marketplaces and plugin ZIP.</param>
/// <param name="PluginRoot">The plugin directory named by each local marketplace.</param>
/// <param name="PluginZip">The ZIP Claude Desktop offers for upload.</param>
/// <param name="CommandPath">The registered Motif executable or stable Linux launcher.</param>
public sealed record MotifAgentPackagePaths(string PackageRoot, string PluginRoot, string PluginZip, string CommandPath);
