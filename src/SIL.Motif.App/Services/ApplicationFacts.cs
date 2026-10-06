using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using SIL.Motif.Host;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.App.Services;

/// <summary>Read-only runtime facts shared by About and problem reports.</summary>
/// <param name="MotifVersion">The sanitized running product version.</param>
/// <param name="PanGlossVersion">The declared release version or an unknown custom-executable value.</param>
/// <param name="OperatingSystem">The sanitized operating-system description.</param>
/// <param name="IsCustomParser">Whether the running commands use a custom parser executable.</param>
/// <param name="ParserAvailable">Whether the selected parser executable exists.</param>
/// <param name="ManagedDataRoot">The managed project-data and parser-artifact directory.</param>
public sealed record ApplicationFacts(
    string MotifVersion,
    string PanGlossVersion,
    string OperatingSystem,
    bool IsCustomParser,
    bool ParserAvailable,
    string ManagedDataRoot)
{
    private static readonly Regex SafeVersion = new(@"\A[0-9A-Za-z][0-9A-Za-z.+-]{0,39}\z",
        RegexOptions.CultureInvariant);

    /// <summary>The current process facts, captured once so About and reports describe the same runtime.</summary>
    public static ApplicationFacts Current { get; } = ReadCurrent();

    /// <summary>Reads the installed product and parser facts for this process.</summary>
    public static ApplicationFacts ReadCurrent()
    {
        var configuredParser = Environment.GetEnvironmentVariable(PanGlossExecutable.PathVariable);
        return new ApplicationFacts(
            SafeText(MotifProductVersion.CurrentText),
            ReadPanGlossVersion(configuredParser),
            string.Join(' ', RuntimeInformation.OSDescription.Split((char[]?)null,
                StringSplitOptions.RemoveEmptyEntries)),
            !string.IsNullOrWhiteSpace(configuredParser),
            PanGlossExecutable.TryLocate() is not null,
            ResolveManagedDataRoot());
    }

    /// <summary>Projects runtime facts for the command settings used by one App window.</summary>
    /// <param name="managedDataRoot">The managed project-data and parser-artifact directory.</param>
    /// <param name="parserPath">The parser executable selected for this App, if found.</param>
    public static ApplicationFacts ForApp(string managedDataRoot, string? parserPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedDataRoot);
        var facts = Current;
        var locatedParser = PanGlossExecutable.TryLocate();
        var usesCustomParser = facts.IsCustomParser || parserPath is not null &&
            (locatedParser is null || !PathEquals(parserPath, locatedParser));
        return facts with
        {
            PanGlossVersion = usesCustomParser ? "unknown (custom executable)" : facts.PanGlossVersion,
            IsCustomParser = usesCustomParser,
            ParserAvailable = parserPath is not null && File.Exists(parserPath),
            ManagedDataRoot = Path.GetFullPath(managedDataRoot),
        };
    }

    private static bool PathEquals(string left, string right)
    {
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
                System.OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string ReadPanGlossVersion(string? configuredParser)
    {
        if (!string.IsNullOrWhiteSpace(configuredParser)) return "unknown (custom executable)";
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
        {
            var packaged = ReadPackageManifest(Path.Combine(directory.FullName, "release-manifest.json"));
            if (packaged is not null) return packaged;
            var pinPath = Path.Combine(directory.FullName, "pangloss-release.json");
            if (File.Exists(pinPath))
                return ReadVersion(pinPath) is { } pinned ? pinned + " (release pin)" : "unknown";
        }
        return "unknown";
    }

    private static string? ReadPackageManifest(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("dependencies", out var dependencies) ||
                dependencies.ValueKind != JsonValueKind.Array) return null;
            foreach (var dependency in dependencies.EnumerateArray())
                if (dependency.TryGetProperty("name", out var name) && name.GetString() == "PanGloss" &&
                    dependency.TryGetProperty("version", out var version) && version.GetString() is { } text &&
                    SafeVersion.IsMatch(text)) return text;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
        }
        return null;
    }

    private static string? ReadVersion(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("version", out var version) &&
                   version.GetString() is { } text && SafeVersion.IsMatch(text) ? text : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string SafeText(string value) => SafeVersion.IsMatch(value) ? value : "unknown";

    private static string ResolveManagedDataRoot()
    {
        var configured = Environment.GetEnvironmentVariable("MOTIF_WORKER_ROOT");
        return Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SIL", "Motif")
            : configured);
    }
}
