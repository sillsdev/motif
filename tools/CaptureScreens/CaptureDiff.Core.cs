using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;

namespace SIL.Motif.CaptureScreens;

internal static class CaptureDiff
{
    private const int ChannelTolerance = 1;

    internal static int Run(string[] args) => Run(args, OperatingSystem.IsWindows()
        ? OSPlatform.Windows
        : OperatingSystem.IsMacOS() ? OSPlatform.OSX : OSPlatform.Linux);

    internal static int Run(string[] args, OSPlatform platform)
    {
        try
        {
            var options = ReadOptions(args);
            WriteDiff(options.Current, options.Previous, options.Csv, platform);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
    internal static DiffResult Compare(SKBitmap? previous, SKBitmap? current)
    {
        if (previous is null) return new("added", 0, 0);
        if (current is null) return new("removed", 0, 0);

        var resized = previous.Width != current.Width || previous.Height != current.Height;
        var width = Math.Max(previous.Width, current.Width);
        var height = Math.Max(previous.Height, current.Height);
        long changedPixels = 0;
        for (var y = 0; y < height; y++)
        {
            Span<byte> oldRow = y < previous.Height ? previous.GetPixelSpan(0, y) : Span<byte>.Empty;
            Span<byte> newRow = y < current.Height ? current.GetPixelSpan(0, y) : Span<byte>.Empty;
            for (var x = 0; x < width; x++)
            {
                var oldInside = x < previous.Width && y < previous.Height;
                var newInside = x < current.Width && y < current.Height;
                if (!oldInside || !newInside || ChannelsDiffer(oldRow, x, newRow, x))
                    changedPixels++;
            }
        }

        var status = resized ? "resized" : changedPixels > 0 ? "changed" : "same";
        var percent = width == 0 || height == 0 ? 0 : changedPixels * 100d / ((long)width * height);
        return new(status, changedPixels, percent);
    }

    internal static SKBitmap RenderDiff(SKBitmap previous, SKBitmap current)
    {
        var width = Math.Max(previous.Width, current.Width);
        var height = Math.Max(previous.Height, current.Height);
        var result = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        for (var y = 0; y < height; y++)
        {
            Span<byte> oldRow = y < previous.Height ? previous.GetPixelSpan(0, y) : Span<byte>.Empty;
            Span<byte> newRow = y < current.Height ? current.GetPixelSpan(0, y) : Span<byte>.Empty;
            var resultRow = result.GetPixelSpan(0, y);
            for (var x = 0; x < width; x++)
            {
                var oldInside = x < previous.Width && y < previous.Height;
                var newInside = x < current.Width && y < current.Height;
                var oldOffset = x * 4;
                var newOffset = x * 4;
                var resultOffset = x * 4;
                var changed = !oldInside || !newInside || ChannelsDiffer(oldRow, x, newRow, x);
                if (changed)
                {
                    resultRow[resultOffset] = 255;
                    resultRow[resultOffset + 1] = 40;
                    resultRow[resultOffset + 2] = 110;
                }
                else
                {
                    var background = newInside ? newRow : oldRow;
                    var backgroundOffset = newInside ? newOffset : oldOffset;
                    resultRow[resultOffset] = (byte)(background[backgroundOffset] * 0.38);
                    resultRow[resultOffset + 1] = (byte)(background[backgroundOffset + 1] * 0.38);
                    resultRow[resultOffset + 2] = (byte)(background[backgroundOffset + 2] * 0.38);
                }
                resultRow[resultOffset + 3] = 255;
            }
        }
        return result;
    }

    private static void WriteDiff(string currentRoot, string? previousRoot, string csvPath, OSPlatform platform)
    {
        currentRoot = Path.GetFullPath(currentRoot);
        if (!Directory.Exists(currentRoot)) throw new DirectoryNotFoundException($"Capture folder not found: {currentRoot}");
        if (previousRoot is not null)
        {
            previousRoot = Path.GetFullPath(previousRoot);
            if (!Directory.Exists(previousRoot)) throw new DirectoryNotFoundException($"Previous capture folder not found: {previousRoot}");
        }

        var currentFiles = IndexPngs(currentRoot, platform);
        var previousFiles = previousRoot is null
            ? new Dictionary<string, string>(PathComparer(platform))
            : IndexPngs(previousRoot, platform);
        var allNames = currentFiles.Keys.Concat(previousFiles.Keys)
            .Distinct(PathComparer(platform)).Order(StringComparer.OrdinalIgnoreCase);
        var diffRoot = Path.Combine(currentRoot, "diff");
        Directory.CreateDirectory(diffRoot);
        var rows = new List<DiffRow>();
        foreach (var relative in allNames)
        {
            currentFiles.TryGetValue(relative, out var currentPath);
            previousFiles.TryGetValue(relative, out var previousPath);
            if (currentPath is null || previousPath is null)
            {
                rows.Add(new(relative, currentPath is null ? "removed" : "added", null, null));
                continue;
            }

            using var previous = Decode(previousPath);
            using var current = Decode(currentPath);
            var comparison = Compare(previous, current);
            var differencePath = Path.Combine(diffRoot, ToPlatformPath(relative));
            Directory.CreateDirectory(Path.GetDirectoryName(differencePath)!);
            using var difference = RenderDiff(previous, current);
            using var image = SKImage.FromBitmap(difference);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100)
                ?? throw new InvalidOperationException($"Could not encode pixel diff for {relative}.");
            using var stream = File.Create(differencePath);
            data.SaveTo(stream);
            rows.Add(new(relative, comparison.Status, comparison.ChangedPixels, comparison.ChangedPercent));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(csvPath))!);
        var csv = new StringBuilder("file,status,changedPixels,changedPercent\n");
        foreach (var row in rows)
        {
            csv.Append(Csv(row.File)).Append(',').Append(row.Status).Append(',');
            if (row.ChangedPixels.HasValue) csv.Append(row.ChangedPixels.Value.ToString(CultureInfo.InvariantCulture));
            csv.Append(',');
            if (row.ChangedPercent.HasValue)
                csv.Append(row.ChangedPercent.Value.ToString("0.##", CultureInfo.InvariantCulture));
            csv.Append('\n');
        }
        File.WriteAllText(csvPath, csv.ToString(), new UTF8Encoding(false));
    }

