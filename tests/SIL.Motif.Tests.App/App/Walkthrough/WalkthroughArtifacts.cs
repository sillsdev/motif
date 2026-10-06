using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

internal sealed record WalkthroughHelpContent(
    string Locale, string Title, string Description, IReadOnlyDictionary<string, string> StepCaptions,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> CalloutCaptions)
{
    public static WalkthroughHelpContent Load(string root, string id, string locale)
    {
        var path = Path.Combine(root, "src", "SIL.Motif.Help", "Content", locale, "walkthroughs", $"{id}.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var element = document.RootElement;
        RequireProperties(element, "id", "title", "description", "steps", "callouts");
        if (RequiredString(element, "id") != id) throw new InvalidDataException($"Help file '{path}' has the wrong id.");
        var title = RequiredString(element, "title");
        var description = RequiredString(element, "description");
        var captions = element.GetProperty("steps");
        if (captions.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"Help file '{path}' needs a steps object.");
        var steps = captions.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.Value.GetString())
                ? property.Value.GetString()!
                : throw new InvalidDataException($"Help caption '{property.Name}' in '{path}' must be a non-empty string."),
            StringComparer.Ordinal);
        var calloutCaptions = element.GetProperty("callouts");
        if (calloutCaptions.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Help file '{path}' needs a callouts object.");
        var callouts = calloutCaptions.EnumerateObject().ToDictionary(
            property => property.Name,
            property =>
            {
                if (property.Value.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException($"Help callouts for '{property.Name}' in '{path}' must be an object.");
                return (IReadOnlyDictionary<string, string>)property.Value.EnumerateObject().ToDictionary(
                    item => item.Name,
                    item => item.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.Value.GetString())
                        ? item.Value.GetString()!
                        : throw new InvalidDataException($"Help callout '{item.Name}' in '{path}' must be a non-empty string."),
                    StringComparer.Ordinal);
            }, StringComparer.Ordinal);
        return new WalkthroughHelpContent(locale, title, description, steps, callouts);
    }

    public string CalloutCaption(string stepId, string automationId) =>
        CalloutCaptions.TryGetValue(stepId, out var captions) && captions.TryGetValue(automationId, out var caption)
            ? caption
            : throw new InvalidDataException($"Help file has no caption for callout '{automationId}' in step '{stepId}'.");

    private static string RequiredString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new InvalidDataException($"Walkthrough help field '{name}' must be a non-empty string.");

    private static void RequireProperties(JsonElement element, params string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Walkthrough help must be an object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name) || !allowed.Contains(property.Name, StringComparer.Ordinal))
                throw new InvalidDataException($"Walkthrough help contains duplicate or unknown field '{property.Name}'.");
        }
        foreach (var name in allowed)
            if (!names.Contains(name)) throw new InvalidDataException($"Walkthrough help is missing '{name}'.");
    }
}

internal sealed record WalkthroughCaptureCallout(string AutomationId, string Caption, Rect Bounds);

internal sealed record WalkthroughCaptionLabel(Rect Bounds, IReadOnlyList<string> Lines,
    Point MarkerCenter = default, Point TargetPoint = default, string? AutomationId = null,
    Rect TargetBounds = default);

internal sealed record WalkthroughLeaderPath(string AutomationId, IReadOnlyList<Point> Points);

internal sealed record WalkthroughCaptionColumnLayout(
    IReadOnlyList<WalkthroughCaptureCallout> Callouts, IReadOnlyList<WalkthroughCaptionLabel> Labels,
    IReadOnlyList<WalkthroughLeaderPath> Leaders, Point StripOffset, int Width, int Height);

internal sealed record WalkthroughCapture(
    string Id, int StartMs, int DurationMs, IReadOnlyList<WalkthroughCaptureCallout> Callouts, byte[] Png,
    double Scale = 1, Rect? SourceFrameCropBounds = null);

internal enum WalkthroughClipSegmentKind { Click, Hold, Capture }

internal sealed record WalkthroughClipSegment(
    int StartMs, int DurationMs, byte[] Png, Avalonia.Rect? TargetBounds,
    WalkthroughClipSegmentKind Kind, string? ClickTarget);

internal sealed record WalkthroughManifest(
    string Id, string Locale, string Title, string Description, int Width, int Height, int Fps,
    IReadOnlyList<WalkthroughManifestStep> Steps, ManifestClip? Clip);

internal sealed record WalkthroughManifestStep(
    string Id, string Caption, int StartMs, int EndMs, string Screenshot, string Annotated,
    int Width, int Height, int AnnotatedWidth, int AnnotatedHeight, string CalloutLayout,
    IReadOnlyList<WalkthroughManifestCallout> Callouts);

internal sealed record WalkthroughManifestCallout(double X, double Y, double Width, double Height, string Label)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Caption { get; init; }
}

internal static class WalkthroughArtifacts
{
    /// <summary>Enables failures for pixel mismatches when set to <c>1</c>.</summary>
    internal const string StrictComparisonVariable = "MOTIF_WALKTHROUGH_STRICT_BASELINES";
    public const int Width = 1280;
    public const int Height = 720;
    public const int Fps = 30;
    private const int ChannelTolerance = 3;
    internal const double ChangedPixelTolerance = 0.001;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static WalkthroughCapture Capture(
        string id, int startMs, int durationMs, Window window, IReadOnlyList<WalkthroughCaptureCallout> callouts,
        int? cropPadding = null, double scale = 1)
    {
        var scaledCallouts = callouts.Select(callout => callout with
        {
            Bounds = new Rect(callout.Bounds.X * scale, callout.Bounds.Y * scale,
                callout.Bounds.Width * scale, callout.Bounds.Height * scale),
        }).ToArray();
        var capture = new WalkthroughCapture(id, startMs, durationMs, scaledCallouts,
            CaptureFrame(window, scale), scale);
        return cropPadding is { } padding ? Crop(capture, (int)Math.Ceiling(padding * scale)) : capture;
    }

