using System.Runtime.InteropServices;
using SkiaSharp;
using SIL.Motif.CaptureScreens;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class CaptureDiffTests
{
    [Fact]
    public void IdenticalImagesHaveNoChangedPixels()
    {
        using var previous = Bitmap(2, 2, SKColors.White);
        using var current = Bitmap(2, 2, SKColors.White);

        var result = CaptureDiff.Compare(previous, current);

        Assert.Equal("same", result.Status);
        Assert.Equal(0, result.ChangedPixels);
        Assert.Equal(0, result.ChangedPercent);
    }

    [Fact]
    public void DifferentPixelsBeyondOneChannelUnitAreCounted()
    {
        using var previous = Bitmap(2, 2, SKColors.White);
        using var current = Bitmap(2, 2, SKColors.White);
        current.SetPixel(1, 1, new SKColor(252, 255, 255));

        var result = CaptureDiff.Compare(previous, current);

        Assert.Equal("changed", result.Status);
        Assert.Equal(1, result.ChangedPixels);
        Assert.Equal(25, result.ChangedPercent);
    }

    [Fact]
    public void DifferentDimensionsAreReportedAsResized()
    {
        using var previous = Bitmap(1, 1, SKColors.White);
        using var current = Bitmap(2, 1, SKColors.White);

        var result = CaptureDiff.Compare(previous, current);

        Assert.Equal("resized", result.Status);
        Assert.Equal(1, result.ChangedPixels);
        Assert.Equal(50, result.ChangedPercent);
    }

    [Fact]
    public void MissingPreviousImageIsAdded()
    {
        using var current = Bitmap(1, 1, SKColors.White);

        var result = CaptureDiff.Compare(null, current);

        Assert.Equal("added", result.Status);
        Assert.Equal(0, result.ChangedPixels);
    }

    [Fact]
    public void MissingCurrentImageIsRemoved()
    {
        using var previous = Bitmap(1, 1, SKColors.White);

        var result = CaptureDiff.Compare(previous, null);

        Assert.Equal("removed", result.Status);
        Assert.Equal(0, result.ChangedPixels);
    }

    private static SKBitmap Bitmap(int width, int height, SKColor color)
    {
        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        bitmap.Erase(color);
        return bitmap;
    }

    [Fact]
    public void DiffReportsCapturesWithoutArtboardsOrGeneratedDiffs()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-capture-diff-" + Guid.NewGuid().ToString("N"));
        var previous = Path.Combine(root, "previous");
        var current = Path.Combine(root, "current");
        try
        {
            foreach (var folder in new[] { previous, current })
            {
                SavePng(Path.Combine(folder, "matrix", "overview.png"), SKColors.White);
                SavePng(Path.Combine(folder, "references-neighbor", "capture.png"), SKColors.White);
            }
            SavePng(Path.Combine(previous, "references", "H.png"), SKColors.Black);
            SavePng(Path.Combine(current, "references", "H.png"), SKColors.White);
            SavePng(Path.Combine(current, "references", "nested", "H.png"), SKColors.Black);
            SavePng(Path.Combine(current, "diff", "old.png"), SKColors.Black);
            var csv = Path.Combine(current, "diff.csv");

            Assert.Equal(0, CaptureDiff.Run(["--current", current, "--previous", previous, "--csv", csv]));

            var rows = File.ReadAllLines(csv);
            Assert.Equal(3, rows.Length);
            Assert.Contains(rows, row => row.StartsWith("\"matrix/overview.png\",same,", StringComparison.Ordinal));
            Assert.Contains(rows, row => row.StartsWith("\"references-neighbor/capture.png\",same,", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("WINDOWS", true)]
    [InlineData("OSX", true)]
    [InlineData("LINUX", false)]
    public void CaseOnlyRenameUsesPlatformPathPolicy(string platform, bool ignoreCase)
    {
        AssertCaseOnlyRename(OSPlatform.Create(platform), ignoreCase);
    }

    [Fact]
    public void CaseOnlyRenameUsesActualHostPathPolicy()
    {
        AssertCaseOnlyRename(null, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());
    }

    private static void AssertCaseOnlyRename(OSPlatform? platform, bool ignoreCase)
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-capture-case-" + Guid.NewGuid().ToString("N"));
        var previous = Path.Combine(root, "previous");
        var current = Path.Combine(root, "current");
        try
        {
            SavePng(Path.Combine(previous, "nested", "overview.png"), SKColors.White);
            SavePng(Path.Combine(current, "nested", "Overview.png"), SKColors.Black);
            var csv = Path.Combine(current, "diff.csv");
            string[] args = ["--current", current, "--previous", previous, "--csv", csv];

            Assert.Equal(0, platform.HasValue ? CaptureDiff.Run(args, platform.Value) : CaptureDiff.Run(args));

            var rows = File.ReadAllLines(csv).Skip(1).ToArray();
            if (ignoreCase)
            {
                Assert.Equal("\"nested/Overview.png\",changed,1,100", Assert.Single(rows));
                Assert.True(File.Exists(Path.Combine(current, "diff", "nested", "Overview.png")));
            }
            else
            {
                Assert.Equal(2, rows.Length);
                Assert.Contains("\"nested/Overview.png\",added,,", rows);
                Assert.Contains("\"nested/overview.png\",removed,,", rows);
                Assert.Empty(Directory.EnumerateFiles(Path.Combine(current, "diff"), "*.png", SearchOption.AllDirectories));
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("WINDOWS", true)]
    [InlineData("OSX", true)]
    [InlineData("LINUX", false)]
    public void GeneratedFolderExclusionUsesPlatformPathPolicy(string platform, bool ignoreCase)
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-capture-exclusions-" + Guid.NewGuid().ToString("N"));
        try
        {
            SavePng(Path.Combine(root, "Diff", "generated.png"), SKColors.White);
            SavePng(Path.Combine(root, "References", "H.png"), SKColors.White);
            SavePng(Path.Combine(root, "References-neighbor", "capture.png"), SKColors.White);
            var csv = Path.Combine(root, "diff.csv");

            Assert.Equal(0, CaptureDiff.Run(["--current", root, "--csv", csv], OSPlatform.Create(platform)));

            var rows = File.ReadAllLines(csv).Skip(1).ToArray();
            Assert.Contains("\"References-neighbor/capture.png\",added,,", rows);
            Assert.Equal(ignoreCase ? 1 : 3, rows.Length);
            if (!ignoreCase)
            {
                Assert.Contains("\"Diff/generated.png\",added,,", rows);
                Assert.Contains("\"References/H.png\",added,,", rows);
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void SavePng(string path, SKColor color)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = Bitmap(1, 1, color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }
}
