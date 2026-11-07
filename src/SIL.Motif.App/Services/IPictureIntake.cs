namespace SIL.Motif.App.Services;

/// <summary>What choosing a picture for a Proposal came to.</summary>
public enum PictureIntakeStatus
{
    /// <summary>The person closed the picker without choosing.</summary>
    Cancelled,
    /// <summary>A picture was chosen and is ready, scaled to fit the store's picture size.</summary>
    Ready,
    /// <summary>The file could not be read, or it holds a picture that cannot be opened.</summary>
    Unreadable,
    /// <summary>The file is not a kind of picture Motif can use.</summary>
    Unsupported,
    /// <summary>The picture could not be made small enough.</summary>
    TooLarge,
}

/// <summary>The outcome of choosing a picture, with its scaled bytes when it is ready.</summary>
/// <param name="Status">What happened.</param>
/// <param name="Bytes">A WebP of at most the store's picture size; null unless <paramref name="Status"/> is Ready.</param>
public sealed record PictureIntakeResult(PictureIntakeStatus Status, byte[]? Bytes = null);

/// <summary>
/// Lets a person choose a picture file and hands back its bytes scaled to fit the store's picture size, so no view model
/// names Avalonia's storage or imaging types.
/// </summary>
public interface IPictureIntake
{
    /// <summary>Asks the person for a picture and scales it; reports why when it cannot be used.</summary>
    Task<PictureIntakeResult> ChooseAsync(CancellationToken cancellationToken = default);
}
