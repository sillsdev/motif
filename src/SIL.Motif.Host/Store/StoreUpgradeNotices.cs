using System.Globalization;

namespace SIL.Motif.Host.Store;

/// <summary>What one finished store upgrade tells the person: how far it went, the backup, and what it dropped.</summary>
/// <param name="StorePath">The upgraded store.</param>
/// <param name="FromSchema">The schema it was at.</param>
/// <param name="ToSchema">The schema it is at now.</param>
/// <param name="BackupDirectory">The backup taken first, which <c>motif store restore</c> takes; null for an upgrade recovered from before backups.</param>
/// <param name="ReceiptPath">The backup's receipt.</param>
/// <param name="DroppedCounts">Each derived table dropped, with the rows it held.</param>
/// <param name="StoreName">What the store is called in the message: the project store, or the machine store.</param>
public sealed record StoreUpgradeNotice(string StorePath, int FromSchema, int ToSchema, string? BackupDirectory,
    string? ReceiptPath, IReadOnlyDictionary<string, long> DroppedCounts, string StoreName = "project store")
{
    /// <summary>The sentence a surface shows after an upgrade.</summary>
    public string Message
    {
        get
        {
            var dropped = DroppedCounts.Where(pair => pair.Value > 0).OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key} ({pair.Value.ToString(CultureInfo.InvariantCulture)})").ToArray();
            var backup = BackupDirectory is null ? "none was taken for this upgrade" : BackupDirectory;
            return $"Your Motif {StoreName} was upgraded from schema {FromSchema} to {ToSchema}; " +
                $"a backup is at {backup}; these derived results were dropped: " +
                (dropped.Length == 0 ? "none" : string.Join(", ", dropped)) + ". " +
                "They are recomputed when needed, and they stay in the backup.";
        }
    }
}

/// <summary>
/// Carries a finished upgrade to the surface that caused it: a caller opens a scope, and every upgrade the code it
/// calls performs is added to the scope, across awaits. Where no scope is open, the notice only raises
/// <see cref="Upgraded"/>.
/// </summary>
public static class StoreUpgradeNotices
{
    private static readonly AsyncLocal<Scope?> Current = new();

    /// <summary>Raised after every upgrade, for a window that shows one notice for the whole process.</summary>
    public static event Action<StoreUpgradeNotice>? Upgraded;

    /// <summary>Starts collecting the upgrades the current asynchronous request performs.</summary>
    public static IStoreUpgradeNoticeScope Begin()
    {
        var scope = new Scope(Current.Value);
        Current.Value = scope;
        return scope;
    }

    internal static void Record(StoreUpgradeNotice notice)
    {
        for (var scope = Current.Value; scope is not null; scope = scope.Parent)
            lock (scope.Notices) scope.Notices.Add(notice);
        Upgraded?.Invoke(notice);
    }

    private sealed class Scope(Scope? parent) : IStoreUpgradeNoticeScope
    {
        public Scope? Parent { get; } = parent;
        public List<StoreUpgradeNotice> Notices { get; } = [];

        IReadOnlyList<StoreUpgradeNotice> IStoreUpgradeNoticeScope.Notices
        {
            get { lock (Notices) return [.. Notices]; }
        }

        public void Dispose() => Current.Value = Parent;
    }
}

/// <summary>The upgrades one request performed, until it is disposed.</summary>
public interface IStoreUpgradeNoticeScope : IDisposable
{
    /// <summary>Every upgrade so far, oldest first.</summary>
    IReadOnlyList<StoreUpgradeNotice> Notices { get; }
}