    internal static Rect PadUnreadHighlightTarget(Rect target) =>
        new(target.X - 4, target.Y - 4, target.Width + 8, target.Height + 8);

    internal static Rect PadSmallHighlightTarget(Rect target) =>
        target.Width < 64 && target.Height < 32 ? PadUnreadHighlightTarget(target) : target;

    internal static byte[] CaptureFrame(Window window, double scale = 1)
    {
        if (scale is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(scale));
        LayoutAssertions.BeforeWalkthroughCapture(window);
        var size = window.Bounds.Size;
        using var bitmap = new RenderTargetBitmap(
            new PixelSize((int)Math.Round(size.Width * scale), (int)Math.Round(size.Height * scale)),
            new Vector(96 * scale, 96 * scale));
        bitmap.Render(window);
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    public static void Write(
        string repositoryRoot, WalkthroughScript script, WalkthroughHelpContent help,
        IReadOnlyList<WalkthroughCapture> captures, IReadOnlyList<WalkthroughClipSegment>? clipSegments = null,
        Action<string>? reportBaselineMismatch = null, bool strictBaselineComparison = false)
    {
        var updateBaselines = Environment.GetEnvironmentVariable("MOTIF_WALKTHROUGH_UPDATE_BASELINES") == "1";
        var prepared = captures.Select(capture =>
        {
            if (!help.StepCaptions.TryGetValue(capture.Id, out var caption))
                throw new InvalidDataException($"Help file for '{script.Id}' has no caption for capture '{capture.Id}'.");
            var annotated = Annotate(repositoryRoot, capture.Png, capture.Callouts, capture.Scale);
            var baselineRoot = Path.Combine(repositoryRoot, "tests", "SIL.Motif.Tests.App", "Assets",
                "WalkthroughBaselines", script.Id);
            CheckBaseline(Path.Combine(baselineRoot, $"{capture.Id}.png"), capture.Png, updateBaselines,
                capture.Callouts, reportBaselineMismatch, strictBaselineComparison);
            CheckBaseline(Path.Combine(baselineRoot, $"{capture.Id}-annotated.png"), annotated, updateBaselines,
                capture.Callouts, reportBaselineMismatch, strictBaselineComparison);
            using var screenshot = SKBitmap.Decode(capture.Png)
                ?? throw new InvalidDataException("Could not read rendered walkthrough PNG.");
            using var annotatedImage = SKBitmap.Decode(annotated)
                ?? throw new InvalidDataException("Could not read annotated walkthrough PNG.");
            return new PreparedCapture(capture, caption, annotated, screenshot.Width, screenshot.Height,
                annotatedImage.Width, annotatedImage.Height);
        }).ToArray();

        var configuredOutput = Environment.GetEnvironmentVariable("MOTIF_WALKTHROUGH_OUTPUT");
        if (string.IsNullOrWhiteSpace(configuredOutput)) return;
        var directory = Path.Combine(Path.GetFullPath(configuredOutput), script.Id);
        var stepsDirectory = Path.Combine(directory, "steps");
        Directory.CreateDirectory(stepsDirectory);
        var manifestSteps = prepared.Select((item, index) =>
        {
            var imageName = $"{index + 1:D2}-{item.Capture.Id}.png";
            var annotatedName = $"{index + 1:D2}-{item.Capture.Id}-annotated.png";
            File.WriteAllBytes(Path.Combine(stepsDirectory, imageName), item.Capture.Png);
            File.WriteAllBytes(Path.Combine(stepsDirectory, annotatedName), item.AnnotatedPng);
            var manifestCallouts = item.Capture.Scale > 1
                ? OrderCaptionColumnCallouts(item.Capture.Callouts, item.Capture.Scale)
                : item.Capture.Callouts;
            return new WalkthroughManifestStep(
                item.Capture.Id, item.Caption, item.Capture.StartMs,
                item.Capture.StartMs + item.Capture.DurationMs,
                $"steps/{imageName}", $"steps/{annotatedName}", item.Width, item.Height,
                item.AnnotatedWidth, item.AnnotatedHeight,
                item.Capture.Scale > 1 ? "side-column" : "target-overlay",
                manifestCallouts.Select((callout, calloutIndex) => new WalkthroughManifestCallout(
                    callout.Bounds.X, callout.Bounds.Y, callout.Bounds.Width, callout.Bounds.Height,
                    (calloutIndex + 1).ToString(CultureInfo.InvariantCulture)) { Caption = callout.Caption }).ToArray());
        }).ToArray();

        var clipsRequested = Environment.GetEnvironmentVariable("MOTIF_WALKTHROUGH_CLIPS") == "1";
        var requireClips = Environment.GetEnvironmentVariable("MOTIF_WALKTHROUGH_REQUIRE_CLIPS") == "1";
        if (requireClips && !clipsRequested)
            throw new InvalidOperationException("Required walkthrough video output is not enabled.");
        var clip = clipsRequested
            ? WalkthroughClipComposer.TryCompose(directory, clipSegments ?? prepared.Select(item =>
                new WalkthroughClipSegment(item.Capture.StartMs, item.Capture.DurationMs, item.Capture.Png,
                    item.Capture.Callouts.FirstOrDefault()?.Bounds, WalkthroughClipSegmentKind.Capture, null)).ToArray(),
                requireVideo: requireClips)
            : null;
        var manifest = new WalkthroughManifest(script.Id, help.Locale, help.Title, help.Description,
            Width, Height, Fps, manifestSteps, clip);
        File.WriteAllText(Path.Combine(directory, "manifest.json"),
            SerializeManifest(manifest) + Environment.NewLine);
        File.WriteAllText(Path.Combine(directory, $"captions.{help.Locale}.vtt"), BuildWebVtt(prepared), new UTF8Encoding(false));
    }

    internal static string SerializeManifest(WalkthroughManifest manifest) =>
        JsonSerializer.Serialize(manifest, JsonOptions);

    private static byte[] Annotate(
        string repositoryRoot, byte[] png, IReadOnlyList<WalkthroughCaptureCallout> callouts, double scale)
    {
        if (scale > 1) return AnnotateBeside(repositoryRoot, png, callouts, scale);
        using var bitmap = SKBitmap.Decode(png) ?? throw new InvalidDataException("Could not read rendered walkthrough PNG.");
        using var canvas = new SKCanvas(bitmap);
        using var stroke = new SKPaint { Color = new SKColor(198, 55, 49), IsAntialias = true, StrokeWidth = 3, Style = SKPaintStyle.Stroke };
        using var fill = new SKPaint { Color = new SKColor(198, 55, 49), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var typeface = LoadBundledFont("Andika-Bold.ttf");
        using var number = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var font = new SKFont(typeface, 20);
        using var captionFont = new SKFont(typeface, 16);
        var labels = ArrangeCaptionLabels(callouts, bitmap.Width, bitmap.Height, captionFont);

        for (var index = 0; index < callouts.Count; index++)
        {
            var bounds = callouts[index].Bounds;
            var box = new SKRect((float)bounds.X, (float)bounds.Y,
                (float)(bounds.X + bounds.Width), (float)(bounds.Y + bounds.Height));
            canvas.DrawRoundRect(box, 5, 5, stroke);
            var markerX = Math.Clamp(box.Left, 18, bitmap.Width - 18);
            var markerY = Math.Clamp(box.Top, 18, bitmap.Height - 18);
            canvas.DrawCircle(markerX, markerY, 15, fill);
            canvas.DrawText((index + 1).ToString(CultureInfo.InvariantCulture), markerX, markerY + 7,
                SKTextAlign.Center, font, number);
            var label = labels[index];
            var captionBox = new SKRect((float)label.Bounds.Left, (float)label.Bounds.Top,
                (float)label.Bounds.Right, (float)label.Bounds.Bottom);
            canvas.DrawRoundRect(captionBox, 5, 5, fill);
            for (var line = 0; line < label.Lines.Count; line++)
                canvas.DrawText(label.Lines[line], captionBox.Left + 10, captionBox.Top + 20 + line * 19,
                    SKTextAlign.Left, captionFont, number);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static byte[] AnnotateBeside(
        string repositoryRoot, byte[] png, IReadOnlyList<WalkthroughCaptureCallout> callouts, double scale)
    {
        using var strip = SKBitmap.Decode(png)
            ?? throw new InvalidDataException("Could not read rendered walkthrough PNG.");
        using var typeface = LoadBundledFont("Andika-Bold.ttf");
        using var numberPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var captionPaint = new SKPaint { Color = new SKColor(55, 45, 42), IsAntialias = true };
        using var leaderPaint = new SKPaint
        {
            Color = new SKColor(198, 55, 49), IsAntialias = true, StrokeWidth = (float)(2 * scale),
        };
        using var targetPaint = new SKPaint
        {
            Color = new SKColor(198, 55, 49), IsAntialias = true,
            StrokeWidth = (float)(2 * scale), Style = SKPaintStyle.Stroke,
        };
        using var labelFill = new SKPaint
        {
            Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Fill,
        };
        using var labelBorder = new SKPaint
        {
            Color = new SKColor(198, 55, 49), IsAntialias = true,
            StrokeWidth = (float)(2 * scale), Style = SKPaintStyle.Stroke,
        };
        using var markerFill = new SKPaint
        {
            Color = new SKColor(198, 55, 49), IsAntialias = true, Style = SKPaintStyle.Fill,
        };
        using var numberFont = new SKFont(typeface, (float)(20 * scale));
        using var captionFont = new SKFont(typeface, (float)(16 * scale));
        var layout = ArrangeCaptionColumnLayout(callouts, strip.Width, strip.Height, captionFont, scale);
        using var output = new SKBitmap(layout.Width, layout.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using (var canvas = new SKCanvas(output))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(strip, (float)layout.StripOffset.X, (float)layout.StripOffset.Y);

            for (var index = 0; index < layout.Callouts.Count; index++)
            {
                var callout = layout.Callouts[index];
                var target = new SKRect(
                    (float)(callout.Bounds.Left + layout.StripOffset.X),
                    (float)(callout.Bounds.Top + layout.StripOffset.Y),
                    (float)(callout.Bounds.Right + layout.StripOffset.X),
                    (float)(callout.Bounds.Bottom + layout.StripOffset.Y));
                var label = layout.Labels[index];
                var marker = new SKPoint((float)label.MarkerCenter.X, (float)label.MarkerCenter.Y);
                var leader = layout.Leaders[index];
                for (var pointIndex = 1; pointIndex < leader.Points.Count; pointIndex++)
                {
                    var start = leader.Points[pointIndex - 1];
                    var end = leader.Points[pointIndex];
                    canvas.DrawLine((float)start.X, (float)start.Y, (float)end.X, (float)end.Y, leaderPaint);
                }
                canvas.DrawRoundRect(target, (float)(4 * scale), (float)(4 * scale), targetPaint);

                var group = new SKRect((float)label.Bounds.Left, (float)label.Bounds.Top,
                    (float)label.Bounds.Right, (float)label.Bounds.Bottom);
                canvas.DrawRoundRect(group, (float)(8 * scale), (float)(8 * scale), labelFill);
                canvas.DrawRoundRect(group, (float)(8 * scale), (float)(8 * scale), labelBorder);
                canvas.DrawCircle(marker, (float)(15 * scale), markerFill);
                canvas.DrawText((index + 1).ToString(CultureInfo.InvariantCulture),
                    marker.X, marker.Y + (float)(7 * scale), SKTextAlign.Center, numberFont, numberPaint);

                var textLeft = (float)(label.Bounds.Left + 40 * scale);
                var lineHeight = (float)(20 * scale);
                var metrics = captionFont.Metrics;
                var totalTextHeight = lineHeight * label.Lines.Count;
                var firstBaseline = (float)(label.Bounds.Top + (label.Bounds.Height - totalTextHeight) / 2 - metrics.Ascent);
                for (var line = 0; line < label.Lines.Count; line++)
                {
                    var baseline = firstBaseline + line * lineHeight;
                    var captionLine = label.Lines[line];
                    canvas.DrawText(captionLine, textLeft, baseline, SKTextAlign.Left, captionFont, captionPaint);
                }
            }
        }

        using var image = SKImage.FromBitmap(output);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    internal static void ValidateCaptionColumnLayout(string repositoryRoot, WalkthroughCapture capture)
    {
        using var strip = SKBitmap.Decode(capture.Png)
            ?? throw new InvalidDataException("Could not read rendered walkthrough PNG.");
        using var typeface = LoadBundledFont("Andika-Bold.ttf");
        using var captionFont = new SKFont(typeface, (float)(16 * capture.Scale));
        var layout = ArrangeCaptionColumnLayout(capture.Callouts, strip.Width, strip.Height, captionFont, capture.Scale);
        var repeatedCaption = layout.Callouts.GroupBy(callout => callout.Caption, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (repeatedCaption is not null)
            throw new InvalidOperationException(
                $"Capture '{capture.Id}' repeats caption '{repeatedCaption.Key}'.");

        var targets = layout.Callouts.ToDictionary(callout => callout.AutomationId, callout =>
            new Rect(callout.Bounds.X + layout.StripOffset.X, callout.Bounds.Y + layout.StripOffset.Y,
                callout.Bounds.Width, callout.Bounds.Height), StringComparer.Ordinal);
        for (var index = 0; index < layout.Labels.Count; index++)
        {
            var label = layout.Labels[index];
            if (label.Bounds.X < 0 || label.Bounds.Y < 0 || label.Bounds.Right > layout.Width ||
                label.Bounds.Bottom > layout.Height)
                throw new InvalidOperationException(
                    $"Capture '{capture.Id}' caption '{label.AutomationId}' lies outside the annotated frame.");
            foreach (var target in targets.Where(target => target.Key != label.AutomationId))
                if (label.Bounds.Intersects(target.Value))
                    throw new InvalidOperationException(
                        $"Capture '{capture.Id}' caption '{label.AutomationId}' covers target '{target.Key}'.");
            for (var other = index + 1; other < layout.Labels.Count; other++)
                if (label.Bounds.Intersects(layout.Labels[other].Bounds))
                    throw new InvalidOperationException(
                        $"Capture '{capture.Id}' captions '{label.AutomationId}' and " +
                        $"'{layout.Labels[other].AutomationId}' overlap.");
        }

        for (var index = 0; index < layout.Leaders.Count; index++)
        {
            var leader = layout.Leaders[index];
            foreach (var target in targets.Where(target => target.Key != leader.AutomationId))
                if (LeaderIntersectsRect(leader, target.Value))
                    throw new InvalidOperationException(
                        $"Capture '{capture.Id}' leader '{leader.AutomationId}' passes through target '{target.Key}'.");
            foreach (var label in layout.Labels.Where(label => label.AutomationId != leader.AutomationId))
                if (LeaderIntersectsRect(leader, label.Bounds))
                    throw new InvalidOperationException(
                        $"Capture '{capture.Id}' leader '{leader.AutomationId}' passes through caption " +
                        $"'{label.AutomationId}'.");
            for (var other = index + 1; other < layout.Leaders.Count; other++)
                if (LeaderPathsIntersect(leader, layout.Leaders[other]))
                    throw new InvalidOperationException(
                        $"Capture '{capture.Id}' leaders '{leader.AutomationId}' and " +
                        $"'{layout.Leaders[other].AutomationId}' intersect: " +
                        $"{FormatLeader(leader)}; {FormatLeader(layout.Leaders[other])}.");
        }
    }

    private static WalkthroughCaptionColumnLayout ArrangeCaptionColumnLayout(
        IReadOnlyList<WalkthroughCaptureCallout> callouts, int stripWidth, int stripHeight,
        SKFont captionFont, double scale)
    {
        const double logicalMargin = 24;
        const double logicalGap = 32;
        const double logicalColumnWidth = 340;
        var margin = logicalMargin * scale;
        var columnX = margin + stripWidth + logicalGap * scale;
        var canvasWidth = (int)Math.Ceiling(columnX + logicalColumnWidth * scale + margin);
        var orderedCallouts = OrderCaptionColumnCallouts(callouts, scale);
        var labels = ArrangeCaptionColumnLabels(orderedCallouts, canvasWidth, captionFont, scale, columnX);
        var columnBottom = labels.Count == 0 ? margin : labels.Max(label => label.Bounds.Bottom);
        var canvasHeight = (int)Math.Ceiling(Math.Max(stripHeight + margin * 2, columnBottom + margin));
        var stripOffset = new Point(margin, (canvasHeight - stripHeight) / 2d);
        var leaders = ArrangeCaptionColumnLeaderPaths(labels, stripOffset, margin + stripWidth, scale);
        return new WalkthroughCaptionColumnLayout(orderedCallouts, labels, leaders, stripOffset,
            canvasWidth, canvasHeight);
    }

    internal static IReadOnlyList<WalkthroughCaptionLabel> ArrangeCaptionColumnLabels(
        IReadOnlyList<WalkthroughCaptureCallout> callouts, int canvasWidth, SKFont captionFont,
        double scale, double columnX)
    {
        var markerDiameter = 30 * scale;
        var textGap = 12 * scale;
        var columnWidth = Math.Min(340 * scale, canvasWidth - columnX - 24 * scale);
        var textWidth = columnWidth - markerDiameter - textGap - 14 * scale;
        var lineHeight = 20 * scale;
        var rowGap = 8 * scale;
        var padding = 12 * scale;
        var y = 24 * scale;
        var labels = new List<WalkthroughCaptionLabel>(callouts.Count);

        var orderedCallouts = OrderCaptionColumnCallouts(callouts, scale);
        var rows = GroupCaptionRows(orderedCallouts, callout => callout.Bounds, 12 * scale);
        var targetPoints = new Dictionary<string, Point>(StringComparer.Ordinal);
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            foreach (var callout in rows[rowIndex])
            {
                var target = callout.Bounds;
                targetPoints[callout.AutomationId] = new Point(target.Center.X,
                    rowIndex == 0 ? target.Top : target.Bottom);
            }
        }

        foreach (var callout in orderedCallouts)
        {
            var lines = WrapCaption(callout.Caption, captionFont, (int)textWidth);
            var rowHeight = Math.Max(40 * scale, lines.Count * lineHeight + padding * 2);
            var bounds = new Rect(columnX, y, columnWidth, rowHeight);
            var markerCenter = new Point(columnX + markerDiameter / 2, y + rowHeight / 2);
            var target = callout.Bounds;
            labels.Add(new WalkthroughCaptionLabel(bounds, lines, markerCenter,
                targetPoints[callout.AutomationId], callout.AutomationId, target));
            y += rowHeight + rowGap;
        }

        return labels;
    }

    internal static IReadOnlyList<WalkthroughLeaderPath> ArrangeCaptionColumnLeaderPaths(
        IReadOnlyList<WalkthroughCaptionLabel> labels, Point targetOffset, double stripRight, double scale)
    {
        var exitX = stripRight + 8 * scale;
        var rows = GroupCaptionRows(labels, label => label.TargetBounds, 12 * scale);

        var gutterYs = new Dictionary<string, double>(StringComparer.Ordinal);
        var stagger = 3 * scale;
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex].OrderBy(label => label.TargetBounds.Left).ToArray();
            if (rowIndex == 0)
            {
                var rowTop = row.Min(label => label.TargetBounds.Top) + targetOffset.Y;
                for (var index = 0; index < row.Length; index++)
                    gutterYs[row[index].AutomationId ?? string.Empty] = rowTop - stagger * (row.Length - index);
                continue;
            }

            var rowBottom = row.Max(label => label.TargetBounds.Bottom) + targetOffset.Y;
            if (rowIndex == rows.Count - 1)
            {
                for (var index = 0; index < row.Length; index++)
                    gutterYs[row[index].AutomationId ?? string.Empty] = rowBottom + stagger * (index + 1);
                continue;
            }

            var nextTop = rows[rowIndex + 1].Min(label => label.TargetBounds.Top) + targetOffset.Y;
            var gutterHeight = nextTop - rowBottom;
            if (gutterHeight <= 0)
                throw new InvalidOperationException(
                    $"Caption target rows need a gutter between '{string.Join(",", row.Select(label => label.AutomationId))}' " +
                    $"at {rowBottom:0.##} and '{string.Join(",", rows[rowIndex + 1].Select(label => label.AutomationId))}' " +
                    $"at {nextTop:0.##}.");
            for (var index = 0; index < row.Length; index++)
                gutterYs[row[index].AutomationId ?? string.Empty] =
                    rowBottom + gutterHeight * (index + 1) / (row.Length + 1);
        }

        return labels.Select(label =>
        {
            var start = new Point(label.TargetPoint.X + targetOffset.X, label.TargetPoint.Y + targetOffset.Y);
            var gutter = gutterYs[label.AutomationId ?? string.Empty];
            var verticalLeg = new Point(start.X, gutter);
            var exit = new Point(exitX, gutter);
            var entry = new Point(label.Bounds.Left - 2 * scale, label.MarkerCenter.Y);
            return new WalkthroughLeaderPath(label.AutomationId ?? string.Empty,
                [start, verticalLeg, exit, entry, label.MarkerCenter]);
        }).ToArray();
    }

    private static IReadOnlyList<T> OrderCaptionColumnCallouts<T>(IReadOnlyList<T> callouts,
        Func<T, Rect> bounds, double rowTolerance) => GroupCaptionRows(callouts, bounds, rowTolerance)
        .SelectMany(row => row.OrderBy(callout => bounds(callout).Left)
            .ThenBy(callout => bounds(callout).Right))
        .ToArray();

    private static IReadOnlyList<WalkthroughCaptureCallout> OrderCaptionColumnCallouts(
        IReadOnlyList<WalkthroughCaptureCallout> callouts, double scale) => OrderCaptionColumnCallouts(callouts,
        callout => callout.Bounds, 12 * scale);

    private static List<List<T>> GroupCaptionRows<T>(
        IReadOnlyList<T> items, Func<T, Rect> bounds, double rowTolerance)
    {
        var rows = new List<List<T>>();
        foreach (var item in items.OrderBy(item => bounds(item).Center.Y).ThenBy(item => bounds(item).Left))
        {
            if (rows.Count == 0 ||
                Math.Abs(bounds(item).Center.Y - bounds(rows[^1][0]).Center.Y) > rowTolerance)
                rows.Add([]);
            rows[^1].Add(item);
        }
        return rows;
    }

    private static bool LeaderIntersectsRect(WalkthroughLeaderPath leader, Rect target)
    {
        for (var index = 1; index < leader.Points.Count; index++)
            if (SegmentIntersectsRect(leader.Points[index - 1], leader.Points[index], target)) return true;
        return false;
    }

    private static string FormatLeader(WalkthroughLeaderPath leader) =>
        string.Join(" -> ", leader.Points.Select(point => $"({point.X:0.##},{point.Y:0.##})"));

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

    private static bool LeaderPathsIntersect(WalkthroughLeaderPath first, WalkthroughLeaderPath second)
    {
        for (var firstIndex = 1; firstIndex < first.Points.Count; firstIndex++)
        for (var secondIndex = 1; secondIndex < second.Points.Count; secondIndex++)
            if (SegmentsIntersect(first.Points[firstIndex - 1], first.Points[firstIndex],
                    second.Points[secondIndex - 1], second.Points[secondIndex])) return true;
        return false;
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

    internal static IReadOnlyList<WalkthroughCaptionLabel> ArrangeCaptionLabels(
        IReadOnlyList<WalkthroughCaptureCallout> callouts, int canvasWidth, int canvasHeight, SKFont captionFont)
    {
        const int margin = 18;
        const int gap = 8;
        const int maximumLabelWidth = 360;
        const int horizontalPadding = 20;
        const int lineHeight = 19;
        var maximumTextWidth = Math.Min(maximumLabelWidth, canvasWidth - margin * 2) - horizontalPadding;
        var targets = callouts.Select(callout => callout.Bounds).ToArray();
        var placed = new List<Rect>(callouts.Count);
        var labels = new List<WalkthroughCaptionLabel>(callouts.Count);

        foreach (var callout in callouts)
        {
            var lines = WrapCaption(callout.Caption, captionFont, maximumTextWidth);
            var textWidth = lines.Max(line => captionFont.MeasureText(line));
            var labelWidth = Math.Min(maximumLabelWidth, Math.Max(84, textWidth + horizontalPadding));
            var labelHeight = lines.Count == 1 ? 30 : 14 + lines.Count * lineHeight;
            var target = callout.Bounds;
            var maximumX = canvasWidth - margin - labelWidth;
            var maximumY = canvasHeight - margin - labelHeight;
            var aboveY = target.Top - labelHeight - gap;
            var belowY = target.Bottom + gap;
            var positions = new List<Rect>
            {
                new(Math.Clamp(target.Left, margin, maximumX), Math.Clamp(aboveY, margin, maximumY), labelWidth, labelHeight),
                new(Math.Clamp(target.Right - labelWidth, margin, maximumX), Math.Clamp(aboveY, margin, maximumY), labelWidth, labelHeight),
                new(Math.Clamp(target.Left, margin, maximumX), Math.Clamp(belowY, margin, maximumY), labelWidth, labelHeight),
                new(Math.Clamp(target.Right - labelWidth, margin, maximumX), Math.Clamp(belowY, margin, maximumY), labelWidth, labelHeight),
                new(Math.Clamp(target.Left - labelWidth - gap, margin, maximumX), Math.Clamp(target.Top, margin, maximumY), labelWidth, labelHeight),
                new(Math.Clamp(target.Right + gap, margin, maximumX), Math.Clamp(target.Top, margin, maximumY), labelWidth, labelHeight),
                new(Math.Clamp(target.Left - labelWidth - gap, margin, maximumX), Math.Clamp(target.Bottom - labelHeight, margin, maximumY), labelWidth, labelHeight),
                new(Math.Clamp(target.Right + gap, margin, maximumX), Math.Clamp(target.Bottom - labelHeight, margin, maximumY), labelWidth, labelHeight),
            };
            Rect? selected = positions.Where(IsAvailable).Select(position => (Rect?)position).FirstOrDefault();
            if (selected is null)
            {
                Rect? grid = GridPositions(maximumX, maximumY, labelWidth, labelHeight, margin)
                    .OrderBy(position => Distance(position, target))
                    .Where(IsAvailable)
                    .Select(position => (Rect?)position)
                    .FirstOrDefault();
                if (grid is null)
                    throw new InvalidDataException("Walkthrough callout labels do not fit without overlapping.");
                selected = grid;
            }

            placed.Add(selected.Value);
            labels.Add(new WalkthroughCaptionLabel(selected.Value, lines));

            bool IsAvailable(Rect position) => position.X >= margin && position.Y >= margin &&
                position.Right <= canvasWidth - margin && position.Bottom <= canvasHeight - margin &&
                targets.All(bounds => !position.Intersects(bounds)) &&
                placed.All(bounds => !position.Intersects(bounds));
        }

        return labels;
    }

    private static IReadOnlyList<string> WrapCaption(string caption, SKFont font, int maximumWidth)
    {
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in caption.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && font.MeasureText(candidate) > maximumWidth)
            {
                lines.Add(current);
                current = word;
            }
            else current = candidate;
        }

        if (current.Length > 0) lines.Add(current);
        return lines;
    }

    private static IEnumerable<Rect> GridPositions(
        double maximumX, double maximumY, double width, double height, int margin)
    {
        var xPositions = Enumerable.Range(0, (int)((maximumX - margin) / 8) + 1)
            .Select(index => (double)margin + index * 8).Append(maximumX).Distinct();
        var yPositions = Enumerable.Range(0, (int)((maximumY - margin) / 8) + 1)
            .Select(index => (double)margin + index * 8).Append(maximumY).Distinct();
        return from y in yPositions
            from x in xPositions
            select new Rect(x, y, width, height);
    }

    private static double Distance(Rect label, Rect target) =>
        Math.Abs(label.Center.X - target.Center.X) + Math.Abs(label.Center.Y - target.Center.Y);

    internal static void CheckBaseline(
        string path, byte[] actual, bool update, IReadOnlyList<WalkthroughCaptureCallout>? callouts = null,
        Action<string>? report = null, bool strictBaselineComparison = false)
    {
        if (update)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, actual);
            return;
        }

        if (!File.Exists(path))
            throw new Xunit.Sdk.XunitException(
                $"Walkthrough baseline is missing: {path}. Set MOTIF_WALKTHROUGH_UPDATE_BASELINES=1 to create it.");
        using var expectedBitmap = SKBitmap.Decode(path);
        using var actualBitmap = SKBitmap.Decode(actual);
        Assert.NotNull(expectedBitmap);
        Assert.NotNull(actualBitmap);
        if ((expectedBitmap!.Width, expectedBitmap.Height) != (actualBitmap!.Width, actualBitmap.Height))
        {
            using var dimensionDiff = CreateDimensionDiff(expectedBitmap, actualBitmap!);
            var actualPath = WriteActualPng(path, actual);
            var diffPath = WriteDiffPng(path, dimensionDiff);
            var message = $"Walkthrough baseline '{path}' is {expectedBitmap.Width}x{expectedBitmap.Height} but the capture is " +
                $"{actualBitmap.Width}x{actualBitmap.Height}. Actual PNG: {actualPath}; Diff PNG: {diffPath}; callouts: " +
                string.Join("; ", (callouts ?? []).Select(callout => $"{callout.AutomationId} {callout.Bounds}"));
            if (strictBaselineComparison || Environment.GetEnvironmentVariable(StrictComparisonVariable) == "1")
                throw new Xunit.Sdk.XunitException(message);
            if (report is null) Console.WriteLine(message);
            else report(message);
            return;
        }
        var width = actualBitmap.Width;
        var height = actualBitmap.Height;
        var changed = 0;
        var calloutChanged = 0;
        using var expectedRgba = expectedBitmap!.Copy(SKColorType.Rgba8888) ??
            throw new InvalidOperationException("The walkthrough baseline could not be read as RGBA.");
        using var actualRgba = actualBitmap!.Copy(SKColorType.Rgba8888) ??
            throw new InvalidOperationException("The walkthrough capture could not be read as RGBA.");
        var calloutMask = BuildCalloutMask(width, height, callouts);
        var expectedPixels = expectedRgba.GetPixelSpan();
        var actualPixels = actualRgba.GetPixelSpan();
        var expectedRowBytes = expectedRgba.RowBytes;
        var actualRowBytes = actualRgba.RowBytes;
        using var diffBitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        diffBitmap.Erase(new SKColor(0, 0, 0, 0));
        var diffPixels = diffBitmap.GetPixelSpan();
        for (var y = 0; y < height; y++)
        {
            var expectedRow = expectedPixels.Slice(y * expectedRowBytes, width * 4);
            var actualRow = actualPixels.Slice(y * actualRowBytes, width * 4);
            var diffRow = diffPixels.Slice(y * diffBitmap.RowBytes, width * 4);
            var maskRowOffset = y * width;
            for (var x = 0; x < width; x++)
            {
                var pixelOffset = x * 4;
                var isCallout = calloutMask[maskRowOffset + x] != 0;
                var tolerance = isCallout ? 0 : ChannelTolerance;
                var differs = Math.Abs(expectedRow[pixelOffset] - actualRow[pixelOffset]) > tolerance ||
                    Math.Abs(expectedRow[pixelOffset + 1] - actualRow[pixelOffset + 1]) > tolerance ||
                    Math.Abs(expectedRow[pixelOffset + 2] - actualRow[pixelOffset + 2]) > tolerance ||
                    expectedRow[pixelOffset + 3] != actualRow[pixelOffset + 3];
                if (differs)
                {
                    changed++;
                    if (isCallout) calloutChanged++;
                    diffRow[pixelOffset] = 255;
                    diffRow[pixelOffset + 1] = 0;
                    diffRow[pixelOffset + 2] = 128;
                    diffRow[pixelOffset + 3] = 255;
                }
            }
        }

        var allowed = (int)Math.Ceiling(width * height * ChangedPixelTolerance);
        if (calloutChanged > 0 || changed > allowed)
        {
            var actualPath = WriteActualPng(path, actual);
            var diffPath = WriteDiffPng(path, diffBitmap);
            var message =
                $"Walkthrough baseline '{path}' differs in {changed:N0} pixels ({calloutChanged:N0} in callouts); " +
                $"tolerance is {allowed:N0} pixels. Actual PNG: {actualPath}; Diff PNG: {diffPath}";
            if (strictBaselineComparison || Environment.GetEnvironmentVariable(StrictComparisonVariable) == "1")
                throw new Xunit.Sdk.XunitException(message);
            if (report is null) Console.WriteLine(message);
            else report(message);
        }
    }

