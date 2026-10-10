using System.Diagnostics;
using System.Text.Json.Nodes;
using SIL.Motif.Host;
using SIL.Motif.Host.Installation;

namespace SIL.Motif.Commands.Assistants;

/// <summary>A supported assistant that can connect to Motif's local MCP server.</summary>
public enum AssistantClient
{
    /// <summary>Claude Desktop on Windows.</summary>
    ClaudeDesktop,

    /// <summary>Claude Code command line.</summary>
    ClaudeCode,

    /// <summary>Codex command line.</summary>
    Codex,
}

/// <summary>An assistant installation found on the current computer.</summary>
/// <param name="Client">The assistant kind.</param>
/// <param name="Name">The name shown in the window.</param>
public sealed record AssistantInstallation(AssistantClient Client, string Name);

/// <summary>The setup instructions returned after one assistant is connected.</summary>
/// <param name="Message">What the person should do next.</param>
public sealed record AssistantConnectionResult(string Message);

/// <summary>Finds supported assistants and connects them through their documented local setup.</summary>
public interface IAssistantConnectionService
{
    /// <summary>The supported assistants detected on this computer.</summary>
    IReadOnlyList<AssistantInstallation> FindAssistants();

    /// <summary>The bundled skill ZIP path for a Claude Desktop upload.</summary>
    string PluginZipPath { get; }

    /// <summary>A warning when the skill package and running Motif have different versions.</summary>
    string? VersionWarning { get; }

    /// <summary>Registers the selected assistant and returns its next setup step.</summary>
    Task<AssistantConnectionResult> ConnectAsync(AssistantClient client, CancellationToken cancellationToken = default);
}

/// <summary>Writes or removes Motif's one MCP entry in Claude Desktop's configuration.</summary>
public interface IClaudeDesktopMcpConfiguration
{
    /// <summary>Adds Motif's server while preserving other MCP entries.</summary>
    void Add(string commandPath);

    /// <summary>Removes Motif's entry only when it still matches the registered command.</summary>
    void Remove(string commandPath);
}

/// <summary>Runs the assistant CLI commands that install Motif from its local marketplace.</summary>
public interface IAssistantPluginCommands
{
    /// <summary>Whether the selected assistant command is available.</summary>
    bool IsAvailable(AssistantClient client);

    /// <summary>Adds the Motif marketplace and installs its plugin.</summary>
    Task InstallAsync(AssistantClient client, string marketplacePath, CancellationToken cancellationToken = default);
}

/// <summary>The installed window's assistant discovery and connection service.</summary>
public sealed class AssistantConnectionService : IAssistantConnectionService
{
    private readonly MotifAgentPackageStore _package;
    private readonly IClaudeDesktopMcpConfiguration _desktopConfiguration;
    private readonly IAssistantPluginCommands _pluginCommands;
    private readonly Func<bool> _claudeDesktopFound;

    /// <summary>Creates a connection service from replaceable configuration and CLI adapters.</summary>
    public AssistantConnectionService(
        MotifAgentPackageStore package,
        IClaudeDesktopMcpConfiguration desktopConfiguration,
        IAssistantPluginCommands pluginCommands,
        Func<bool>? claudeDesktopFound = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(desktopConfiguration);
        ArgumentNullException.ThrowIfNull(pluginCommands);
        _package = package;
        _desktopConfiguration = desktopConfiguration;
        _pluginCommands = pluginCommands;
        _claudeDesktopFound = claudeDesktopFound ?? ClaudeDesktopIsInstalled;
    }

    /// <inheritdoc />
    public IReadOnlyList<AssistantInstallation> FindAssistants()
    {
        var found = new List<AssistantInstallation>();
        if (_claudeDesktopFound())
            found.Add(new(AssistantClient.ClaudeDesktop, "Claude Desktop"));
        if (_pluginCommands.IsAvailable(AssistantClient.ClaudeCode))
            found.Add(new(AssistantClient.ClaudeCode, "Claude Code"));
        if (_pluginCommands.IsAvailable(AssistantClient.Codex))
            found.Add(new(AssistantClient.Codex, "Codex"));
        return found;
    }

