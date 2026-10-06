namespace SIL.Motif.Host.Parser;

/// <summary>
/// Locates the <c>pangloss</c> executable.
/// </summary>
public static class PanGlossExecutable
{
    /// <summary>Overrides discovery. Set this when the parser lives somewhere unusual, or in CI.</summary>
    public const string PathVariable = "MOTIF_PANGLOSS_EXE";

    private static string FileName =>
        OperatingSystem.IsWindows() ? "pangloss.exe" : "pangloss";

    /// <summary>
    /// Returns the executable's path, or <c>null</c> when it cannot be found. A configured override is
    /// authoritative: when it is set but missing, discovery stops instead of silently selecting another
    /// parser. Inside a Motif checkout a sibling PanGloss build wins, so local PanGloss work runs against Motif:
    /// its live <c>rust/target/release</c> build, or a <c>dist/v&lt;version&gt;</c> copy of exactly the version
    /// <c>pangloss-release.json</c> pins. Older copies are skipped because their output shapes no longer match.
    /// Otherwise the parser beside Motif is used: the bundled one in a shipped Motif, and in a development build
    /// the pinned copy <c>build.ps1</c> stages there.
    /// </summary>
    public static string? TryLocate()
    {
        return TryLocate(
            Environment.GetEnvironmentVariable(PathVariable),
            AppContext.BaseDirectory,
            FileName,
            TryFindRepositoryRoot());
    }

    /// <summary>Resolves a parser from explicit configuration, a sibling checkout, or the application directory.</summary>
    internal static string? TryLocate(
        string? configuredPath, string applicationDirectory, string fileName, string? repositoryRoot)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return ExistingFile(configuredPath);

        if (repositoryRoot is not null && NewestSiblingBuild(Path.Combine(repositoryRoot, "..", "PanGloss"), fileName,
                PinnedVersion(repositoryRoot)) is { } beside)
            return beside;

        return ExistingFile(Path.Combine(applicationDirectory, fileName));
    }

    /// <summary>
    /// Why no parser was found, naming every place <see cref="TryLocate()"/> looked so a person can see where
    /// PanGloss has to be.
    /// </summary>
    public static string NotFoundMessage => NotFoundMessageFor(PlacesSearched(
        Environment.GetEnvironmentVariable(PathVariable), AppContext.BaseDirectory, FileName,
        TryFindRepositoryRoot()));

    /// <summary>The not-found sentence for the given places, in the order discovery looked at them.</summary>
    internal static string NotFoundMessageFor(IReadOnlyList<string> places) =>
        "Could not find the pangloss executable. Looked for it at: " + string.Join("; ", places) + ". " +
        "Build it with `cargo build --release -p pg-cli` in the PanGloss checkout, or set " +
        PathVariable + " to its path.";

    /// <summary>The places <see cref="TryLocate(string?, string, string, string?)"/> looks, in its order.</summary>
    internal static IReadOnlyList<string> PlacesSearched(
        string? configuredPath, string applicationDirectory, string fileName, string? repositoryRoot)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath)) return [Path.GetFullPath(configuredPath)];

        var besideMotif = Path.GetFullPath(Path.Combine(applicationDirectory, fileName));
        if (repositoryRoot is null) return [besideMotif];

        var sibling = Path.GetFullPath(Path.Combine(repositoryRoot, "..", "PanGloss"));
        return
        [
            Path.Combine(sibling, "dist", "<version>", fileName),
            Path.Combine(sibling, "rust", "target", "release", fileName),
            besideMotif,
        ];
    }

    // PanGloss's managed release build copies to dist/v<version>; a plain cargo build leaves rust/target/release.
    private static string? NewestSiblingBuild(string panGlossRoot, string fileName, string? pinnedVersion)
    {
        var dist = Path.Combine(panGlossRoot, "dist");
        var releaseCopies = Directory.Exists(dist)
            ? Directory.EnumerateDirectories(dist)
                .Where(version => pinnedVersion is null || Path.GetFileName(version) == "v" + pinnedVersion)
                .Select(version => Path.Combine(version, fileName))
            : [];
        return releaseCopies.Append(Path.Combine(panGlossRoot, "rust", "target", "release", fileName))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Select(Path.GetFullPath)
            .FirstOrDefault();
    }

    // The checkout's pin names the PanGloss whose output shapes this Motif reads.
    private static string? PinnedVersion(string repositoryRoot)
    {
        var pin = Path.Combine(repositoryRoot, "pangloss-release.json");
        if (!File.Exists(pin)) return null;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(pin));
            return document.RootElement.TryGetProperty("version", out var version) ? version.GetString() : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string? ExistingFile(string path) =>
        File.Exists(path) ? Path.GetFullPath(path) : null;

    // The marker pair avoids treating an arbitrary parent directory as the repository root.
    private static string? TryFindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 12 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "AGENTS.md")) && Directory.Exists(Path.Combine(dir, "manifest")))
                return dir;

            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        return null;
    }
}
