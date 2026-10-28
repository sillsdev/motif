using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Requests;

namespace SIL.Motif.Host.Preferences;

/// <summary>
/// Reads and writes the per-user choices behind the checks Motif runs on a grammar without being asked.
/// </summary>
/// <remarks>
/// The choice is per user, not per project: it trades the person's patience for completeness, and the same
/// person wants the same trade on every project. The window, the command line, the MCP server and the
/// background worker all read the one file, so a grammar check reads the same choice wherever it starts.
/// </remarks>
public interface IDefaultProposalChecksStore
{
    /// <summary>How much of the stored-analysis comparison a grammar check runs when it is not told otherwise.</summary>
    StoredAnalysisComparison StoredAnalyses { get; }

    /// <summary>Saves how much of the stored-analysis comparison a grammar check runs by default.</summary>
    void SetStoredAnalyses(StoredAnalysisComparison mode);
}

/// <summary>Keeps the choices in memory only; the window uses it when no file store is supplied.</summary>
public sealed class MemoryDefaultProposalChecksStore : IDefaultProposalChecksStore
{
    /// <inheritdoc />
    public StoredAnalysisComparison StoredAnalyses { get; private set; } = StoredAnalysisComparison.Budget;

    /// <inheritdoc />
    public void SetStoredAnalyses(StoredAnalysisComparison mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        StoredAnalyses = mode;
    }
}

/// <summary>Stores the Default proposal checks beside Motif's other per-user preferences.</summary>
public sealed class FileDefaultProposalChecksStore : IDefaultProposalChecksStore
{
    /// <summary>The environment variable tests use to select a private preference file.</summary>
    public const string PathEnvironmentVariable = "MOTIF_DEFAULT_PROPOSAL_CHECKS_PATH";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        NewLine = "\n",
        Converters = { new JsonStringEnumConverter<StoredAnalysisComparison>(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _path;

    /// <summary>Creates a preference store for the supplied file path.</summary>
    public FileDefaultProposalChecksStore(string path)
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
            return Path.Combine(localData, "Motif", "default-proposal-checks.json");
        }
    }

    /// <summary>
    /// The saved choice, or the default when the file is absent, unreadable, or names an unknown mode.
    /// </summary>
    public StoredAnalysisComparison StoredAnalyses
    {
        get
        {
            try
            {
                if (!File.Exists(_path)) return StoredAnalysisComparison.Budget;
                using var document = JsonDocument.Parse(File.ReadAllText(_path));
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty("storedAnalyses", out var value) ||
                    value.ValueKind != JsonValueKind.String)
                    return StoredAnalysisComparison.Budget;
                return value.GetString() switch
                {
                    "all" => StoredAnalysisComparison.All,
                    "off" => StoredAnalysisComparison.Off,
                    _ => StoredAnalysisComparison.Budget,
                };
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                JsonException or ArgumentException or NotSupportedException or System.Security.SecurityException)
            {
                return StoredAnalysisComparison.Budget;
            }
        }
    }

    /// <inheritdoc />
    public void SetStoredAnalyses(StoredAnalysisComparison mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var directory = Path.GetDirectoryName(_path) ??
            throw new IOException("The Default proposal checks preference has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, ".default-proposal-checks-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new PreferenceDocument(mode), JsonOptions);
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
    public static FileDefaultProposalChecksStore ForInstallation()
    {
        var configuredPath = AmbientEnvironment.Get(PathEnvironmentVariable);
        return new FileDefaultProposalChecksStore(
            string.IsNullOrWhiteSpace(configuredPath) ? DefaultPath : configuredPath);
    }

    private sealed record PreferenceDocument(
        [property: JsonPropertyName("storedAnalyses")] StoredAnalysisComparison StoredAnalyses);
}