    /// <inheritdoc />
    public string PluginZipPath => _package.Paths.PluginZip;

    /// <inheritdoc />
    public string? VersionWarning => _package.PluginVersion.Length == 0 ||
        string.Equals(_package.PluginVersion, MotifProductVersion.CurrentText, StringComparison.Ordinal)
            ? null
            : $"Motif is {MotifProductVersion.CurrentText}, while its agent skills are {_package.PluginVersion}. Restart Motif or reinstall the plugin.";

    /// <inheritdoc />
    public async Task<AssistantConnectionResult> ConnectAsync(
        AssistantClient client, CancellationToken cancellationToken = default)
    {
        if (!_package.IsBundled)
            throw new InvalidOperationException("The Motif agent package is missing from this installation.");
        _package.Refresh(refreshConnectedClients: true);
        switch (client)
        {
            case AssistantClient.ClaudeDesktop:
                _desktopConfiguration.Add(_package.Paths.CommandPath);
                _package.MarkClientConnected(client.ToString());
                return new("Motif is in Claude Desktop's MCP configuration. Upload the Motif plugin ZIP from " +
                    PluginZipPath + " in Customize → Plugins, then restart Claude Desktop. Upload the latest ZIP " +
                    "again after a Motif update to refresh the skills in your Claude account.");
            case AssistantClient.ClaudeCode:
            case AssistantClient.Codex:
                if (!_pluginCommands.IsAvailable(client))
                    throw new InvalidOperationException($"The {client} command is not available on PATH.");
                var marketplacePath = client == AssistantClient.ClaudeCode
                    ? _package.Paths.PackageRoot
                    : _package.Paths.PluginRoot;
                await _pluginCommands.InstallAsync(client, marketplacePath, cancellationToken).ConfigureAwait(false);
                _package.MarkClientConnected(client.ToString());
                return new($"Motif is installed for {client}. Start a new {client} session to load its skills and tools.");
            default:
                throw new ArgumentOutOfRangeException(nameof(client), client, "Unknown assistant client.");
        }
    }

    /// <summary>Builds the installed service from Motif's bundled package and per-user configuration locations.</summary>
    public static AssistantConnectionService ForInstallation()
    {
        var package = MotifAgentPackageStore.ForInstallation();
        return new AssistantConnectionService(package,
            new ClaudeDesktopMcpConfiguration(), new AssistantPluginCommands());
    }

    private static bool ClaudeDesktopIsInstalled()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var configPath = ClaudeDesktopConfigPath();
        if (File.Exists(configPath)) return true;
        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return File.Exists(Path.Combine(local, "Programs", "Claude", "Claude.exe"));
        }
        if (OperatingSystem.IsMacOS())
            return Directory.Exists("/Applications/Claude.app") ||
                Directory.Exists(Path.Combine(home, "Applications", "Claude.app"));
        return IsOnPath("claude-desktop");
    }

    private static string ClaudeDesktopConfigPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Claude", "claude_desktop_config.json");
        if (OperatingSystem.IsMacOS())
            return Path.Combine(home, "Library", "Application Support", "Claude", "claude_desktop_config.json");
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var configRoot = string.IsNullOrWhiteSpace(xdg) || !Path.IsPathRooted(xdg)
            ? Path.Combine(home, ".config")
            : Path.GetFullPath(xdg);
        return Path.Combine(configRoot, "Claude", "claude_desktop_config.json");
    }

    private static bool IsOnPath(string name) => (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Any(directory => File.Exists(Path.Combine(directory, name)));
}

internal sealed class ClaudeDesktopMcpConfiguration : IClaudeDesktopMcpConfiguration
{
    private readonly string _configPath;

    internal ClaudeDesktopMcpConfiguration() : this(InstalledConfigPath())
    {
    }

    internal ClaudeDesktopMcpConfiguration(string configPath) => _configPath = Path.GetFullPath(configPath);

