namespace SIL.Motif.Host.Store;

/// <summary>
/// A Motif store was made by another version of Motif, older or newer. Before 1.0 a stored shape is either
/// the current one or refused, never migrated.
/// </summary>
/// <remarks>
/// It stays a <see cref="NotSupportedException"/> so every caller that already treats an unsupported store
/// as a refusal keeps doing so. <see cref="StorePath"/> is what the specific type adds: the file a person
/// deletes so Motif can recreate it.
/// </remarks>
public sealed class MotifStoreVersionException : NotSupportedException
{
    public MotifStoreVersionException(string storePath, string message) : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storePath);
        StorePath = storePath;
    }

    /// <summary>The fully resolved path of the refused store file.</summary>
    public string StorePath { get; }
}
