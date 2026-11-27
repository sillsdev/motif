namespace SIL.Motif.Host.Store;

/// <summary>Controls whether the current request can initiate a project store upgrade.</summary>
public static class StoreUpgradeContext
{
    private static readonly AsyncLocal<bool> Suppressed = new();
    private static readonly AsyncLocal<bool> ReadsUpgrade = new();
    internal static bool IsSuppressed => Suppressed.Value;

    /// <summary>Whether a read of an older store upgrades it first and then reads, as an open does.</summary>
    public static bool UpgradesOnRead => ReadsUpgrade.Value && !Suppressed.Value;

    /// <summary>Prevents upgrades initiated by the current asynchronous request.</summary>
    public static IDisposable Suppress()
    {
        var previous = Suppressed.Value;
        Suppressed.Value = true;
        return new Scope(previous, Suppressed);
    }

    /// <summary>
    /// Lets a read that finds an older store upgrade it (after a backup, as every upgrade is) and then read it, so a
    /// caller with no separate open step, such as an assistant over MCP, just keeps working.
    /// </summary>
    public static IDisposable UpgradeOnRead()
    {
        var previous = ReadsUpgrade.Value;
        ReadsUpgrade.Value = true;
        return new Scope(previous, ReadsUpgrade);
    }

    private sealed class Scope(bool previous, AsyncLocal<bool> value) : IDisposable
    {
        public void Dispose() => value.Value = previous;
    }
}
