using SkiaSharp;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

public sealed class WalkthroughArtifactTests
{
    [Fact]
    public void ManifestUsesTheDocsSiteShape()
    {
        var manifest = new WalkthroughManifest(
            "open-project-overview", "en", "Open a project", "See its summary.", 1280, 720, 30,
            [new WalkthroughManifestStep("overview", "Words in the Selection", 0, 1600,
                "screenshots/overview.png", "screenshots/overview-annotated.png",
                [new WalkthroughManifestCallout(10, 20, 30, 40, "1")])],
            null);

        using var document = System.Text.Json.JsonDocument.Parse(WalkthroughArtifacts.SerializeManifest(manifest));
        var root = document.RootElement;
        Assert.Equal(
            ["id", "locale", "title", "description", "width", "height", "fps", "steps", "clip"],
            root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, root.GetProperty("clip").ValueKind);
        var step = Assert.Single(root.GetProperty("steps").EnumerateArray());
        Assert.Equal(
            ["id", "caption", "startMs", "endMs", "screenshot", "annotated", "callouts"],
            step.EnumerateObject().Select(property => property.Name));
        var callout = Assert.Single(step.GetProperty("callouts").EnumerateArray());
        Assert.Equal(["x", "y", "width", "height", "label"],
            callout.EnumerateObject().Select(property => property.Name));
        Assert.Equal("1", callout.GetProperty("label").GetString());
    }

    [Fact]
    public void ManifestClipUsesTheDocsSiteAssetNames()
    {
        var manifest = new WalkthroughManifest(
            "open-project-overview", "en", "Open a project", "See its summary.", 1280, 720, 30, [],
            new ManifestClip("clip.webm", "clip.mp4", "clip.webp", "poster.png"));

        using var document = System.Text.Json.JsonDocument.Parse(WalkthroughArtifacts.SerializeManifest(manifest));
        Assert.Equal(["webm", "mp4", "webp", "poster"],
            document.RootElement.GetProperty("clip").EnumerateObject().Select(property => property.Name));
        Assert.Equal("poster.png", document.RootElement.GetProperty("clip").GetProperty("poster").GetString());
    }

    [Fact]
    public void BaselineComparisonRejectsChangesBeyondTheDocumentedTolerance()
    {
        var baselinePath = Path.Combine(Path.GetTempPath(), $"walkthrough-baseline-{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(baselinePath, SolidPng(SKColors.White, 0));
            var allowedChangedPixels = (int)Math.Ceiling(
                WalkthroughArtifacts.Width * WalkthroughArtifacts.Height * 0.01);
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
    public void MissingFfmpegSkipsClipComposition()
    {
        var output = Path.Combine(Path.GetTempPath(), $"walkthrough-no-ffmpeg-{Guid.NewGuid():N}");
        var missingFfmpeg = Path.Combine(output, "missing-ffmpeg.exe");

        var clip = WalkthroughClipComposer.TryCompose(output, [], missingFfmpeg);

        Assert.Null(clip);
        Assert.False(Directory.Exists(output));
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
}
