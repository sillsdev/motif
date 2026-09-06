namespace SIL.Motif.App.Services;

/// <summary>Lets a view model ask for a project file without depending on Avalonia's storage APIs.</summary>
public interface IProjectPicker
{
    /// <summary>Prompts for a <c>.fwdata</c> file. Returns <c>null</c> when the user cancels.</summary>
    Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default);
}
