using System.Diagnostics;
using System.Text.Json.Nodes;

namespace SIL.Motif.Host.Installation;

/// <summary>Client operations the install lifecycle can refresh or remove without knowing client settings paths.</summary>
public interface IMotifAssistantClientLifecycle
{
    /// <summary>Refreshes the installed Motif plugin in one assistant after its package source changes.</summary>
    void Refresh(string client, string marketplaceRoot, string commandPath);

    /// <summary>Removes the Motif plugin and configuration that were added for one assistant.</summary>
    void Remove(string client, string marketplaceRoot, string commandPath);
}

internal sealed class MotifAssistantClientLifecycle : IMotifAssistantClientLifecycle
{
    public void Refresh(string client, string marketplaceRoot, string commandPath)
    {
        try
        {
            if (client == "ClaudeCode")
                Run("claude", ["plugin", "update", "motif@motif", "--scope", "user"]);
            else if (client == "Codex")
            {
                Run("codex", ["plugin", "marketplace", "upgrade", "motif"]);
                Run("codex", ["plugin", "add", "motif@motif"]);
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or
            System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"Motif could not refresh the {client} plugin: {exception.Message}");
        }
    }

    public void Remove(string client, string marketplaceRoot, string commandPath)
    {
        try
        {
            if (client == "ClaudeDesktop") RemoveClaudeDesktop(commandPath);
            else if (client == "ClaudeCode")
            {
                Run("claude", ["plugin", "uninstall", "motif@motif", "--scope", "user"]);
                Run("claude", ["plugin", "marketplace", "remove", "motif", "--scope", "user"]);
            }
            else if (client == "Codex")
            {
                Run("codex", ["plugin", "remove", "motif@motif"]);
                Run("codex", ["plugin", "marketplace", "remove", "motif"]);
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or
            System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"Motif could not remove the {client} plugin: {exception.Message}");
        }
    }

    private static void RemoveClaudeDesktop(string commandPath)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var path = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Claude", "claude_desktop_config.json")
            : OperatingSystem.IsMacOS()
                ? Path.Combine(home, "Library", "Application Support", "Claude", "claude_desktop_config.json")
                : Path.Combine(
                    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"))
                        ? Path.Combine(home, ".config")
                        : Path.IsPathRooted(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")!)
                            ? Path.GetFullPath(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")!)
                            : Path.Combine(home, ".config"),
                    "Claude", "claude_desktop_config.json");
        if (!File.Exists(path)) return;
        var root = JsonNode.Parse(File.ReadAllText(path))?.AsObject();
        var server = root?["mcpServers"]?["motif"]?.AsObject();
        var arguments = server?["args"]?.AsArray().Select(value => value?.GetValue<string>()).ToArray();
        if (!string.Equals(server?["command"]?.GetValue<string>(), commandPath, StringComparison.OrdinalIgnoreCase) ||
            arguments?.SequenceEqual(new string?[] { "mcp" }) != true) return;
        root!["mcpServers"]!.AsObject().Remove("motif");
        WriteJson(path, root);
    }

    private static void Run(string commandName, IReadOnlyList<string> arguments)
    {
        var command = FindOnPath(commandName) ?? throw new IOException($"{commandName} is not available on PATH.");
        var start = new ProcessStartInfo(command)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException($"Could not start {commandName}.");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new IOException($"{commandName} did not exit within 30 seconds.");
        }
        if (process.ExitCode != 0)
            throw new IOException($"{commandName} exited with {process.ExitCode}: {error.GetAwaiter().GetResult()}{output.GetAwaiter().GetResult()}");
    }

    private static string? FindOnPath(string name)
    {
        var fileName = OperatingSystem.IsWindows() ? name + ".exe" : name;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static void WriteJson(string path, JsonObject root)
    {
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
