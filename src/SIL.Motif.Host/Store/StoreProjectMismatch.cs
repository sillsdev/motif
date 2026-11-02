namespace SIL.Motif.Host.Store;

/// <summary>
/// A Motif store sits beside a project file but is registered to a different project, which is what a copied
/// or renamed project that took its <c>.motif.db</c> along looks like.
/// </summary>
/// <remarks>
/// It is an <see cref="InvalidDataException"/> (that type is sealed) so every caller that already treats a
/// store that disagrees with its project as a refusal keeps doing so; the two paths a refusal names ride in
/// <see cref="Exception.Data"/>. The store is not adopted: it records the original's Baseline files by path, so
/// a copy that took it over would read the original's Baselines as its own.
/// </remarks>
public static class StoreProjectMismatch
{
    private const string StorePathKey = "storePath";
    private const string RegisteredKey = "registeredProjectPath";

    /// <summary>Makes the exception that says <paramref name="storePath"/> belongs to another project.</summary>
    public static InvalidDataException Create(string storePath, string registeredProjectPath)
    {
        var exception = new InvalidDataException(StoreUpgradeMessages.Get("ProjectMismatch"));
        exception.Data[StorePathKey] = storePath;
        exception.Data[RegisteredKey] = registeredProjectPath ?? string.Empty;
        return exception;
    }

    /// <summary>Reads the paths back when <paramref name="exception"/> came from <see cref="Create"/>.</summary>
    public static bool TryRead(Exception exception, out string storePath, out string registeredProjectPath)
    {
        storePath = exception.Data[StorePathKey] as string ?? string.Empty;
        registeredProjectPath = exception.Data[RegisteredKey] as string ?? string.Empty;
        return storePath.Length > 0;
    }
}
