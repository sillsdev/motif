namespace SIL.Motif.Tests.App.Walkthrough;

internal static class WalkthroughTestFiles
{
    // Suites in other worktrees replay the same scripts at once, so a fixed per-script path is shared with them.
    internal static string ProcessFolder { get; } = Environment.ProcessId + "-" + Guid.NewGuid().ToString("N");

    /// <summary>Where a walkthrough replay keeps the managed root for <paramref name="scriptId"/> in this process.</summary>
    internal static string EngineRoot(string scriptId) =>
        Path.Combine(Path.GetTempPath(), "SIL.Motif.Walkthrough", "engine", ProcessFolder, scriptId);

    /// <summary>Where this process writes the actual and diff images of a baseline that did not match.</summary>
    internal static string DiagnosticsDirectory { get; } =
        Path.Combine(Path.GetTempPath(), "SIL.Motif.WalkthroughDiffs", ProcessFolder);

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
