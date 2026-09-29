#:package Svg.Skia@3.0.3

using SkiaSharp;
using Svg.Skia;

return RenderIcons.Run(args);

/// <summary>
/// Re-derives every raster form of the Motif logo from the canonical <c>src/SIL.Motif.App/Assets/motif.svg</c>.
/// Run it as <c>dotnet run --file tools/Icons/render-icons.cs</c> after editing the SVG, and commit the outputs.
/// </summary>
/// <remarks>
/// It writes <c>motif.png</c> at the SVG's own size for the README, <c>motif-icon.png</c> as the 256-pixel square
/// the window shows beside its title, and <c>motif.ico</c> holding 16 to 256 pixels for the Windows apphost and
/// taskbar. Each size is rendered from the vector, never scaled down from a larger raster, so small icons stay sharp.
/// </remarks>
internal static class RenderIcons
{
    private static readonly int[] IcoSizes = [16, 24, 32, 48, 64, 128, 256];

    // Matches the margin the square icon has always had around the logo.
    private const float SquareMargin = 0.03f;

    public static int Run(string[] args)
    {
        var assets = Path.Combine(RepositoryRoot(), "src", "SIL.Motif.App", "Assets");
        using var svg = new SKSvg();
        if (svg.Load(Path.Combine(assets, "motif.svg")) is not { } picture)
        {
            Console.Error.WriteLine("Could not load motif.svg.");
            return 1;
        }

        var bounds = picture.CullRect;
        File.WriteAllBytes(Path.Combine(assets, "motif.png"),
            Render(picture, (int)bounds.Width, (int)bounds.Height, 0f));
        File.WriteAllBytes(Path.Combine(assets, "motif-icon.png"), Render(picture, 256, 256, SquareMargin));
        File.WriteAllBytes(Path.Combine(assets, "motif.ico"),
            Ico(IcoSizes.Select(size => (size, Render(picture, size, size, SquareMargin))).ToList()));
        Console.WriteLine($"Rendered motif.png, motif-icon.png and motif.ico into {assets}.");
        return 0;
    }

    private static byte[] Render(SKPicture picture, int width, int height, float margin)
    {
        var bounds = picture.CullRect;
        var room = 1f - 2f * margin;
        var scale = Math.Min(width * room / bounds.Width, height * room / bounds.Height);
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Translate((width - bounds.Width * scale) / 2f, (height - bounds.Height * scale) / 2f);
        canvas.Scale(scale);
        canvas.DrawPicture(picture);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    // PNG-compressed entries are valid in an ICO on every Windows the apphost supports.
    private static byte[] Ico(IReadOnlyList<(int Size, byte[] Png)> images)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Count);
        var offset = 6 + 16 * images.Count;
        foreach (var (size, png) in images)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }

        foreach (var (_, png) in images)
            writer.Write(png);
        return stream.ToArray();
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Motif.sln")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Run this from inside the Motif repository.");
    }
}
