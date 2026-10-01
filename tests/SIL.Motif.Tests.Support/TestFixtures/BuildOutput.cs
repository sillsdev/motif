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

    /// <summary>The portable package prepared by the build gate for sibling-process tests.</summary>
    internal static string PortableWorkerPackageDirectory { get; } =
        Path.Combine(ProductDirectory, "tests", "prepared", "portable-worker-package");

    /// <summary>The checks recorded while the portable package was prepared.</summary>
    internal static string PortableWorkerPackageValidation { get; } =
        Path.Combine(ProductDirectory, "tests", "prepared", "portable-worker-package-validation.json");

    /// <summary>The sample project prepared for the Explained Word Card walkthrough.</summary>
    internal static string ExplainedWordCardFixtureDirectory { get; } =
        Path.Combine(ProductDirectory, "tests", "prepared", "walkthrough-fixtures", "explained-word-card");

    private static string InProductDirectory(string name) =>
        Path.Combine(ProductDirectory, OperatingSystem.IsWindows() ? name + ".exe" : name);
}
