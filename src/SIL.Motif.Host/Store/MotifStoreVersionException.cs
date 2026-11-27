namespace SIL.Motif.Host.Store;

/// <summary>
/// A Motif store was made by another version of Motif, older or newer. Before 1.0 a stored shape is either
/// the current one or a version this build cannot use.
/// </summary>
/// <remarks>
/// It stays a <see cref="NotSupportedException"/> so every caller that already treats an unsupported store
/// as a refusal keeps doing so. <see cref="StorePath"/> is what the specific type adds: the file a person
/// resets, after a backup, so Motif can recreate it, unless the store is newer.
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

    /// <summary>True when the store is older and a normal open would upgrade it, so deleting it is never the advice.</summary>
    public bool Upgradable { get; init; }

    /// <summary>True when a newer Motif made the store, so neither an upgrade nor a reset is offered: install the newer Motif or restore a pre-upgrade backup.</summary>
    public bool Newer { get; init; }
}
