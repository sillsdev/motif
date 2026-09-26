namespace SIL.Motif.App.Services;

/// <summary>Lets a view model hand a link to the program that opens it, without naming Avalonia's launcher.</summary>
public interface IUriLauncher
{
    /// <summary>Opens <paramref name="uri"/>; returns <c>false</c> when no program took it.</summary>
    Task<bool> LaunchAsync(Uri uri, CancellationToken cancellationToken = default);
}
