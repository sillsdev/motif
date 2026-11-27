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
/// Keeps the pre-upgrade backups of a store within a cap by count and by bytes, evicting the oldest first, so
/// upgrading across many versions cannot fill the disk.
/// </summary>
/// <remarks>
/// Candidates are the folders this class's naming makes (<c>pre-upgrade-schema&lt;N&gt;-&lt;stamp&gt;[-n]</c>) and, for a
/// project store, the <c>&lt;store&gt;.schema&lt;N&gt;.bak[n]</c> files older upgrades left beside it; together they count
/// toward one cap. A backup the person took with <c>motif store backup</c>, a pre-reset or pre-restore folder, and
/// anything else is never touched, nor is a name that does not match exactly (ASCII digits, in range). Never evicted: the
/// backup the running upgrade just made, the newest folder that has its receipt, and any backup of a NEWER store than this
/// build understands (not counted). When those protected backups alone exceed a cap, the cap is exceeded.
/// </remarks>
public static class StoreBackupRetention
{
    /// <summary>The suffix of the marker written beside a <c>.bak</c> kept because its backup did not verify; it is never evicted.</summary>
    public const string UnverifiedMarker = ".unverified";

    private static readonly Regex Name = new(
        @"^(?<reason>[a-z]+(?:-[a-z]+)*)-schema(?<schema>[0-9]+)-(?<stamp>[0-9]{8}T[0-9]{9}Z)(?:-(?<n>[0-9]+))?\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Evicts the oldest <paramref name="reason"/> backups under <paramref name="root"/> until the caps hold.</summary>
    /// <param name="root">The folder that holds the backup folders; absent means nothing to do.</param>
    /// <param name="reason">The folder-name prefix: <see cref="StoreBackups.PreUpgrade"/>.</param>
    /// <param name="justMade">The folder the running upgrade just made; never evicted. May be <see langword="null"/>.</param>
    /// <param name="currentSchema">The schema this build understands; a backup of a higher one is never evicted.</param>
    /// <param name="policy">The caps, or <see langword="null"/> for <see cref="StoreBackupRetentionPolicy.Default"/>.</param>
    /// <param name="storePath">A project store whose <c>.schemaN.bak</c> siblings count toward the cap; null for none.</param>
    /// <returns>The folders and files removed, oldest first.</returns>
    public static IReadOnlyList<string> Prune(string root, string reason, string? justMade, int currentSchema,
        StoreBackupRetentionPolicy? policy = null, string? storePath = null)
    {
        policy ??= StoreBackupRetentionPolicy.Default;
        ArgumentOutOfRangeException.ThrowIfLessThan(policy.MaxCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(policy.MaxBytes, 1);

        var counted = new List<Candidate>();
        if (Directory.Exists(root))
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                var candidate = Describe(directory, reason, currentSchema);
                if (candidate is { Newer: false } && !IsReparsePoint(directory)) counted.Add(candidate);
            }
        if (storePath is not null) counted.AddRange(Siblings(storePath, currentSchema));
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
            try { candidate.Delete(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { continue; }
            removed.Add(candidate.Path);
            count--;
            bytes -= candidate.Bytes;
        }
        return removed;
    }

    private static IEnumerable<Candidate> Siblings(string storePath, int currentSchema)
    {
        var full = Path.GetFullPath(storePath);
        var directory = Path.GetDirectoryName(full);
        if (directory is null || !Directory.Exists(directory)) yield break;
        var pattern = new Regex("^" + Regex.Escape(Path.GetFileName(full)) + @"\.schema(?<schema>[0-9]+)\.bak(?<n>[0-9]*)\z",
            RegexOptions.CultureInvariant);
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            var match = pattern.Match(Path.GetFileName(file));
            if (!match.Success || !int.TryParse(match.Groups["schema"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var schema))
                continue;
            if (schema > currentSchema || File.Exists(file + UnverifiedMarker)) continue;
            long bytes = 0;
            DateTime written;
            try
            {
                foreach (var part in new[] { file, file + "-wal", file + "-shm" })
                    if (File.Exists(part)) bytes += new FileInfo(part).Length;
                written = File.GetLastWriteTimeUtc(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { continue; }
            var n = match.Groups["n"].Value.Length > 0 &&
                int.TryParse(match.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : 1;
            yield return new Candidate(file, written.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture), n, bytes,
                false, false, IsFile: true);
        }
    }

    private static Candidate? Describe(string directory, string reason, int currentSchema)
    {
        var match = Name.Match(System.IO.Path.GetFileName(directory));
        if (!match.Success || match.Groups["reason"].Value != reason) return null;
        if (!int.TryParse(match.Groups["schema"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var schema)) return null;
        var n = 1;
        if (match.Groups["n"].Success &&
            !int.TryParse(match.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out n)) return null;
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

    private sealed record Candidate(string Path, string Stamp, int N, long Bytes, bool HasReceipt, bool Newer, bool IsFile = false)
    {
        public void Delete()
        {
            if (!IsFile) { Directory.Delete(Path, recursive: true); return; }
            foreach (var part in new[] { Path + "-wal", Path + "-shm", Path })
                if (File.Exists(part)) File.Delete(part);
        }

        public static int Oldest(Candidate a, Candidate b)
        {
            var stamp = string.CompareOrdinal(a.Stamp, b.Stamp);
            return stamp != 0 ? stamp : a.N != b.N ? a.N.CompareTo(b.N) : string.CompareOrdinal(a.Path, b.Path);
        }
    }
}
