using System.Runtime.InteropServices;

namespace SIL.Motif.Host.Parser;

/// <summary>
/// Locates the <c>pangloss</c> executable.
/// </summary>
public static class PanGlossExecutable
{
    /// <summary>Overrides discovery. Set this when the parser lives somewhere unusual, or in CI.</summary>
    public const string PathVariable = "MOTIF_PANGLOSS_EXE";

    private static string FileName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "pangloss.exe" : "pangloss";

    /// <summary>
    /// Returns the executable's path, or <c>null</c> when it cannot be found. A configured override is
    /// authoritative: when it is set but missing, discovery stops instead of silently selecting another
    /// parser. Otherwise the bundled executable beside the application is preferred to the development
    /// checkout fallback.
    /// </summary>
    public static string? TryLocate()
    {
        return TryLocate(
            Environment.GetEnvironmentVariable(PathVariable),
            AppContext.BaseDirectory,
            FileName,
            TryFindRepositoryRoot());
    }

    /// <summary>Resolves a parser from explicit configuration, an application directory, or a repository.</summary>
    internal static string? TryLocate(
        string? configuredPath, string applicationDirectory, string fileName, string? repositoryRoot)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return ExistingFile(configuredPath);

        var bundled = ExistingFile(Path.Combine(applicationDirectory, fileName));
        if (bundled is not null) return bundled;

        if (repositoryRoot is null) return null;

        return ExistingFile(Path.Combine(
            repositoryRoot, "..", "PanGloss", "rust", "target", "release", fileName));
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
