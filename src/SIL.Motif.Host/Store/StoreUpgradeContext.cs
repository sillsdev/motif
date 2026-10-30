namespace SIL.Motif.Host.Store;

/// <summary>Controls whether the current request can initiate a project store upgrade.</summary>
public static class StoreUpgradeContext
{
    private static readonly AsyncLocal<bool> Suppressed = new();
    internal static bool IsSuppressed => Suppressed.Value;

    /// <summary>Prevents upgrades initiated by the current asynchronous request.</summary>
    public static IDisposable Suppress()
    {
        var previous = Suppressed.Value;
        Suppressed.Value = true;
        return new Scope(previous);
    }

    private sealed class Scope(bool previous) : IDisposable
    {
        public void Dispose() => Suppressed.Value = previous;
    }
}
