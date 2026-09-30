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
        Assert.Equal((196, 196), (output.Width, output.Height));
        Assert.Equal(100, cropped.Callouts[0].Bounds.Width);
        Assert.Equal(48, cropped.Callouts[0].Bounds.X);
        Assert.Equal(new Rect(52, 52, 196, 196), cropped.SourceFrameCropBounds);
        Assert.DoesNotContain(Enumerable.Range(0, output.Height).SelectMany(y => Enumerable.Range(0, output.Width)
            .Select(x => output.GetPixel(x, y))), color => color == SKColors.Red);
        Assert.Contains(Enumerable.Range(0, output.Height).SelectMany(y => Enumerable.Range(0, output.Width)
            .Select(x => output.GetPixel(x, y))), color => color.Blue > color.Red);
    }

    [Fact]
    public void CalloutCaptionLabelsStayInsideTheFrameAndAvoidTargetsAndEachOther()
    {
        var callouts = new[]
        {
            new WalkthroughCaptureCallout("first", "Stored FieldWorks opinion", new Rect(500, 300, 145, 45)),
            new WalkthroughCaptureCallout("second", "PanGloss found another analysis", new Rect(540, 325, 155, 45)),
            new WalkthroughCaptureCallout("third", "No analysis is stored for this word", new Rect(510, 350, 165, 45)),
        };
        using var font = new SKFont(SKTypeface.Default, 16);
        var labels = WalkthroughArtifacts.ArrangeCaptionLabels(
            callouts, WalkthroughArtifacts.Width, WalkthroughArtifacts.Height, font)
            .Select(label => label.Bounds)
            .ToArray();

        Assert.Equal(callouts.Length, labels.Length);
        Assert.All(labels, label =>
        {
            Assert.True(label.X >= 18 && label.Y >= 18);
            Assert.True(label.Right <= WalkthroughArtifacts.Width - 18);
            Assert.True(label.Bottom <= WalkthroughArtifacts.Height - 18);
            Assert.DoesNotContain(callouts, callout => label.Intersects(callout.Bounds));
        });
        for (var index = 0; index < labels.Length; index++)
        for (var other = index + 1; other < labels.Length; other++)
            Assert.False(labels[index].Intersects(labels[other]));
    }

    [Fact]
    public void CloseupCaptionsStayInOneColumnBesideTheStripAndKeepTheirNumbers()
    {
        var callouts = new[]
        {
            new WalkthroughCaptureCallout("pangloss", "PanGloss built the same reading.", new Rect(250, 130, 210, 54)),
            new WalkthroughCaptureCallout("fix", "Fix opens the other choices for this word.", new Rect(420, 70, 50, 40)),
            new WalkthroughCaptureCallout("opinion", "This is the word opinion.", new Rect(20, 40, 120, 44)),
            new WalkthroughCaptureCallout("word", "The word as it appears in the text.", new Rect(150, 40, 120, 44)),
        };
        using var font = new SKFont(SKTypeface.Default, 16);

        var labels = WalkthroughArtifacts.ArrangeCaptionColumnLabels(callouts, 1000, font, 1, 510);

        Assert.Equal(callouts.Length, labels.Count);
        Assert.All(labels, label => Assert.True(label.Bounds.Left > 480));
        Assert.Single(labels.Select(label => label.MarkerCenter.X).Distinct());
        Assert.Equal("This is the word opinion.", string.Join(" ", labels[0].Lines));
        Assert.Equal("The word as it appears in the text.", string.Join(" ", labels[1].Lines));
        Assert.Contains("Fix", string.Join(" ", labels[2].Lines));
        Assert.Equal("Fix opens the other choices for this word.", string.Join(" ", labels[2].Lines));
        Assert.Equal("PanGloss built the same reading.", string.Join(" ", labels[3].Lines));
        Assert.Equal(["opinion", "word", "fix", "pangloss"], labels.Select(label => label.AutomationId));
        Assert.Equal(new Point(80, 40), labels[0].TargetPoint);
        Assert.Equal(new Point(210, 40), labels[1].TargetPoint);
        Assert.Equal(new Point(445, 110), labels[2].TargetPoint);
        Assert.Equal(new Point(355, 184), labels[3].TargetPoint);
        Assert.True(labels[0].Bounds.Bottom < labels[1].Bounds.Top);
        Assert.True(labels[1].Bounds.Bottom < labels[2].Bounds.Top);
        Assert.True(labels[2].Bounds.Bottom < labels[3].Bounds.Top);
    }

    [Fact]
    public void CaptionColumnLeadersDoNotCrossOrPassThroughOtherCaptions()
    {
        var callouts = new[]
        {
            new WalkthroughCaptureCallout("opinion", "A means FieldWorks approved this reading.", new Rect(65, 53, 32.5, 32.5)),
            new WalkthroughCaptureCallout("word", "The word as it appears in the text.", new Rect(112.5, 10.5, 90, 117.5)),
            new WalkthroughCaptureCallout("fix", "Fix opens the other choices for this word.", new Rect(272.5, 10.5, 225, 117.5)),
            new WalkthroughCaptureCallout("unread", "Unread marks a word whose current reading has not been reviewed.", new Rect(497.5, 10.5, 142.5, 117.5)),
            new WalkthroughCaptureCallout("fieldworks", "FieldWorks shows its morphemes with a gloss below each one.", new Rect(10, 133, 630, 150)),
            new WalkthroughCaptureCallout("pangloss", "PanGloss built the same reading.", new Rect(10, 288, 630, 282.5)),
        };
        using var font = new SKFont(SKTypeface.Default, 40);
        var labels = WalkthroughArtifacts.ArrangeCaptionColumnLabels(callouts, 1700, font, 2.5, 790);
        var stripOffset = new Point(60, 258.5);
        var leaders = WalkthroughArtifacts.ArrangeCaptionColumnLeaderPaths(
            labels, stripOffset, 710, 2.5);

        Assert.All(leaders, leader =>
        {
            var label = labels.Single(label => label.AutomationId == leader.AutomationId);
            Assert.Equal(label.TargetPoint + stripOffset, leader.Points[0]);
            Assert.Equal(leader.Points[0].X, leader.Points[1].X);
            Assert.Equal(leader.Points[1].Y, leader.Points[2].Y);
            Assert.True(leader.Points[2].X > 710);
            Assert.True(leader.Points[3].X < label.Bounds.Left);
        });
        Assert.Equal(leaders.Count, leaders.Select(leader => leader.Points[1].Y).Distinct().Count());
        for (var index = 1; index < 4; index++)
            Assert.Equal(7.5, leaders[index].Points[1].Y - leaders[index - 1].Points[1].Y);
        Assert.All(leaders.Take(4), leader => Assert.True(leader.Points[1].Y < leader.Points[0].Y));
        Assert.All(leaders.Skip(4), leader => Assert.True(leader.Points[1].Y > leader.Points[0].Y));

        for (var index = 0; index < leaders.Count; index++)
        {
            foreach (var target in callouts.Where(callout => callout.AutomationId != leaders[index].AutomationId))
            {
                var targetBounds = new Rect(target.Bounds.X + stripOffset.X, target.Bounds.Y + stripOffset.Y,
                    target.Bounds.Width, target.Bounds.Height);
                for (var pointIndex = 1; pointIndex < leaders[index].Points.Count; pointIndex++)
                    Assert.False(SegmentIntersectsRect(leaders[index].Points[pointIndex - 1],
                        leaders[index].Points[pointIndex], targetBounds),
                        $"Leader '{leaders[index].AutomationId}' passes through target '{target.AutomationId}'.");
            }

            foreach (var label in labels.Where(label => label.AutomationId != leaders[index].AutomationId))
            for (var pointIndex = 1; pointIndex < leaders[index].Points.Count; pointIndex++)
                Assert.False(SegmentIntersectsRect(leaders[index].Points[pointIndex - 1],
                    leaders[index].Points[pointIndex], label.Bounds),
                    $"Leader '{leaders[index].AutomationId}' passes through caption '{label.AutomationId}'.");

            for (var other = index + 1; other < leaders.Count; other++)
                Assert.False(LeaderPathsIntersect(leaders[index], leaders[other]),
                    $"Leaders '{leaders[index].AutomationId}' and '{leaders[other].AutomationId}' intersect.");
        }
    }

    [Fact]
    public void ExplainedWordCardFixCaptionsUsePlainWords()
    {
        var root = FindRepositoryRoot();
        var help = WalkthroughHelpContent.Load(root, "explained-word-card", "en");

        var captions = help.CalloutCaptions.Values
            .SelectMany(step => step.Where(pair => pair.Key.EndsWith("-fix", StringComparison.Ordinal)))
            .Select(pair => pair.Value);

        Assert.NotEmpty(captions);
        Assert.All(captions, caption =>
        {
            Assert.StartsWith("Fix opens the other choices", caption, StringComparison.Ordinal);
            Assert.DoesNotContain("▾", caption);
        });
    }

    [Fact]
    public void ExplainedWordCardCaptionsUseDistinctWindowTerms()
    {
        var root = FindRepositoryRoot();
        var help = WalkthroughHelpContent.Load(root, "explained-word-card", "en");
        var disapproved = help.CalloutCaptions["disapproved-conflict"]
            .Single(pair => pair.Key.EndsWith("-disapproved", StringComparison.Ordinal)).Value;
        var staged = help.CalloutCaptions["unknown-staged"]
            .Single(pair => pair.Key.EndsWith("-staged", StringComparison.Ordinal)).Value;

        Assert.Equal("Disapproved", disapproved);
        Assert.Contains("Staged", help.Description, StringComparison.Ordinal);
        Assert.Contains("Apply to FieldWorks project", staged, StringComparison.Ordinal);
        foreach (var step in help.CalloutCaptions.Values)
            Assert.Equal(step.Count, step.Values.Distinct(StringComparer.Ordinal).Count());
        foreach (var stepId in new[] { "no-stored-new-reading", "no-parse" })
        {
            var fieldworks = help.CalloutCaptions[stepId]
                .Single(pair => pair.Key.EndsWith("-fieldworks", StringComparison.Ordinal)).Value;
            Assert.Equal("FieldWorks analyses appear here.", fieldworks);
        }

        var guide = File.ReadAllText(Path.Combine(root, "help", "en", "guide", "pangloss.md"));
        Assert.Contains("PanGloss parses XAmple and HermitCrab grammars fast. Fully compatible.", guide,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SmallCalloutTargetsHavePaddingAndLargeTargetsKeepTheirBounds()
    {
        var smallTargets = new[]
        {
            new Rect(12, 20, 12, 12),
            new Rect(12, 20, 48, 26),
            new Rect(12, 20, 14, 24),
        };
        var largeTargets = new[] { new Rect(12, 20, 80, 40), new Rect(12, 20, 100, 20) };

        Assert.Equal(
            [new Rect(8, 16, 20, 20), new Rect(8, 16, 56, 34), new Rect(8, 16, 22, 32)],
            smallTargets.Select(WalkthroughArtifacts.PadSmallHighlightTarget));
        Assert.Equal(largeTargets, largeTargets.Select(WalkthroughArtifacts.PadSmallHighlightTarget));

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

            WithStrictBaselineGate(() => Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
                WalkthroughArtifacts.CheckBaseline(baselinePath, tooDifferent, update: false)));
        }
        finally
        {
            File.Delete(baselinePath);
        }
    }

    [Fact]
    public void BaselineComparisonWritesDiagnosticsWithoutFailingWhenStrictGateIsOff()
    {
        var baselinePath = Path.Combine(Path.GetTempPath(), $"walkthrough-baseline-{Guid.NewGuid():N}.png");
        var diagnosticsDirectory = WalkthroughTestFiles.DiagnosticsDirectory;
        var fileName = Path.GetFileNameWithoutExtension(baselinePath);
        var actualPath = Path.Combine(diagnosticsDirectory, $"{fileName}-actual.png");
        var diffPath = Path.Combine(diagnosticsDirectory, $"{fileName}-diff.png");
        var previousStrictGate = Environment.GetEnvironmentVariable(WalkthroughArtifacts.StrictComparisonVariable);
        try
        {
            Environment.SetEnvironmentVariable(WalkthroughArtifacts.StrictComparisonVariable, null);
            File.WriteAllBytes(baselinePath, SolidPng(SKColors.White, 0));
            var changedPixels = (int)Math.Ceiling(
                WalkthroughArtifacts.Width * WalkthroughArtifacts.Height * WalkthroughArtifacts.ChangedPixelTolerance) + 1;
            var diagnostics = new List<string>();

            var exception = Record.Exception(() => WalkthroughArtifacts.CheckBaseline(
                baselinePath, SolidPng(SKColors.White, changedPixels), update: false, report: diagnostics.Add));

            Assert.Null(exception);
            var diagnostic = Assert.Single(diagnostics);
            Assert.Contains($"differs in {changedPixels:N0} pixels", diagnostic, StringComparison.Ordinal);
            Assert.True(File.Exists(actualPath), diagnostic);
            Assert.True(File.Exists(diffPath), diagnostic);
        }
        finally
        {
            Environment.SetEnvironmentVariable(WalkthroughArtifacts.StrictComparisonVariable, previousStrictGate);
            File.Delete(baselinePath);
            File.Delete(actualPath);
            File.Delete(diffPath);
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

            WithStrictBaselineGate(() => Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
                WalkthroughArtifacts.CheckBaseline(baselinePath, SolidPng(SKColors.White, changedPixels), update: false)));
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

            WithStrictBaselineGate(() => Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
                WalkthroughArtifacts.CheckBaseline(baselinePath, actual, update: false, [callout])));
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

            WithStrictBaselineGate(() =>
            {
                var failure = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
                    WalkthroughArtifacts.CheckBaseline(baselinePath, SolidPng(SKColors.White, changedPixels), update: false));
                var actualPath = failure.Message.Split("Actual PNG: ", StringSplitOptions.None)[1]
                    .Split("; Diff PNG: ", StringSplitOptions.None)[0];
                var diffPath = failure.Message.Split("; Diff PNG: ", StringSplitOptions.None).Last();
                Assert.True(File.Exists(actualPath), failure.Message);
                Assert.True(File.Exists(diffPath), failure.Message);
                File.Delete(actualPath);
                File.Delete(diffPath);
            });
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

    private static bool SegmentIntersectsRect(Point start, Point end, Rect rect)
    {
        if (rect.Contains(start) || rect.Contains(end)) return true;
        var topLeft = new Point(rect.Left, rect.Top);
        var topRight = new Point(rect.Right, rect.Top);
        var bottomLeft = new Point(rect.Left, rect.Bottom);
        var bottomRight = new Point(rect.Right, rect.Bottom);
        return SegmentsIntersect(start, end, topLeft, topRight) ||
            SegmentsIntersect(start, end, topRight, bottomRight) ||
            SegmentsIntersect(start, end, bottomRight, bottomLeft) ||
            SegmentsIntersect(start, end, bottomLeft, topLeft);
    }

    private static bool SegmentsIntersect(Point firstStart, Point firstEnd, Point secondStart, Point secondEnd)
    {
        static double Orientation(Point a, Point b, Point c) =>
            (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        static bool OnSegment(Point a, Point b, Point c) =>
            Math.Min(a.X, c.X) <= b.X && b.X <= Math.Max(a.X, c.X) &&
            Math.Min(a.Y, c.Y) <= b.Y && b.Y <= Math.Max(a.Y, c.Y);

        var first = Orientation(firstStart, firstEnd, secondStart);
        var second = Orientation(firstStart, firstEnd, secondEnd);
        var third = Orientation(secondStart, secondEnd, firstStart);
        var fourth = Orientation(secondStart, secondEnd, firstEnd);
        if (first == 0 && OnSegment(firstStart, secondStart, firstEnd)) return true;
        if (second == 0 && OnSegment(firstStart, secondEnd, firstEnd)) return true;
        if (third == 0 && OnSegment(secondStart, firstStart, secondEnd)) return true;
        if (fourth == 0 && OnSegment(secondStart, firstEnd, secondEnd)) return true;
        return (first > 0) != (second > 0) && (third > 0) != (fourth > 0);
    }

    private static bool LeaderPathsIntersect(WalkthroughLeaderPath first, WalkthroughLeaderPath second)
    {
        for (var firstIndex = 1; firstIndex < first.Points.Count; firstIndex++)
        for (var secondIndex = 1; secondIndex < second.Points.Count; secondIndex++)
            if (SegmentsIntersect(first.Points[firstIndex - 1], first.Points[firstIndex],
                    second.Points[secondIndex - 1], second.Points[secondIndex])) return true;
        return false;
    }

    private static void WithStrictBaselineGate(Action test)
    {
        var previousValue = Environment.GetEnvironmentVariable(WalkthroughArtifacts.StrictComparisonVariable);
        try
        {
            Environment.SetEnvironmentVariable(WalkthroughArtifacts.StrictComparisonVariable, "1");
            test();
        }
        finally
        {
            Environment.SetEnvironmentVariable(WalkthroughArtifacts.StrictComparisonVariable, previousValue);
        }
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