    private static string InstalledConfigPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Claude", "claude_desktop_config.json");
        if (OperatingSystem.IsMacOS())
            return Path.Combine(home, "Library", "Application Support", "Claude", "claude_desktop_config.json");
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var configRoot = string.IsNullOrWhiteSpace(xdg) || !Path.IsPathRooted(xdg)
            ? Path.Combine(home, ".config")
            : Path.GetFullPath(xdg);
        return Path.Combine(configRoot, "Claude", "claude_desktop_config.json");
    }

    public void Add(string commandPath)
    {
        var root = ReadConfig();
        var servers = root["mcpServers"] switch
        {
            JsonObject configured => configured,
            null => new JsonObject(),
            _ => throw new InvalidDataException("Claude Desktop's mcpServers value is not an object."),
        };
        if (root["mcpServers"] is null) root["mcpServers"] = servers;
        if (servers["motif"] is { } existingServer &&
            (existingServer is not JsonObject current ||
             !string.Equals(current["command"]?.GetValue<string>(), commandPath, StringComparison.OrdinalIgnoreCase) ||
             current["args"]?.AsArray().Select(value => value?.GetValue<string>()).ToArray()
                 .SequenceEqual(new string?[] { "mcp" }) != true))
            throw new IOException("Claude Desktop already has a different MCP server named motif.");
        servers["motif"] = new JsonObject
        {
            ["command"] = commandPath,
            ["args"] = new JsonArray(JsonValue.Create("mcp")),
        };
        WriteConfig(root);
    }

    public void Remove(string commandPath)
    {
        var root = ReadConfig();
        var servers = root["mcpServers"] as JsonObject;
        if (servers?["motif"] is not JsonObject current ||
            !string.Equals(current["command"]?.GetValue<string>(), commandPath, StringComparison.OrdinalIgnoreCase) ||
            current["args"]?.AsArray().Select(value => value?.GetValue<string>()).ToArray()
                .SequenceEqual(new string?[] { "mcp" }) != true)
            return;
        servers.Remove("motif");
        WriteConfig(root);
    }

    private JsonObject ReadConfig()
    {
        if (!File.Exists(_configPath)) return new JsonObject();
        return JsonNode.Parse(File.ReadAllText(_configPath))?.AsObject()
            ?? throw new InvalidDataException("Claude Desktop's MCP configuration is not a JSON object.");
    }

    private void WriteConfig(JsonObject root)
    {
        var path = _configPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".motif-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

internal sealed class AssistantPluginCommands : IAssistantPluginCommands
{
    public bool IsAvailable(AssistantClient client) => FindCommand(client) is not null;

    public async Task InstallAsync(AssistantClient client, string marketplacePath, CancellationToken cancellationToken = default)
    {
        var command = FindCommand(client) ?? throw new InvalidOperationException($"The {client} command is not available on PATH.");
        var steps = client switch
        {
            AssistantClient.ClaudeCode => new[]
            {
                new[] { "plugin", "marketplace", "add", marketplacePath, "--scope", "user" },
                new[] { "plugin", "install", "motif@motif", "--scope", "user" },
            },
            AssistantClient.Codex => new[]
            {
                new[] { "plugin", "marketplace", "add", marketplacePath },
                new[] { "plugin", "add", "motif@motif" },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(client), client, "This assistant does not install plugins by CLI."),
        };
        foreach (var arguments in steps)
            await RunAsync(command, arguments, cancellationToken).ConfigureAwait(false);
    }

    private static string? FindCommand(AssistantClient client)
    {
        var name = client == AssistantClient.ClaudeCode ? "claude" : client == AssistantClient.Codex ? "codex" : null;
        if (name is null) return null;
        var fileName = OperatingSystem.IsWindows() ? name + ".exe" : name;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static async Task RunAsync(string command, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(command) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.RedirectStandardInput = true;
        using var process = Process.Start(start) ?? throw new IOException($"Could not start {Path.GetFileName(command)}.");
        process.StandardInput.Close();
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        var output = await standardOutput.ConfigureAwait(false);
        var error = await standardError.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new IOException($"{Path.GetFileName(command)} exited with {process.ExitCode}: {error}{output}");
    }
}
