namespace SIL.Motif.App.Services;

/// <summary>Lets a view model ask for a Handoff destination folder without depending on Avalonia's storage APIs.</summary>
public interface IHandoffFolderPicker
{
    /// <summary>Prompts for an output folder. Returns <c>null</c> when the user cancels.</summary>
    Task<string?> PickFolderAsync(CancellationToken cancellationToken = default);
}