    private static byte[] BuildCalloutMask(int width, int height,
        IReadOnlyList<WalkthroughCaptureCallout>? callouts)
    {
        var mask = new byte[width * height];
        if (callouts is null) return mask;
        foreach (var callout in callouts)
        {
            var bounds = callout.Bounds;
            if (double.IsNaN(bounds.X) || double.IsNaN(bounds.Y) ||
                double.IsNaN(bounds.Right) || double.IsNaN(bounds.Bottom)) continue;
            var left = FirstPixelCenterAtOrAfter(bounds.X, width);
            var top = FirstPixelCenterAtOrAfter(bounds.Y, height);
            var right = FirstPixelCenterAtOrAfter(bounds.Right, width);
            var bottom = FirstPixelCenterAtOrAfter(bounds.Bottom, height);
            if (right <= left || bottom <= top) continue;
            for (var y = top; y < bottom; y++)
                Array.Fill(mask, (byte)1, y * width + left, right - left);
        }
        return mask;
    }

    private static int FirstPixelCenterAtOrAfter(double edge, int limit)
    {
        var index = Math.Ceiling(edge - 0.5);
        if (index <= 0) return 0;
        if (index >= limit) return limit;
        return (int)index;
    }

    private static string WriteActualPng(string baselinePath, byte[] actual)
    {
        var path = DiagnosticPngPath(baselinePath, "actual");
        File.WriteAllBytes(path, actual);
        return path;
    }

