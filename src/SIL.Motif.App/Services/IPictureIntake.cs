namespace SIL.Motif.App.Services;

/// <summary>What choosing a picture for a Proposal came to.</summary>
public enum PictureIntakeStatus
{
    /// <summary>The person closed the picker without choosing.</summary>
    Cancelled,
    /// <summary>A picture was chosen and is ready for the Proposal command to prepare.</summary>
    Ready,
    /// <summary>The file could not be read, or it holds a picture that cannot be opened.</summary>
    Unreadable,
    /// <summary>The file is not a kind of picture Motif can use.</summary>
    Unsupported,
    /// <summary>The picture exceeds an input limit.</summary>
    TooLarge,
}

/// <summary>The outcome of choosing a picture, with its original bytes when it is ready.</summary>
/// <param name="Status">What happened.</param>
/// <param name="Bytes">The original picture file; null unless <paramref name="Status"/> is Ready.</param>
public sealed record PictureIntakeResult(PictureIntakeStatus Status, byte[]? Bytes = null);

/// <summary>
/// Lets a person choose a picture file and returns its original bytes without exposing Avalonia storage types.
/// </summary>
public interface IPictureIntake
{
    /// <summary>Asks the person for a picture and checks its input limits.</summary>
    Task<PictureIntakeResult> ChooseAsync(CancellationToken cancellationToken = default);
}
