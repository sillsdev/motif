using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SIL.Motif.Commands;

namespace SIL.Motif.App.Services;

/// <summary>
/// Opens the window's file picker and reads a picture for the Proposal command to prepare.
/// </summary>
public sealed class AvaloniaPictureIntake : IPictureIntake
{
    /// <summary>The largest file read at all; a bigger one is refused as too large without being decoded.</summary>
    public const long MaxSourceBytes = SIL.Motif.Commands.PictureScaler.MaxSourceBytes;

    private static readonly FilePickerFileType PictureFileType = new("Pictures")
    {
        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp"],
    };

    private readonly TopLevel _topLevel;

    public AvaloniaPictureIntake(TopLevel topLevel)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        _topLevel = topLevel;
    }

    public async Task<PictureIntakeResult> ChooseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var files = await _topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add Picture",
            AllowMultiple = false,
            FileTypeFilter = [PictureFileType],
        });
        if (files.Count == 0) return new PictureIntakeResult(PictureIntakeStatus.Cancelled);
        try
        {
            await using var stream = await files[0].OpenReadAsync();
            return await ReadAsync(stream, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new PictureIntakeResult(PictureIntakeStatus.Unreadable);
        }
    }

    internal static async Task<PictureIntakeResult> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxSourceBytes) return new PictureIntakeResult(PictureIntakeStatus.TooLarge);
        }
        var source = buffer.ToArray();
        return PictureScaler.ValidateSource(source) switch
        {
            PictureScaleStatus.Ready => new PictureIntakeResult(PictureIntakeStatus.Ready, source),
            PictureScaleStatus.TooLarge => new PictureIntakeResult(PictureIntakeStatus.TooLarge),
            _ => new PictureIntakeResult(PictureIntakeStatus.Unsupported),
        };
    }
}
