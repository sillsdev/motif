using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SIL.Motif.Host.Store;

/// <summary>How many pre-upgrade backups a store keeps, and how many bytes they may hold together.</summary>
/// <param name="MaxCount">The most backup folders kept.</param>
/// <param name="MaxBytes">The most bytes the kept folders hold together.</param>
public sealed record StoreBackupRetentionPolicy(int MaxCount, long MaxBytes)
{
    /// <summary>The caps Motif applies: the last 3 backups, and 4 GiB between them.</summary>
    public static StoreBackupRetentionPolicy Default { get; } = new(3, 4L * 1024 * 1024 * 1024);
}

/// <summary>
/// Keeps the pre-upgrade backups of a store within a hard cap by count and by bytes, evicting the oldest first, so
/// upgrading across many versions cannot fill the disk.
/// </summary>
/// <remarks>
/// Only folders this class's naming makes (<c>pre-upgrade-schema&lt;N&gt;-&lt;stamp&gt;[-n]</c>) are candidates; a backup the
/// person took with <c>motif store backup</c>, a pre-reset or pre-restore folder, and anything else beside them is never
/// touched. Never evicted, whatever the caps say: the backup the running upgrade just made, the newest backup that has
/// its receipt (the verified one), a backup of a NEWER store than this build understands (it is someone else's way back,
/// and is not counted), and a folder that cannot be told apart from one of those. When the protected folders alone exceed
/// a cap, the cap is simply exceeded.
/// </remarks>
public static class StoreBackupRetention
{
    private static readonly Regex Name = new(
        @"^(?<reason>[a-z]+(?:-[a-z]+)*)-schema(?<schema>\d+)-(?<stamp>\d{8}T\d{9}Z)(?:-(?<n>\d+))?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Evicts the oldest <paramref name="reason"/> backups under <paramref name="root"/> until the caps hold.</summary>
    /// <param name="root">The folder that holds the backup folders; absent means nothing to do.</param>
    /// <param name="reason">The folder-name prefix: <see cref="StoreBackups.PreUpgrade"/>.</param>
    /// <param name="justMade">The folder the running upgrade just made; never evicted. May be <see langword="null"/>.</param>
    /// <param name="currentSchema">The schema this build understands; a backup of a higher one is never evicted.</param>
    /// <param name="policy">The caps, or <see langword="null"/> for <see cref="StoreBackupRetentionPolicy.Default"/>.</param>
    /// <returns>The folders removed, oldest first.</returns>
    public static IReadOnlyList<string> Prune(string root, string reason, string? justMade, int currentSchema,
        StoreBackupRetentionPolicy? policy = null)
    {
        policy ??= StoreBackupRetentionPolicy.Default;
        ArgumentOutOfRangeException.ThrowIfLessThan(policy.MaxCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(policy.MaxBytes, 1);
        if (!Directory.Exists(root)) return [];

        var counted = new List<Candidate>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var candidate = Describe(directory, reason, currentSchema);
            if (candidate is { Newer: false } && !IsReparsePoint(directory)) counted.Add(candidate);
        }
        counted.Sort(Candidate.Oldest);

        var newestVerified = counted.LastOrDefault(candidate => candidate.HasReceipt);
        var made = justMade is null ? null : Path.GetFullPath(justMade);
        bool Protected(Candidate candidate) =>
            ReferenceEquals(candidate, newestVerified) ||
            made is not null && string.Equals(candidate.Path, made, StringComparison.Ordinal);

        var removed = new List<string>();
        var count = counted.Count;
        var bytes = counted.Sum(candidate => candidate.Bytes);
        foreach (var candidate in counted)
        {
            if (count <= policy.MaxCount && bytes <= policy.MaxBytes) break;
            if (Protected(candidate)) continue;
            try { Directory.Delete(candidate.Path, recursive: true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { continue; }
            removed.Add(candidate.Path);
            count--;
            bytes -= candidate.Bytes;
        }
        return removed;
    }

    private static Candidate? Describe(string directory, string reason, int currentSchema)
    {
        var match = Name.Match(System.IO.Path.GetFileName(directory));
        if (!match.Success || match.Groups["reason"].Value != reason) return null;
        var schema = int.Parse(match.Groups["schema"].Value, CultureInfo.InvariantCulture);
        var receipt = System.IO.Path.Combine(directory, StoreBackups.ReceiptName);
        var hasReceipt = File.Exists(receipt);
        if (hasReceipt && ReceiptSchema(receipt) is { } recorded) schema = Math.Max(schema, recorded);
        long bytes = 0;
        try
        {
            foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.AllDirectories))
                bytes += file.Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        var n = match.Groups["n"].Success ? int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture) : 1;
        return new Candidate(System.IO.Path.GetFullPath(directory), match.Groups["stamp"].Value, n, bytes, hasReceipt,
            schema > currentSchema);
    }

    private static int? ReceiptSchema(string receipt)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(receipt));
            return document.RootElement.TryGetProperty("schemaVersion", out var value) && value.TryGetInt32(out var schema)
                ? schema : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsReparsePoint(string directory) =>
        (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0;

    private sealed record Candidate(string Path, string Stamp, int N, long Bytes, bool HasReceipt, bool Newer)
    {
        public static int Oldest(Candidate a, Candidate b)
        {
            var stamp = string.CompareOrdinal(a.Stamp, b.Stamp);
            return stamp != 0 ? stamp : a.N != b.N ? a.N.CompareTo(b.N) : string.CompareOrdinal(a.Path, b.Path);
        }
    }
}
