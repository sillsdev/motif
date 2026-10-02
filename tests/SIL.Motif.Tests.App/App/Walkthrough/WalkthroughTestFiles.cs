namespace SIL.Motif.Tests.App.Walkthrough;

internal static class WalkthroughTestFiles
{
    // Suites in other worktrees replay the same scripts at once, so a fixed per-script path is shared with them.
    internal static string ProcessFolder { get; } = Environment.ProcessId + "-" + Guid.NewGuid().ToString("N");

    /// <summary>Where a walkthrough replay keeps the managed root for <paramref name="scriptId"/> in this process.</summary>
    internal static string EngineRoot(string scriptId) =>
        Path.Combine(Path.GetTempPath(), "SIL.Motif.Walkthrough", "engine", ProcessFolder, scriptId);

    /// <summary>Where this process writes actual and diff images for the CI test-results artifact.</summary>
    internal static string DiagnosticsDirectory { get; } = Path.Combine(
        new DirectoryInfo(AppContext.BaseDirectory).Parent!.FullName,
        "test-results", "walkthrough-diffs", ProcessFolder);

    internal static void CopyDirectory(string source, string destination)
    {
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException("The prepared walkthrough fixture is missing: " + source);
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
    }

    internal static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
