using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using SIL.Motif.Tests.TestFixtures;
using SkiaSharp;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class WalkthroughArtifactTests
{
    [Fact]
    public void WriteProducesSiteShapedAssetsAndParsableCaptions()
    {
        var root = Path.Combine(Path.GetTempPath(), $"walkthrough-output-{Guid.NewGuid():N}");
        var output = Path.Combine(root, "generated");
        var previousOutput = Environment.GetEnvironmentVariable("MOTIF_WALKTHROUGH_OUTPUT");
        var previousUpdate = Environment.GetEnvironmentVariable("MOTIF_WALKTHROUGH_UPDATE_BASELINES");
        var previousClips = Environment.GetEnvironmentVariable("MOTIF_WALKTHROUGH_CLIPS");
        try
        {
            var repositoryRoot = FindRepositoryRoot();
            var fontDirectory = Path.Combine(root, "tests", "SIL.Motif.Tests.App", "Assets", "Fonts");
            Directory.CreateDirectory(fontDirectory);
            File.Copy(Path.Combine(repositoryRoot, "tests", "SIL.Motif.Tests.App", "Assets", "Fonts", "Andika-Bold.ttf"),
                Path.Combine(fontDirectory, "Andika-Bold.ttf"));
            var helpPath = Path.Combine(root, "help", "en", "walkthroughs", "example.json");
            Directory.CreateDirectory(Path.GetDirectoryName(helpPath)!);
            File.WriteAllText(helpPath,
                """{"id":"example","title":"Example","description":"A walkthrough.","steps":{"overview":"Overview caption."},"callouts":{"overview":{"motif-pages":"Project pages"}}}""");
            var help = WalkthroughHelpContent.Load(root, "example", "en");
            var script = new WalkthroughScript("example", []);
            var callout = new WalkthroughCaptureCallout("motif-pages", "Project pages", new Rect(80, 110, 360, 150));
            var capture = new WalkthroughCapture("overview", 0, 1600, [callout], SolidPng(SKColors.White, 0));
            Environment.SetEnvironmentVariable("MOTIF_WALKTHROUGH_OUTPUT", output);
            Environment.SetEnvironmentVariable("MOTIF_WALKTHROUGH_UPDATE_BASELINES", "1");
            Environment.SetEnvironmentVariable("MOTIF_WALKTHROUGH_CLIPS", "0");

            WalkthroughArtifacts.Write(root, script, help, [capture]);

            var outputDirectory = Path.Combine(output, script.Id);
            var manifestPath = Path.Combine(outputDirectory, "manifest.json");
            Assert.True(File.Exists(manifestPath));
            using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(repositoryRoot, "tests",
                "SIL.Motif.Tests.App", "Assets", "WalkthroughSiteFixture", "manifest.json")));
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var fixtureRoot = fixture.RootElement;
            var manifest = document.RootElement;
            Assert.Equal(fixtureRoot.EnumerateObject().Select(property => property.Name),
                manifest.EnumerateObject().Select(property => property.Name));
            var fixtureStep = Assert.Single(fixtureRoot.GetProperty("steps").EnumerateArray());
            var step = Assert.Single(manifest.GetProperty("steps").EnumerateArray());
            Assert.Equal(fixtureStep.EnumerateObject().Select(property => property.Name),
                step.EnumerateObject().Select(property => property.Name));
            var fixtureCallout = Assert.Single(fixtureStep.GetProperty("callouts").EnumerateArray());
            var writtenCallout = Assert.Single(step.GetProperty("callouts").EnumerateArray());
            Assert.Equal(fixtureCallout.EnumerateObject().Select(property => property.Name).Append("caption"),
                writtenCallout.EnumerateObject().Select(property => property.Name));
            Assert.Equal("Project pages", writtenCallout.GetProperty("caption").GetString());
            Assert.Equal("steps/01-overview.png", step.GetProperty("screenshot").GetString());
            Assert.Equal("steps/01-overview-annotated.png", step.GetProperty("annotated").GetString());
            Assert.True(File.Exists(Path.Combine(outputDirectory, "steps", "01-overview.png")));
            Assert.True(File.Exists(Path.Combine(outputDirectory, "steps", "01-overview-annotated.png")));

            var captionsPath = Path.Combine(outputDirectory, "captions.en.vtt");
            var cue = Assert.Single(ParseWebVtt(File.ReadAllText(captionsPath)));
            Assert.Equal(("00:00:00.000", "00:00:01.600", "Overview caption."), cue);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MOTIF_WALKTHROUGH_OUTPUT", previousOutput);
            Environment.SetEnvironmentVariable("MOTIF_WALKTHROUGH_UPDATE_BASELINES", previousUpdate);
            Environment.SetEnvironmentVariable("MOTIF_WALKTHROUGH_CLIPS", previousClips);
            WalkthroughTestFiles.DeleteDirectory(root);
        }
    }

    [Fact]
    public void CropZoomsToCalloutAndTransformsItsBounds()
    {
        var baseline = SolidPng(SKColors.White, 0);
        using (var bitmap = SKBitmap.Decode(baseline)!)
        {
            using var canvas = new SKCanvas(bitmap);
            using var blue = new SKPaint { Color = SKColors.Blue, Style = SKPaintStyle.Fill };
            canvas.DrawRect(new SKRect(100, 100, 200, 200), blue);
            bitmap.SetPixel(1000, 100, SKColors.Red);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            baseline = data.ToArray();
        }
        var capture = new WalkthroughCapture("crop", 0, 1000,
            [new WalkthroughCaptureCallout("motif-pages", "Project pages", new Rect(100, 100, 100, 100))], baseline);

        var cropped = WalkthroughArtifacts.Crop(capture, 48);

        using var output = SKBitmap.Decode(cropped.Png)!;
        Assert.Equal((WalkthroughArtifacts.Width, WalkthroughArtifacts.Height), (output.Width, output.Height));
        Assert.True(cropped.Callouts[0].Bounds.Width > 300);
        Assert.True(cropped.Callouts[0].Bounds.X > 0);
        Assert.DoesNotContain(Enumerable.Range(0, output.Height).SelectMany(y => Enumerable.Range(0, output.Width)
            .Select(x => output.GetPixel(x, y))), color => color == SKColors.Red);
        Assert.Contains(Enumerable.Range(0, output.Height).SelectMany(y => Enumerable.Range(0, output.Width)
            .Select(x => output.GetPixel(x, y))), color => color.Blue > color.Red);
    }

    [Fact]
    public void BaselineComparisonRejectsChangesBeyondTheDocumentedTolerance()
    {
        var baselinePath = Path.Combine(Path.GetTempPath(), $"walkthrough-baseline-{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(baselinePath, SolidPng(SKColors.White, 0));
            var allowedChangedPixels = (int)Math.Ceiling(
                WalkthroughArtifacts.Width * WalkthroughArtifacts.Height * WalkthroughArtifacts.ChangedPixelTolerance);
            var tooDifferent = SolidPng(SKColors.White, allowedChangedPixels + 1);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
                WalkthroughArtifacts.CheckBaseline(baselinePath, tooDifferent, update: false));
        }
        finally
        {
            File.Delete(baselinePath);
        }
    }

    [Fact]
    public void BaselineComparisonUsesAtMostOneTenthPercentChangedPixels()
    {
        var baselinePath = Path.Combine(Path.GetTempPath(), $"walkthrough-baseline-{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(baselinePath, SolidPng(SKColors.White, 0));
            var changedPixels = (int)Math.Ceiling(
                WalkthroughArtifacts.Width * WalkthroughArtifacts.Height * 0.001) + 1;

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
                WalkthroughArtifacts.CheckBaseline(baselinePath, SolidPng(SKColors.White, changedPixels), update: false));
        }
        finally
        {
            File.Delete(baselinePath);
        }
    }

    [Fact]
    public void BaselineComparisonRejectsAnyDifferenceInsideACallout()
    {
        var baselinePath = Path.Combine(Path.GetTempPath(), $"walkthrough-baseline-{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(baselinePath, SolidPng(SKColors.White, 0));
            var actual = PngWithPixel(new SKColor(254, 255, 255), 0, 0);
            var callout = new WalkthroughCaptureCallout(
                "motif-pages", "Project pages", new Avalonia.Rect(0, 0, 1, 1));

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
                WalkthroughArtifacts.CheckBaseline(baselinePath, actual, update: false, [callout]));
        }
        finally
        {
            File.Delete(baselinePath);
        }
    }

    [Fact]
    public void BaselineComparisonWritesADiffPngWhenItFails()
    {
        var baselinePath = Path.Combine(Path.GetTempPath(), $"walkthrough-baseline-{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(baselinePath, SolidPng(SKColors.White, 0));
            var changedPixels = (int)Math.Ceiling(
                WalkthroughArtifacts.Width * WalkthroughArtifacts.Height * WalkthroughArtifacts.ChangedPixelTolerance) + 1;

            var failure = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
                WalkthroughArtifacts.CheckBaseline(baselinePath, SolidPng(SKColors.White, changedPixels), update: false));
            var diffPath = failure.Message.Split("Diff PNG: ", StringSplitOptions.None).Last();
            Assert.True(File.Exists(diffPath), failure.Message);
            File.Delete(diffPath);
        }
        finally
        {
            File.Delete(baselinePath);
        }
    }

    [Fact]
    public void MissingFfmpegSkipsClipComposition()
    {
        var output = Path.Combine(Path.GetTempPath(), $"walkthrough-no-ffmpeg-{Guid.NewGuid():N}");
        var missingFfmpeg = Path.Combine(output, "missing-ffmpeg.exe");

        var clip = WalkthroughClipComposer.TryCompose(output, [], missingFfmpeg);

        Assert.Null(clip);
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public void ClipFrameCountsFollowRoundedTimelineBoundaries()
    {
        var segments = new[]
        {
            (StartMs: 0, DurationMs: 250),
            (StartMs: 250, DurationMs: 250),
            (StartMs: 500, DurationMs: 1600),
            (StartMs: 2100, DurationMs: 250),
        };

        var frameCounts = segments.Select(segment =>
            WalkthroughClipComposer.FramesForSegment(segment.StartMs, segment.DurationMs)).ToArray();

        Assert.Equal([8, 7, 48, 8], frameCounts);
        Assert.Equal(71, frameCounts.Sum());
    }

    private static byte[] SolidPng(SKColor baseColor, int changedPixels)
    {
        using var bitmap = new SKBitmap(WalkthroughArtifacts.Width, WalkthroughArtifacts.Height);
        bitmap.Erase(baseColor);
        for (var index = 0; index < changedPixels; index++)
            bitmap.SetPixel(index % bitmap.Width, index / bitmap.Width, SKColors.Black);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static byte[] PngWithPixel(SKColor color, int x, int y)
    {
        using var bitmap = new SKBitmap(WalkthroughArtifacts.Width, WalkthroughArtifacts.Height);
        bitmap.Erase(SKColors.White);
        bitmap.SetPixel(x, y, color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static IReadOnlyList<(string Start, string End, string Caption)> ParseWebVtt(string text)
    {
        Assert.StartsWith("WEBVTT\n\n", text, StringComparison.Ordinal);
        var cues = Regex.Matches(text,
                @"(?m)^(?<start>\d{2}:\d{2}:\d{2}\.\d{3}) --> (?<end>\d{2}:\d{2}:\d{2}\.\d{3})\r?\n(?<caption>[^\r\n]+)\r?$")
            .Select(match => (match.Groups["start"].Value, match.Groups["end"].Value, match.Groups["caption"].Value))
            .ToArray();
        Assert.NotEmpty(cues);
        return cues;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Motif.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate Motif.sln.");
    }
}
