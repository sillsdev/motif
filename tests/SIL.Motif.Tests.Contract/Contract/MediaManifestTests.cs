using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace SIL.Motif.Tests.Contract;

/// <summary>Checks that the media manifest exactly tracks repository images, videos, and diagrams.</summary>
public sealed class MediaManifestTests
{
    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".tif", ".tiff", ".ico", ".svg",
        ".mp4", ".webm", ".mov", ".mkv", ".avi", ".m4v", ".drawio", ".mmd", ".mermaid",
        ".puml", ".dot",
    };

    [Fact]
    public void ManifestListsEveryRepositoryMediaFileExactlyOnceAndWalkthroughScreenshotsExist()
    {
        var root = FindRepositoryRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "media.json")));
        var entries = document.RootElement.GetProperty("media").EnumerateArray().ToArray();
        var paths = entries.Select(entry => entry.GetProperty("path").GetString()!).ToArray();

        Assert.Equal(paths.Length, paths.Distinct(StringComparer.Ordinal).Count());
        foreach (var entry in entries)
        {
            var path = entry.GetProperty("path").GetString()!;
            Assert.True(File.Exists(Resolve(root, path)), $"Media manifest path does not exist: {path}");
            AssertValidClassification(entry, root, path);
        }

        var repositoryPaths = TrackedAndUntrackedFiles(root)
            .Where(IsMediaPath)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var declaredPaths = paths.Order(StringComparer.Ordinal).ToArray();
        var undeclared = repositoryPaths.Except(declaredPaths, StringComparer.Ordinal).ToArray();
        var missing = declaredPaths.Except(repositoryPaths, StringComparer.Ordinal).ToArray();

        Assert.True(undeclared.Length == 0,
            $"Media files missing from media.json: {string.Join(", ", undeclared)}");
        Assert.True(missing.Length == 0,
            $"media.json names files that are not repository media: {string.Join(", ", missing)}");

        AssertWalkthroughScreenshotsExist(root);
    }

    [Fact]
    public void CheckedInWalkthroughCapturesAreGeneratedByTheWalkthroughsStep()
    {
        var root = FindRepositoryRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "media.json")));
        var entries = document.RootElement.GetProperty("media").EnumerateArray()
            .ToDictionary(entry => entry.GetProperty("path").GetString()!, StringComparer.Ordinal);
        var captures = TrackedAndUntrackedFiles(root)
            .Where(IsMediaPath)
            .Where(path => path.StartsWith("site/fixtures/walkthroughs/", StringComparison.Ordinal)
                || path.StartsWith("tests/SIL.Motif.Tests.App/Assets/WalkthroughBaselines/", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(captures);
        foreach (var path in captures)
        {
            Assert.True(entries.TryGetValue(path, out var entry), $"Walkthrough capture is missing from media.json: {path}");
            Assert.Equal("generated", entry.GetProperty("classification").GetString());
            Assert.Equal("walkthroughs", entry.GetProperty("step").GetString());
            Assert.Equal(path, entry.GetProperty("outputPath").GetString());
        }
    }

    private static void AssertValidClassification(JsonElement entry, string root, string path)
    {
        var classification = entry.GetProperty("classification").GetString();
        Assert.True(classification is "generated" or "source" or "record",
            $"Unknown media classification for {path}: {classification}");

        if (classification != "generated")
            return;

        Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("step").GetString()));
        var outputPath = entry.GetProperty("outputPath").GetString();
        Assert.False(string.IsNullOrWhiteSpace(outputPath));
        Assert.True(File.Exists(Resolve(root, outputPath!)),
            $"Generated media output does not exist for {path}: {outputPath}");
    }

    private static string[] TrackedAndUntrackedFiles(string root)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("ls-files");
        start.ArgumentList.Add("-z");
        start.ArgumentList.Add("--cached");
        start.ArgumentList.Add("--others");
        start.ArgumentList.Add("--exclude-standard");

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start git to enumerate repository files.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Could not enumerate repository files: {error}");

        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool IsMediaPath(string path) =>
        path.EndsWith(".dc.html", StringComparison.OrdinalIgnoreCase)
        || MediaExtensions.Contains(Path.GetExtension(path));

    private static string Resolve(string root, string path) =>
        Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));

    private static void AssertWalkthroughScreenshotsExist(string root)
    {
        var mediaRoot = Path.Combine(root, "bin");
        if (!Directory.Exists(mediaRoot))
            return;

        var manifests = Directory.GetFiles(mediaRoot, "manifest.json", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}media{Path.DirectorySeparatorChar}walkthroughs{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (var manifestPath in manifests)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var manifest = document.RootElement;
            var directory = Path.GetDirectoryName(manifestPath)!;
            foreach (var step in manifest.GetProperty("steps").EnumerateArray())
            {
                var screenshot = step.GetProperty("screenshot").GetString();
                if (string.IsNullOrWhiteSpace(screenshot))
                    continue;

                var screenshotPath = Path.GetFullPath(Path.Combine(directory, screenshot.Replace('/', Path.DirectorySeparatorChar)));
                Assert.True(File.Exists(screenshotPath), $"Walkthrough screenshot is missing: {manifestPath} -> {screenshot}");
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Motif.sln")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Motif repository root.");
    }
}
