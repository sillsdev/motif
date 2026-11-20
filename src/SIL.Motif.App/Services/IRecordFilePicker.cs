namespace SIL.Motif.App.Services;

/// <summary>Lets a view model ask for an edited record file without depending on Avalonia's storage APIs.</summary>
public interface IRecordFilePicker
{
    /// <summary>Prompts for a record file; returns its path, or <c>null</c> when the person cancels.</summary>
    Task<string?> PickRecordFileAsync(CancellationToken cancellationToken = default);
}
