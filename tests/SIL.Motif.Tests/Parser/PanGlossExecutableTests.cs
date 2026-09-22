using SIL.Motif.Host.Parser;
using Xunit;

namespace SIL.Motif.Tests.Parser;

public sealed class PanGlossExecutableTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "motif-parser-discovery-" + Guid.NewGuid().ToString("N"));

    public PanGlossExecutableTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void InsideACheckoutTheSiblingBuildBeatsABundledParser()
    {
        var applicationDirectory = Directory.CreateDirectory(Path.Combine(_root, "app")).FullName;
        var repositoryRoot = Directory.CreateDirectory(Path.Combine(_root, "repo")).FullName;
        Touch(Path.Combine(applicationDirectory, "pangloss.exe"));
        var beside = Touch(DevelopmentParserPath(repositoryRoot));

        var result = PanGlossExecutable.TryLocate(
            configuredPath: null,
            applicationDirectory: applicationDirectory,
            fileName: "pangloss.exe",
            repositoryRoot: repositoryRoot);

        // Local work runs the parser being built beside Motif, never a pinned copy left in the output folder.
        Assert.Equal(Path.GetFullPath(beside), result);
    }

    [Fact]
    public void InsideACheckoutWithNoSiblingBuildTheBundledParserIsUsed()
    {
        var applicationDirectory = Directory.CreateDirectory(Path.Combine(_root, "app")).FullName;
        var repositoryRoot = Directory.CreateDirectory(Path.Combine(_root, "repo")).FullName;
        var bundled = Touch(Path.Combine(applicationDirectory, "pangloss.exe"));

        var result = PanGlossExecutable.TryLocate(
            configuredPath: null,
            applicationDirectory: applicationDirectory,
            fileName: "pangloss.exe",
            repositoryRoot: repositoryRoot);

        Assert.Equal(Path.GetFullPath(bundled), result);
    }

    [Fact]
    public void ExplicitOverrideIsPreferredToBundledParser()
    {
        var applicationDirectory = Directory.CreateDirectory(Path.Combine(_root, "app")).FullName;
        var repositoryRoot = Directory.CreateDirectory(Path.Combine(_root, "repo")).FullName;
        Touch(Path.Combine(applicationDirectory, "pangloss.exe"));
        var overridePath = Touch(Path.Combine(_root, "developer", "pangloss.exe"));

        var result = PanGlossExecutable.TryLocate(
            configuredPath: overridePath,
            applicationDirectory: applicationDirectory,
            fileName: "pangloss.exe",
            repositoryRoot: repositoryRoot);

        Assert.Equal(Path.GetFullPath(overridePath), result);
    }

    [Fact]
    public void MissingExplicitOverrideDoesNotFallThroughToAnotherParser()
    {
        var applicationDirectory = Directory.CreateDirectory(Path.Combine(_root, "app")).FullName;
        var repositoryRoot = Directory.CreateDirectory(Path.Combine(_root, "repo")).FullName;
        Touch(Path.Combine(applicationDirectory, "pangloss.exe"));
        Touch(DevelopmentParserPath(repositoryRoot));
        var missingOverride = Path.Combine(_root, "missing", "pangloss.exe");

        var result = PanGlossExecutable.TryLocate(
            configuredPath: missingOverride,
            applicationDirectory: applicationDirectory,
            fileName: "pangloss.exe",
            repositoryRoot: repositoryRoot);

        Assert.Null(result);
    }

    [Fact]
    public void TheSiblingBuildIsUsedWhenTheBundleIsAbsent()
    {
        var applicationDirectory = Directory.CreateDirectory(Path.Combine(_root, "app")).FullName;
        var repositoryRoot = Directory.CreateDirectory(Path.Combine(_root, "repo")).FullName;
        var fallback = Touch(DevelopmentParserPath(repositoryRoot));

        var result = PanGlossExecutable.TryLocate(
            configuredPath: null,
            applicationDirectory: applicationDirectory,
            fileName: "pangloss.exe",
            repositoryRoot: repositoryRoot);

        Assert.Equal(Path.GetFullPath(fallback), result);
    }

    [Fact]
    public void AppRelativeParserIsUsedWithoutRepositoryRoot()
    {
        var applicationDirectory = Directory.CreateDirectory(Path.Combine(_root, "app")).FullName;
        var bundled = Touch(Path.Combine(applicationDirectory, "pangloss.exe"));

        var result = PanGlossExecutable.TryLocate(
            configuredPath: null,
            applicationDirectory: applicationDirectory,
            fileName: "pangloss.exe",
            repositoryRoot: null);

        Assert.Equal(Path.GetFullPath(bundled), result);
    }

    [Fact]
    public void ReturnsNullWhenNoParserCandidateExists()
    {
        var applicationDirectory = Directory.CreateDirectory(Path.Combine(_root, "app")).FullName;

        var result = PanGlossExecutable.TryLocate(
            configuredPath: null,
            applicationDirectory: applicationDirectory,
            fileName: "pangloss.exe",
            repositoryRoot: null);

        Assert.Null(result);
    }

    private static string DevelopmentParserPath(string repositoryRoot) => Path.GetFullPath(Path.Combine(
        repositoryRoot, "..", "PanGloss", "rust", "target", "release", "pangloss.exe"));

    private static string Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "parser placeholder");
        return path;
    }
}