    private static Dictionary<string, string> IndexPngs(string root, OSPlatform platform)
    {
        var diffRoot = Path.GetFullPath(Path.Combine(root, "diff")) + Path.DirectorySeparatorChar;
        var referenceRoot = Path.GetFullPath(Path.Combine(root, "references")) + Path.DirectorySeparatorChar;
        var files = new Dictionary<string, string>(PathComparer(platform));
        foreach (var path in Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories))
        {
            var fullPath = Path.GetFullPath(path);
            if (fullPath.StartsWith(diffRoot, PathComparison(platform)) ||
                fullPath.StartsWith(referenceRoot, PathComparison(platform))) continue;
            var relative = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
            files[relative] = fullPath;
        }
        return files;
    }

    private static SKBitmap Decode(string path)
    {
        using var source = SKBitmap.Decode(path)
            ?? throw new InvalidDataException($"Could not decode PNG capture: {path}");
        var rgba = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        if (source.CopyTo(rgba)) return rgba;
        rgba.Dispose();
        throw new InvalidDataException($"Could not convert PNG capture to RGBA: {path}");
    }

    private static bool ChannelsDiffer(ReadOnlySpan<byte> leftRow, int leftX, ReadOnlySpan<byte> rightRow, int rightX)
    {
        var leftOffset = leftX * 4;
        var rightOffset = rightX * 4;
        return Math.Abs(leftRow[leftOffset] - rightRow[rightOffset]) > ChannelTolerance ||
            Math.Abs(leftRow[leftOffset + 1] - rightRow[rightOffset + 1]) > ChannelTolerance ||
            Math.Abs(leftRow[leftOffset + 2] - rightRow[rightOffset + 2]) > ChannelTolerance ||
            Math.Abs(leftRow[leftOffset + 3] - rightRow[rightOffset + 3]) > ChannelTolerance;
    }

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    private static string ToPlatformPath(string relative) => relative.Replace('/', Path.DirectorySeparatorChar);

    private static StringComparer PathComparer(OSPlatform platform) => PathComparison(platform) == StringComparison.OrdinalIgnoreCase
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static StringComparison PathComparison(OSPlatform platform) =>
        platform == OSPlatform.Windows || platform == OSPlatform.OSX
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static CaptureOptions ReadOptions(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Count; index++)
        {
            var key = args[index];
            if (key is not ("--current" or "--previous" or "--csv"))
                throw new ArgumentException($"Unknown capture diff option: {key}");
            if (index + 1 >= args.Count) throw new ArgumentException($"Missing value for {key}.");
            values[key] = args[++index];
        }

        if (!values.TryGetValue("--current", out var current) || string.IsNullOrWhiteSpace(current))
            throw new ArgumentException("--current is required.");
        if (!values.TryGetValue("--csv", out var csv) || string.IsNullOrWhiteSpace(csv))
            throw new ArgumentException("--csv is required.");
        values.TryGetValue("--previous", out var previous);
        return new(current, previous, csv);
    }

    private sealed record CaptureOptions(string Current, string? Previous, string Csv);
    private sealed record DiffRow(string File, string Status, long? ChangedPixels, double? ChangedPercent);
}

internal sealed record DiffResult(string Status, long ChangedPixels, double ChangedPercent);