    private static string WriteDiffPng(string baselinePath, SKBitmap diffBitmap)
    {
        var path = DiagnosticPngPath(baselinePath, "diff");
        using var image = SKImage.FromBitmap(diffBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    private static SKBitmap CreateDimensionDiff(SKBitmap expected, SKBitmap actual)
    {
        var diff = new SKBitmap(
            Math.Max(expected.Width, actual.Width), Math.Max(expected.Height, actual.Height),
            SKColorType.Rgba8888, SKAlphaType.Unpremul);
        diff.Erase(new SKColor(0, 0, 0, 0));
        for (var y = 0; y < diff.Height; y++)
        for (var x = 0; x < diff.Width; x++)
        {
            var outsideExpected = x >= expected.Width || y >= expected.Height;
            var outsideActual = x >= actual.Width || y >= actual.Height;
            var differs = outsideExpected || outsideActual;
            if (!differs)
            {
                var before = expected.GetPixel(x, y);
                var after = actual.GetPixel(x, y);
                differs = Math.Abs(before.Red - after.Red) > ChannelTolerance ||
                    Math.Abs(before.Green - after.Green) > ChannelTolerance ||
                    Math.Abs(before.Blue - after.Blue) > ChannelTolerance || before.Alpha != after.Alpha;
            }
            if (differs) diff.SetPixel(x, y, new SKColor(255, 0, 128));
        }
        return diff;
    }

    private static string DiagnosticPngPath(string baselinePath, string suffix)
    {
        var directory = WalkthroughTestFiles.DiagnosticsDirectory;
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, Path.GetFileNameWithoutExtension(baselinePath) + $"-{suffix}.png");
    }

    private static string BuildWebVtt(IReadOnlyList<PreparedCapture> captures)
    {
        var builder = new StringBuilder("WEBVTT\n\n");
        foreach (var item in captures)
        {
            var end = item.Capture.StartMs + item.Capture.DurationMs;
            builder.Append(TimeCode(item.Capture.StartMs)).Append(" --> ").Append(TimeCode(end)).Append('\n');
            builder.Append(item.Caption.Replace("-->", "—>", StringComparison.Ordinal)).Append("\n\n");
        }
        return builder.ToString();
    }

    private static string TimeCode(int milliseconds)
    {
        var hours = milliseconds / 3_600_000;
        var minutes = milliseconds / 60_000 % 60;
        var seconds = milliseconds / 1_000 % 60;
        var remainder = milliseconds % 1_000;
        return string.Create(CultureInfo.InvariantCulture, $"{hours:00}:{minutes:00}:{seconds:00}.{remainder:000}");
    }

    private sealed record PreparedCapture(
        WalkthroughCapture Capture, string Caption, byte[] AnnotatedPng, int Width, int Height,
        int AnnotatedWidth, int AnnotatedHeight);

    internal static WalkthroughCapture Crop(WalkthroughCapture capture, int padding)
    {
        if (padding is < 0 or > 256) throw new ArgumentOutOfRangeException(nameof(padding));
        if (capture.Callouts.Count == 0) throw new InvalidDataException("A cropped capture needs at least one callout.");
        using var source = SKBitmap.Decode(capture.Png)
            ?? throw new InvalidDataException("Could not read rendered walkthrough PNG for cropping.");
        var left = Math.Max(0, (int)Math.Floor(capture.Callouts.Min(callout => callout.Bounds.X) - padding));
        var top = Math.Max(0, (int)Math.Floor(capture.Callouts.Min(callout => callout.Bounds.Y) - padding));
        var right = Math.Min(source.Width, (int)Math.Ceiling(capture.Callouts.Max(callout => callout.Bounds.Right) + padding));
        var bottom = Math.Min(source.Height, (int)Math.Ceiling(capture.Callouts.Max(callout => callout.Bounds.Bottom) + padding));
        if (left >= source.Width || top >= source.Height || right <= left || bottom <= top)
            throw new InvalidDataException($"Walkthrough crop '{capture.Id}' lies outside its {source.Width}x{source.Height} frame: " +
                $"left={left}, top={top}, right={right}, bottom={bottom}.");
        var cropWidth = Math.Max(1, right - left);
        var cropHeight = Math.Max(1, bottom - top);
        using var cropped = new SKBitmap(cropWidth, cropHeight);
        using (var cropCanvas = new SKCanvas(cropped))
            cropCanvas.DrawBitmap(source, new SKRect(left, top, right, bottom), new SKRect(0, 0, cropWidth, cropHeight));
        var transformed = capture.Callouts.Select(callout => new WalkthroughCaptureCallout(
            callout.AutomationId, callout.Caption,
            new Avalonia.Rect(
                callout.Bounds.X - left,
                callout.Bounds.Y - top,
                callout.Bounds.Width,
                callout.Bounds.Height))).ToArray();
        using var image = SKImage.FromBitmap(cropped);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return capture with
        {
            Callouts = transformed,
            Png = data.ToArray(),
            SourceFrameCropBounds = new Rect(left, top, cropWidth, cropHeight),
        };
    }

    private static SKTypeface LoadBundledFont(string fileName)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://SIL.Motif.App/Assets/Fonts/{fileName}"));
        return SKTypeface.FromStream(stream)
            ?? throw new InvalidDataException($"Bundled font '{fileName}' could not be loaded.");
    }
}

internal sealed record ManifestClip(string Webm, string Mp4, string Webp, string Poster);
