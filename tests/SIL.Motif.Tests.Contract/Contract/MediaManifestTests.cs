using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    public void ManifestListsEveryRepositoryMediaFileExactlyOnceAndCapturedShotsResolve()
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

        AssertCapturedGuideShotsResolve(root);
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

    private static void AssertCapturedGuideShotsResolve(string root)
    {
        var mediaRoot = Path.Combine(root, "bin");
        if (!Directory.Exists(mediaRoot))
            return;

        var manifests = Directory.GetFiles(mediaRoot, "manifest.json", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}media{Path.DirectorySeparatorChar}walkthroughs{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (manifests.Length == 0)
            return;

        var resolved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var manifestPath in manifests)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var manifest = document.RootElement;
            var walkthroughId = manifest.GetProperty("id").GetString();
            var directory = Path.GetDirectoryName(manifestPath)!;
            foreach (var step in manifest.GetProperty("steps").EnumerateArray())
            {
                var stepId = step.GetProperty("id").GetString();
                var screenshot = step.GetProperty("screenshot").GetString();
                if (string.IsNullOrWhiteSpace(walkthroughId) || string.IsNullOrWhiteSpace(stepId) || string.IsNullOrWhiteSpace(screenshot))
                    continue;

                var screenshotPath = Path.GetFullPath(Path.Combine(directory, screenshot.Replace('/', Path.DirectorySeparatorChar)));
                Assert.True(File.Exists(screenshotPath), $"Walkthrough screenshot is missing: {walkthroughId}/{stepId} -> {screenshot}");
                resolved.Add($"{walkthroughId}/{stepId}");
            }
        }

        var guideRoot = Path.Combine(root, "help", "en", "guide");
        var unresolved = Directory.GetFiles(guideRoot, "*.md", SearchOption.AllDirectories)
            .SelectMany(path => Regex.Matches(File.ReadAllText(path), @"\]\(shot:([^)]+)\)")
                .Select(match => match.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .Where(target => !resolved.Contains(target))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(unresolved.Length == 0,
            $"Captured Walkthroughs do not resolve Guide shots: {string.Join(", ", unresolved.Select(target => $"shot:{target}"))}");
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
