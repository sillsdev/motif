namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// The built executables a test starts as a real process.
/// </summary>
/// <remarks>
/// Located from the test binaries' own directory rather than from a repository-relative path, so the
/// configuration never appears in test source: a Release run drives the Release build with no second
/// literal to keep in step, and no <c>Debug</c> spelled into a test can outlive the build that made it.
/// </remarks>
internal static class BuildOutput
{
    /// <summary>Where the product builds: one level above the suite's own output directory.</summary>
    internal static string ProductDirectory { get; } =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));

    /// <summary>The command-line front end, which most of this suite drives argv-first.</summary>
    internal static string Cli { get; } = InProductDirectory("motif");

    /// <summary>The background job runner, started directly by the integration suites.</summary>
    internal static string Worker { get; } = InProductDirectory("SIL.Motif.Worker");

    private static string InProductDirectory(string name) =>
        Path.Combine(ProductDirectory, OperatingSystem.IsWindows() ? name + ".exe" : name);
}
