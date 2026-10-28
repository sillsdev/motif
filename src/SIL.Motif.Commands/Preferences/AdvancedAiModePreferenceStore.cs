using SIL.Motif.Host;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIL.Motif.Commands.Preferences;

/// <summary>Reads and writes the per-user choice that exposes Motif's AI-agent features.</summary>
public interface IAdvancedAiModePreferenceStore
{
    /// <summary>Whether Advanced AI mode is enabled for this user.</summary>
    bool IsEnabled { get; }

    /// <summary>Saves whether Advanced AI mode is enabled for this user.</summary>
    void SetEnabled(bool enabled);
}

/// <summary>Stores Advanced AI mode beside Motif's other per-user preferences.</summary>
public sealed class FileAdvancedAiModePreferenceStore : IAdvancedAiModePreferenceStore
{
    /// <summary>The environment variable tests use to select a private preference file.</summary>
    public const string PathEnvironmentVariable = "MOTIF_ADVANCED_AI_MODE_PATH";

    /// <summary>The refusal shown when an AI tool is called while Advanced AI mode is off.</summary>
    public const string DisabledMessage =
        "Advanced AI mode is OFF. Run 'motif settings advanced-ai on' to enable Motif's tools. " +
        "In a sandbox, set MOTIF_ADVANCED_AI_MODE_PATH to a writable preference file. " +
        "Then restart the Motif MCP server and reconnect the assistant so it reads the changed startup setting.";

    /// <summary>Explains why an initialized MCP connection exposes no tools while Advanced AI mode is off.</summary>
    public const string DisabledServerInstructions =
        "The Motif MCP server started and connected successfully. Advanced AI mode is OFF, so zero tools are exposed. " +
        "Run 'motif settings advanced-ai on' to enable Motif's tools. In a sandbox, set " +
        "MOTIF_ADVANCED_AI_MODE_PATH to a writable preference file before changing the setting. " +
        "Then restart the Motif MCP server and reconnect the assistant so it reads the changed startup setting.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _path;

    /// <summary>Creates a preference store for the supplied file path.</summary>
    public FileAdvancedAiModePreferenceStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    /// <summary>The per-user file, in the same directory used by the window's preferences.</summary>
    public static string DefaultPath
    {
        get
        {
            var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localData))
                throw new InvalidOperationException("The per-user data directory is unavailable.");
            return Path.Combine(localData, "Motif", "advanced-ai-mode.json");
        }
    }

    /// <inheritdoc />
    public bool IsEnabled
    {
        get
        {
            try
            {
                if (!File.Exists(_path)) return false;
                using var document = JsonDocument.Parse(File.ReadAllText(_path));
                return document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("advancedAiMode", out var value) &&
                    value.ValueKind == JsonValueKind.True;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                JsonException or ArgumentException or NotSupportedException or System.Security.SecurityException)
            {
                return false;
            }
        }
    }

    /// <inheritdoc />
    public void SetEnabled(bool enabled)
    {
        var directory = Path.GetDirectoryName(_path) ??
            throw new IOException("The Advanced AI mode preference has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, ".advanced-ai-mode-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new PreferenceDocument(enabled), JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    /// <summary>Creates a store using the installed user's preference path or the test override.</summary>
    public static FileAdvancedAiModePreferenceStore ForInstallation()
    {
        var configuredPath = AmbientEnvironment.Get(PathEnvironmentVariable);
        return new FileAdvancedAiModePreferenceStore(
            string.IsNullOrWhiteSpace(configuredPath) ? DefaultPath : configuredPath);
    }

    private sealed record PreferenceDocument(
        [property: JsonPropertyName("advancedAiMode")] bool AdvancedAiMode);
}
