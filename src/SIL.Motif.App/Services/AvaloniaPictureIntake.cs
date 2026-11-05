using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace SIL.Motif.App.Services;

/// <summary>
/// Opens the window's file picker for a picture and scales what it returns with <see cref="PictureScaler"/>.
/// </summary>
public sealed class AvaloniaPictureIntake : IPictureIntake
{
    /// <summary>The largest file read at all; a bigger one is refused as too large without being decoded.</summary>
    public const long MaxSourceBytes = 64L * 1024 * 1024;

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
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > MaxSourceBytes) return new PictureIntakeResult(PictureIntakeStatus.TooLarge);
            }
            return await Task.Run(() => PictureScaler.Scale(buffer.ToArray()), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new PictureIntakeResult(PictureIntakeStatus.Unreadable);
        }
    }
}
