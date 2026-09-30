using System.ComponentModel;
using System.Diagnostics;
using SkiaSharp;

namespace SIL.Motif.Tests.App.Walkthrough;

internal static class WalkthroughClipComposer
{
    internal static int FramesForSegment(int startMs, int durationMs)
    {
        if (startMs < 0) throw new ArgumentOutOfRangeException(nameof(startMs));
        if (durationMs <= 0) throw new ArgumentOutOfRangeException(nameof(durationMs));
        var startFrame = (long)Math.Round(startMs * WalkthroughArtifacts.Fps / 1000d,
            MidpointRounding.AwayFromZero);
        var endFrame = (long)Math.Round((startMs + (long)durationMs) * WalkthroughArtifacts.Fps / 1000d,
            MidpointRounding.AwayFromZero);
        return Math.Max(1, checked((int)(endFrame - startFrame)));
    }

    public static ManifestClip? TryCompose(
        string outputDirectory, IReadOnlyList<WalkthroughClipSegment> segments, string ffmpegExecutable = "ffmpeg",
        bool requireVideo = false)
    {
        if (!CanRunFfmpeg(ffmpegExecutable))
        {
            const string message = "Required walkthrough video output needs ffmpeg on PATH.";
            if (requireVideo) throw new InvalidOperationException(message);
            Console.WriteLine("Walkthrough clips skipped: ffmpeg is not available on PATH.");
            return null;
        }

        var frameDirectory = Path.Combine(Path.GetTempPath(), "SIL.Motif.WalkthroughFrames", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(frameDirectory);
        try
        {
            var frames = WriteFrames(frameDirectory, segments);
            Directory.CreateDirectory(outputDirectory);
            var webm = Path.Combine(outputDirectory, "clip.webm");
            var mp4 = Path.Combine(outputDirectory, "clip.mp4");
            var webp = Path.Combine(outputDirectory, "clip.webp");
            var poster = Path.Combine(outputDirectory, "poster.png");
            Encode(ffmpegExecutable, frames, webm, "-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0", "-pix_fmt", "yuv420p");
            Encode(ffmpegExecutable, frames, mp4, "-c:v", "libx264", "-preset", "medium", "-crf", "25", "-pix_fmt", "yuv420p", "-movflags", "+faststart");
            Encode(ffmpegExecutable, frames, webp, "-c:v", "libwebp_anim", "-loop", "0", "-quality", "80", "-preset", "picture");
            EncodePoster(ffmpegExecutable, frames, poster);
            return new ManifestClip("clip.webm", "clip.mp4", "clip.webp", "poster.png");
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException or TimeoutException)
        {
            if (requireVideo)
                throw new InvalidOperationException($"Required walkthrough video encoding failed: {exception.Message}", exception);
            Console.WriteLine($"Walkthrough clips skipped: ffmpeg could not encode the requested formats ({exception.Message}).");
            return null;
        }
        finally
        {
            WalkthroughTestFiles.DeleteDirectory(frameDirectory);
        }
    }

    private static IReadOnlyList<string> WriteFrames(
        string frameDirectory, IReadOnlyList<WalkthroughClipSegment> segments)
    {
        var files = new List<string>();
        var timelineEnd = 0;
        foreach (var segment in segments)
        {
            if (segment.StartMs != timelineEnd || segment.DurationMs <= 0)
                throw new InvalidDataException("Walkthrough clip segments must form one positive-duration timeline.");
            using var screenshot = SKBitmap.Decode(segment.Png)
                ?? throw new InvalidDataException("Could not decode a walkthrough timeline frame.");
            var count = FramesForSegment(segment.StartMs, segment.DurationMs);
            for (var index = 0; index < count; index++)
            {
                var progress = count == 1 ? 1f : (float)index / (count - 1);
                var framePath = Path.Combine(frameDirectory, $"frame-{files.Count:D5}.png");
                using var frame = new SKBitmap(WalkthroughArtifacts.Width, WalkthroughArtifacts.Height);
                using (var canvas = new SKCanvas(frame))
                {
                    canvas.Clear(SKColors.White);
                    DrawScene(canvas, screenshot, segment.TargetBounds, progress,
                        segment.Kind == WalkthroughClipSegmentKind.Click && segment.ClickTarget is not null);
                }
                using var image = SKImage.FromBitmap(frame);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(framePath, data.ToArray());
                files.Add(framePath);
            }
            timelineEnd = segment.StartMs + segment.DurationMs;
        }
        return files;
    }

    private static void DrawScene(
        SKCanvas canvas, SKBitmap screenshot, Avalonia.Rect? targetBounds, float progress, bool clicked)
    {
        var target = targetBounds ?? new Avalonia.Rect(0, 0, WalkthroughArtifacts.Width, WalkthroughArtifacts.Height);
        var zoom = targetBounds is null ? 1f : 1f + 0.045f * MathF.Sin(MathF.PI * progress);
        var centerX = (float)(target.X + target.Width / 2);
        var centerY = (float)(target.Y + target.Height / 2);
        canvas.Save();
        canvas.Translate(WalkthroughArtifacts.Width / 2f - centerX * zoom,
            WalkthroughArtifacts.Height / 2f - centerY * zoom);
        canvas.Scale(zoom);
        canvas.DrawBitmap(screenshot, 0, 0);
        canvas.Restore();

        var startX = WalkthroughArtifacts.Width * 0.12f;
        var startY = WalkthroughArtifacts.Height * 0.84f;
        var cursorX = startX + (centerX - startX) * Math.Clamp(progress * 2.6f, 0, 1);
        var cursorY = startY + (centerY - startY) * Math.Clamp(progress * 2.6f, 0, 1);
        using var ripple = new SKPaint
        {
            Color = new SKColor(198, 55, 49, (byte)(150 * (1 - Math.Clamp(progress, 0, 1)))),
            IsAntialias = true,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
        };
        if (clicked && progress > 0.35f)
            canvas.DrawCircle(centerX, centerY, 8 + 34 * Math.Clamp((progress - 0.35f) / 0.65f, 0, 1), ripple);

        using var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 170), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var cursor = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Fill };
        using var outline = new SKPaint { Color = SKColors.Black, IsAntialias = true, StrokeWidth = 2, Style = SKPaintStyle.Stroke };
        using var path = new SKPath();
        path.MoveTo(cursorX, cursorY);
        path.LineTo(cursorX, cursorY + 27);
        path.LineTo(cursorX + 7, cursorY + 20);
        path.LineTo(cursorX + 13, cursorY + 33);
        path.LineTo(cursorX + 19, cursorY + 30);
        path.LineTo(cursorX + 12, cursorY + 17);
        path.LineTo(cursorX + 23, cursorY + 17);
        path.Close();
        canvas.Save();
        canvas.Translate(2, 2);
        canvas.DrawPath(path, shadow);
        canvas.Restore();
        canvas.DrawPath(path, cursor);
        canvas.DrawPath(path, outline);
    }

    private static bool CanRunFfmpeg(string ffmpegExecutable)
    {
        try
        {
            var start = NewStartInfo(ffmpegExecutable);
            start.ArgumentList.Add("-version");
            using var process = Process.Start(start);
            if (process is null) return false;
            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }
            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static void Encode(string ffmpegExecutable, IReadOnlyList<string> frames, string output, params string[] codecOptions)
    {
        var start = NewStartInfo(ffmpegExecutable);
        start.ArgumentList.Add("-framerate");
        start.ArgumentList.Add(WalkthroughArtifacts.Fps.ToString());
        start.ArgumentList.Add("-i");
        start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(frames[0])!, "frame-%05d.png"));
        start.ArgumentList.Add("-an");
        foreach (var option in codecOptions) start.ArgumentList.Add(option);
        start.ArgumentList.Add("-y");
        start.ArgumentList.Add(output);
        Run(start);
    }

    private static void EncodePoster(string ffmpegExecutable, IReadOnlyList<string> frames, string output)
    {
        var start = NewStartInfo(ffmpegExecutable);
        start.ArgumentList.Add("-i");
        start.ArgumentList.Add(frames[0]);
        start.ArgumentList.Add("-frames:v");
        start.ArgumentList.Add("1");
        start.ArgumentList.Add("-y");
        start.ArgumentList.Add(output);
        Run(start);
    }

    private static ProcessStartInfo NewStartInfo(string ffmpegExecutable) => new(ffmpegExecutable)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardError = true,
    };

    private static void Run(ProcessStartInfo start)
    {
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start ffmpeg.");
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException("ffmpeg did not finish within two minutes.");
        }
        var error = errorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0) throw new InvalidOperationException(error.Trim());
    }
}
