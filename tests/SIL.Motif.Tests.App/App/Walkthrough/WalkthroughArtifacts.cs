using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
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

internal sealed record WalkthroughCapture(
    string Id, int StartMs, int DurationMs, IReadOnlyList<WalkthroughCaptureCallout> Callouts, byte[] Png);

internal enum WalkthroughClipSegmentKind { Click, Hold, Capture }

internal sealed record WalkthroughClipSegment(
    int StartMs, int DurationMs, byte[] Png, Avalonia.Rect? TargetBounds,
    WalkthroughClipSegmentKind Kind, string? ClickTarget);

internal sealed record WalkthroughManifest(
    string Id, string Locale, string Title, string Description, int Width, int Height, int Fps,
    IReadOnlyList<WalkthroughManifestStep> Steps, ManifestClip? Clip);

internal sealed record WalkthroughManifestStep(
    string Id, string Caption, int StartMs, int EndMs, string Screenshot, string Annotated,
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
        int? cropPadding = null)
    {
        var capture = new WalkthroughCapture(id, startMs, durationMs, callouts, CaptureFrame(window));
        return cropPadding is { } padding ? Crop(capture, padding) : capture;
    }

    internal static byte[] CaptureFrame(Window window)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(Width, Height), new Vector(96, 96));
        bitmap.Render(window);
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    public static void Write(
        string repositoryRoot, WalkthroughScript script, WalkthroughHelpContent help,
        IReadOnlyList<WalkthroughCapture> captures, IReadOnlyList<WalkthroughClipSegment>? clipSegments = null,
        Action<string>? reportBaselineMismatch = null)
    {
        var updateBaselines = Environment.GetEnvironmentVariable("MOTIF_WALKTHROUGH_UPDATE_BASELINES") == "1";
        var prepared = captures.Select(capture =>
        {
            if (!help.StepCaptions.TryGetValue(capture.Id, out var caption))
                throw new InvalidDataException($"Help file for '{script.Id}' has no caption for capture '{capture.Id}'.");
            var annotated = Annotate(repositoryRoot, capture.Png, capture.Callouts);
            var baselineRoot = Path.Combine(repositoryRoot, "tests", "SIL.Motif.Tests.App", "Assets",
                "WalkthroughBaselines", script.Id);
            CheckBaseline(Path.Combine(baselineRoot, $"{capture.Id}.png"), capture.Png, updateBaselines,
                capture.Callouts, reportBaselineMismatch);
            CheckBaseline(Path.Combine(baselineRoot, $"{capture.Id}-annotated.png"), annotated, updateBaselines,
                capture.Callouts, reportBaselineMismatch);
            return new PreparedCapture(capture, caption, annotated);
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
            return new WalkthroughManifestStep(
                item.Capture.Id, item.Caption, item.Capture.StartMs,
                item.Capture.StartMs + item.Capture.DurationMs,
                $"steps/{imageName}", $"steps/{annotatedName}",
                item.Capture.Callouts.Select((callout, calloutIndex) => new WalkthroughManifestCallout(
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
        string repositoryRoot, byte[] png, IReadOnlyList<WalkthroughCaptureCallout> callouts)
    {
        using var bitmap = SKBitmap.Decode(png) ?? throw new InvalidDataException("Could not read rendered walkthrough PNG.");
        using var canvas = new SKCanvas(bitmap);
        using var stroke = new SKPaint { Color = new SKColor(198, 55, 49), IsAntialias = true, StrokeWidth = 3, Style = SKPaintStyle.Stroke };
        using var fill = new SKPaint { Color = new SKColor(198, 55, 49), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var typeface = SKTypeface.FromFile(Path.Combine(repositoryRoot, "tests", "SIL.Motif.Tests.App",
            "Assets", "Fonts", "Andika-Bold.ttf"));
        using var number = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var font = new SKFont(typeface, 20);
        using var captionFont = new SKFont(typeface, 16);

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
            var caption = callouts[index].Caption;
            var captionWidth = Math.Min(bitmap.Width - 36, captionFont.MeasureText(caption) + 20);
            var captionX = Math.Clamp(box.Left, 18, bitmap.Width - captionWidth - 18);
            var captionY = Math.Clamp(box.Top - 38, 18, bitmap.Height - 38);
            var captionBox = new SKRect(captionX, captionY, captionX + captionWidth, captionY + 30);
            canvas.DrawRoundRect(captionBox, 5, 5, fill);
            canvas.DrawText(caption, captionBox.Left + 10, captionBox.Top + 20,
                SKTextAlign.Left, captionFont, number);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    internal static void CheckBaseline(
        string path, byte[] actual, bool update, IReadOnlyList<WalkthroughCaptureCallout>? callouts = null,
        Action<string>? report = null)
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
        Assert.Equal((Width, Height), (expectedBitmap!.Width, expectedBitmap.Height));
        Assert.Equal((Width, Height), (actualBitmap!.Width, actualBitmap.Height));
        var changed = 0;
        var calloutChanged = 0;
        using var diffBitmap = new SKBitmap(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        diffBitmap.Erase(new SKColor(0, 0, 0, 0));
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        {
            var before = expectedBitmap.GetPixel(x, y);
            var after = actualBitmap.GetPixel(x, y);
            var isCallout = callouts?.Any(callout =>
                x + 0.5 >= callout.Bounds.X && x + 0.5 < callout.Bounds.Right &&
                y + 0.5 >= callout.Bounds.Y && y + 0.5 < callout.Bounds.Bottom) == true;
            var tolerance = isCallout ? 0 : ChannelTolerance;
            var differs = Math.Abs(before.Red - after.Red) > tolerance ||
                Math.Abs(before.Green - after.Green) > tolerance ||
                Math.Abs(before.Blue - after.Blue) > tolerance || before.Alpha != after.Alpha;
            if (differs)
            {
                changed++;
                if (isCallout) calloutChanged++;
                diffBitmap.SetPixel(x, y, new SKColor(255, 0, 128));
            }
        }

        var allowed = (int)Math.Ceiling(Width * Height * ChangedPixelTolerance);
        if (calloutChanged > 0 || changed > allowed)
        {
            var actualPath = WriteActualPng(path, actual);
            var diffPath = WriteDiffPng(path, diffBitmap);
            var message =
                $"Walkthrough baseline '{path}' differs in {changed:N0} pixels ({calloutChanged:N0} in callouts); " +
                $"tolerance is {allowed:N0} pixels. Actual PNG: {actualPath}; Diff PNG: {diffPath}";
            if (Environment.GetEnvironmentVariable(StrictComparisonVariable) == "1")
                throw new Xunit.Sdk.XunitException(message);
            if (report is null) Console.WriteLine(message);
            else report(message);
        }
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

    private sealed record PreparedCapture(WalkthroughCapture Capture, string Caption, byte[] AnnotatedPng);

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
        var cropWidth = Math.Max(1, right - left);
        var cropHeight = Math.Max(1, bottom - top);
        using var cropped = new SKBitmap(cropWidth, cropHeight);
        using (var cropCanvas = new SKCanvas(cropped))
            cropCanvas.DrawBitmap(source, new SKRect(left, top, right, bottom), new SKRect(0, 0, cropWidth, cropHeight));

        var scale = Math.Min((float)Width / cropWidth, (float)Height / cropHeight);
        var scaledWidth = cropWidth * scale;
        var scaledHeight = cropHeight * scale;
        var offsetX = (Width - scaledWidth) / 2;
        var offsetY = (Height - scaledHeight) / 2;
        using var output = new SKBitmap(Width, Height);
        using (var canvas = new SKCanvas(output))
        {
            canvas.Clear(source.GetPixel(left, top));
            canvas.DrawBitmap(cropped, new SKRect(offsetX, offsetY, offsetX + scaledWidth, offsetY + scaledHeight));
        }
        var transformed = capture.Callouts.Select(callout => new WalkthroughCaptureCallout(
            callout.AutomationId, callout.Caption,
            new Avalonia.Rect(
                (callout.Bounds.X - left) * scale + offsetX,
                (callout.Bounds.Y - top) * scale + offsetY,
                callout.Bounds.Width * scale,
                callout.Bounds.Height * scale))).ToArray();
        using var image = SKImage.FromBitmap(output);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return capture with { Callouts = transformed, Png = data.ToArray() };
    }
}

internal sealed record ManifestClip(string Webm, string Mp4, string Webp, string Poster);
