namespace SIL.Motif.Host.Store;

/// <summary>
/// The backup that must precede a store reset could not be taken or did not verify, so nothing was deleted.
/// </summary>
public sealed class StoreBackupFailedException : IOException
{
    /// <summary>Creates the failure for the store at <paramref name="storePath"/>.</summary>
    public StoreBackupFailedException(string storePath, Exception cause)
        : base(StoreUpgradeMessages.Format("ResetBackupFailed", Path.GetFileName(storePath), cause.Message), cause) =>
        StorePath = storePath;

    /// <summary>The store that was to be reset.</summary>
    public string StorePath { get; }
}
