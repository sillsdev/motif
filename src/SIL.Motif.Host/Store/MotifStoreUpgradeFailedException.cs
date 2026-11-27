namespace SIL.Motif.Host.Store;

/// <summary>
/// Opening an older project store could not finish its upgrade. The store is as it was, or, when
/// <see cref="Step"/> is <see cref="SwapStep"/>, the next open finishes the swap; the backup taken first is named.
/// </summary>
/// <remarks>
/// It is an <see cref="IOException"/> so a caller that already treats a store failure as a refusal keeps doing so;
/// the command layer maps it to the <c>store.upgrade-failed</c> refusal, which carries the backup.
/// </remarks>
public sealed class MotifStoreUpgradeFailedException : IOException
{
    /// <summary>The backup could not be taken or did not verify; nothing was changed.</summary>
    public const string BackupStep = "backup";

    /// <summary>The upgraded copy could not be built; the original is untouched.</summary>
    public const string BuildStep = "build";

    /// <summary>The upgraded copy could not be moved into place; the next open finishes it.</summary>
    public const string SwapStep = "swap";

    /// <summary>Creates the failure for <paramref name="step"/>, with the backup when one was taken.</summary>
    public MotifStoreUpgradeFailedException(string storePath, string step, int fromSchema, int toSchema,
        StoreSafetyBackup? backup, Exception cause)
        : base(Describe(storePath, step, fromSchema, toSchema, backup, cause), cause)
    {
        StorePath = storePath;
        Step = step;
        FromSchema = fromSchema;
        ToSchema = toSchema;
        Backup = backup;
    }

    /// <summary>The store that was being upgraded.</summary>
    public string StorePath { get; }

    /// <summary>Where it failed: <see cref="BackupStep"/>, <see cref="BuildStep"/> or <see cref="SwapStep"/>.</summary>
    public string Step { get; }

    /// <summary>The schema the store was at.</summary>
    public int FromSchema { get; }

    /// <summary>The schema the upgrade was to reach.</summary>
    public int ToSchema { get; }

    /// <summary>The backup taken before the upgrade; <see langword="null"/> when none was taken.</summary>
    public StoreSafetyBackup? Backup { get; }

    private static string Describe(string storePath, string step, int from, int to, StoreSafetyBackup? backup, Exception cause)
    {
        var name = Path.GetFileName(storePath);
        if (backup is null)
            return StoreUpgradeMessages.Format("UpgradeFailedNoBackup", name, from, to, cause.Message);
        return StoreUpgradeMessages.Format(step == SwapStep ? "UpgradeFailedSwap" : "UpgradeFailedBuild",
            name, from, to, backup.Directory, cause.Message);
    }
}
