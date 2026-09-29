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
    string Locale, string Title, string Description, IReadOnlyDictionary<string, string> StepCaptions)
{
    public static WalkthroughHelpContent Load(string root, string id, string locale)
    {
        var path = Path.Combine(root, "help", locale, "walkthroughs", $"{id}.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var element = document.RootElement;
        RequireProperties(element, "id", "title", "description", "steps");
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
        return new WalkthroughHelpContent(locale, title, description, steps);
    }

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

internal sealed record WalkthroughManifest(
    string Id, string Locale, string Title, string Description, int Width, int Height, int Fps,
    IReadOnlyList<WalkthroughManifestStep> Steps, ManifestClip? Clip);

internal sealed record WalkthroughManifestStep(
    string Id, string Caption, int StartMs, int EndMs, string Screenshot, string Annotated,
    IReadOnlyList<WalkthroughManifestCallout> Callouts);

internal sealed record WalkthroughManifestCallout(double X, double Y, double Width, double Height, string Label);

internal static class WalkthroughArtifacts
{
    public const int Width = 1280;
    public const int Height = 720;
    public const int Fps = 30;
    private const int ChannelTolerance = 12;
    private const double ChangedPixelTolerance = 0.01;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static WalkthroughCapture Capture(
        string id, int startMs, int durationMs, Window window, IReadOnlyList<WalkthroughCaptureCallout> callouts)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(Width, Height), new Vector(96, 96));
        bitmap.Render(window);
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return new WalkthroughCapture(id, startMs, durationMs, callouts, stream.ToArray());
    }

    public static void Write(
        string repositoryRoot, WalkthroughScript script, WalkthroughHelpContent help,
        IReadOnlyList<WalkthroughCapture> captures)
    {
        var updateBaselines = Environment.GetEnvironmentVariable("MOTIF_WALKTHROUGH_UPDATE_BASELINES") == "1";
        var prepared = captures.Select(capture =>
        {
            if (!help.StepCaptions.TryGetValue(capture.Id, out var caption))
                throw new InvalidDataException($"Help file for '{script.Id}' has no caption for capture '{capture.Id}'.");
            var annotated = Annotate(repositoryRoot, capture.Png, capture.Callouts);
            var baselineRoot = Path.Combine(repositoryRoot, "tests", "SIL.Motif.Tests.App", "Assets",
                "WalkthroughBaselines", script.Id);
            CheckBaseline(Path.Combine(baselineRoot, $"{capture.Id}.png"), capture.Png, updateBaselines);
            CheckBaseline(Path.Combine(baselineRoot, $"{capture.Id}-annotated.png"), annotated, updateBaselines);
            return new PreparedCapture(capture, caption, annotated);
        }).ToArray();

        var configuredOutput = Environment.GetEnvironmentVariable("MOTIF_WALKTHROUGH_OUTPUT");
        if (string.IsNullOrWhiteSpace(configuredOutput)) return;
        var directory = Path.Combine(Path.GetFullPath(configuredOutput), script.Id);
        var screenshots = Path.Combine(directory, "screenshots");
        Directory.CreateDirectory(screenshots);
        var manifestSteps = prepared.Select(item =>
        {
            var imageName = $"{item.Capture.Id}.png";
            var annotatedName = $"{item.Capture.Id}-annotated.png";
            File.WriteAllBytes(Path.Combine(screenshots, imageName), item.Capture.Png);
            File.WriteAllBytes(Path.Combine(screenshots, annotatedName), item.AnnotatedPng);
            return new WalkthroughManifestStep(
                item.Capture.Id, item.Caption, item.Capture.StartMs,
                item.Capture.StartMs + item.Capture.DurationMs,
                $"screenshots/{imageName}", $"screenshots/{annotatedName}",
                item.Capture.Callouts.Select((callout, index) => new WalkthroughManifestCallout(
                    callout.Bounds.X, callout.Bounds.Y, callout.Bounds.Width, callout.Bounds.Height,
                    (index + 1).ToString(CultureInfo.InvariantCulture))).ToArray());
        }).ToArray();

        var clip = Environment.GetEnvironmentVariable("MOTIF_WALKTHROUGH_CLIPS") == "1"
            ? WalkthroughClipComposer.TryCompose(directory, prepared.Select(item => item.Capture).ToArray())
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
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    internal static void CheckBaseline(string path, byte[] actual, bool update)
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
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        {
            var before = expectedBitmap.GetPixel(x, y);
            var after = actualBitmap.GetPixel(x, y);
            if (Math.Abs(before.Red - after.Red) > ChannelTolerance ||
                Math.Abs(before.Green - after.Green) > ChannelTolerance ||
                Math.Abs(before.Blue - after.Blue) > ChannelTolerance)
                changed++;
        }

        var allowed = (int)Math.Ceiling(Width * Height * ChangedPixelTolerance);
        Assert.True(changed <= allowed,
            $"Walkthrough baseline '{path}' differs in {changed:N0} pixels; tolerance is {allowed:N0} pixels.");
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
}

internal sealed record ManifestClip(string Webm, string Mp4, string Webp, string Poster);
