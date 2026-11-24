using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace SIL.Motif.RecordStore;

/// <summary>How much of the store a handle has checked for itself; a snapshot stamp reports Certified or Full.</summary>
public enum StoreVerification
{
    /// <summary>No valid certificate and no full verification by this handle yet.</summary>
    Uncertified,
    /// <summary>History was not rescanned: a stored certificate matched the state row, the crash flag was clear and the store file was the same.</summary>
    Certified,
    /// <summary>This handle scanned every history row, or published them itself, or ran <see cref="RecordStore.VerifyAsync"/>.</summary>
    Full
}

/// <summary>How a certificate's replay digest was obtained.</summary>
internal static class CertificateProvenance
{
    public const string FullImport = "full-import";
    public const string FullVerify = "full-verify";
    public const string Incremental = "incremental";
}

/// <summary>
/// The row written in the same transaction as every publication and guarded write. It binds the four published digests,
/// the exact replay accumulators (so the next write can extend them) and the store file's identity to one revision.
/// It detects accidental damage and a copy from another file; it does not authenticate anything.
/// </summary>
internal sealed class StoreCertificate
{
    public int Id { get; set; } = 1;
    public string Revision { get; set; } = "";
    public string ReplayDigest { get; set; } = "";
    public string SnapshotDigest { get; set; } = "";
    public string ContentDigest { get; set; } = "";
    public string IndexDigest { get; set; } = "";
    public string CommitRows { get; set; } = "";
    public string ChangeRows { get; set; } = "";
    public string Provenance { get; set; } = "";
    public string StoreId { get; set; } = "";
    /// <summary>Set in its own transaction before a write begins and cleared by the write's own commit; set at open means a crash.</summary>
    public bool WriteInProgress { get; set; }
}

/// <summary>One format upgrade: the format the store had, the one it has now, how long the upgrading transaction ran, and how much of that wrote the derived tables.</summary>
public sealed record StoreUpgrade(int FromFormat, int ToFormat, TimeSpan Elapsed, TimeSpan BackfillElapsed);

/// <summary>Points in a guarded write where a test can stop it; a stop here is what a crash would leave.</summary>
public enum WriteStage { HistoryWritten, StateWritten, Committed }

public sealed partial class RecordStore
{
    private bool upgrading;
    private bool Certifiable => format >= CertificateFormat || upgrading;

    /// <summary>Whether this handle verified history itself, trusted a certificate, or has done neither.</summary>
    public StoreVerification Verification { get; private set; }

    /// <summary>True once a read-write operation by this handle upgraded the store; the same transaction backfilled what the new format adds.</summary>
    public bool UpgradedFormat { get; private set; }

    /// <summary>What the last upgrade by this handle was and how long it held the transaction; null when it has not upgraded.</summary>
    public StoreUpgrade? LastUpgrade { get; private set; }

    // Set during an upgrade to format 7: the transaction must write the derived tables before it commits.
    private bool backfillDerived;
    private int upgradeFrom;
    private TimeSpan backfillTime;
    private System.Diagnostics.Stopwatch? upgradeClock;

    /// <summary>Told before a one-time cost starts, for example the format 5 to 6 upgrade; the write is busy until it returns.</summary>
    public Action<string>? Progress { get; set; }

    /// <summary>Test hook: called as a guarded write passes each stage; throwing from it stops the write there.</summary>
    internal Action<WriteStage>? WriteStageReached { get; set; }

    /// <summary>Test hook: awaited by <see cref="VerifyAsync"/> after its lock-free scan and before it asks for the writer lock.</summary>
    internal Func<Task>? AfterVerifyScan { get; set; }

    /// <summary>Test hook: awaited by <see cref="VerifyAsync"/> after it re-certified and before it returns.</summary>
    internal Func<Task>? AfterVerifyCertified { get; set; }

    /// <summary>Test hook: called as the history scan inside <see cref="VerifyAsync"/> starts each batch of rows.</summary>
    internal Action<string, long>? VerifyScanBatchStarted { get; set; }

    /// <summary>Full history scans this handle ran for writes and publications.</summary>
    internal int HistoryScans { get; private set; }

    internal async Task<StoreCertificate?> CertificateAsync(CancellationToken ct = default) =>
        Certifiable ? await database.Set<StoreCertificate>().AsNoTracking().SingleOrDefaultAsync(ct) : null;

    // The first commit row names the store file: another file's certificate carries another identity.
    private async Task<string?> StoreIdentityAsync(CancellationToken ct = default)
    {
        var first = await database.Database.SqlQuery<string>(
            $"SELECT hex(Id) || '/' || Hash AS Value FROM Commits ORDER BY rowid LIMIT 1").ToListAsync(ct);
        return first.Count == 0 ? null
            : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("motif-record-store-identity/" + first[0])));
    }

    // The flag this handle set itself for a write that is in flight or was abandoned is not evidence of a crash.
    private bool ownWriteFlag;

    internal static bool Trusted(StoreCertificate certificate, StoreState state, string? identity, bool ignoreFlag = false) =>
        (ignoreFlag || !certificate.WriteInProgress) && identity is not null && certificate.StoreId == identity && state.Published &&
        certificate.Revision == state.Revision && certificate.ReplayDigest == state.ReplayDigest &&
        certificate.SnapshotDigest == state.SnapshotDigest && certificate.ContentDigest == state.ContentDigest &&
        certificate.IndexDigest == state.IndexDigest &&
        SetDigest.TryParse(certificate.CommitRows, out var commits) && SetDigest.TryParse(certificate.ChangeRows, out var changes) &&
        commits.Combine(changes).Final("replay") == certificate.ReplayDigest;

    private StoreVerification OpenVerification(StoreState state)
    {
        if (!Certifiable) return StoreVerification.Uncertified;
        var certificate = database.Set<StoreCertificate>().AsNoTracking().SingleOrDefault();
        if (certificate is null) return StoreVerification.Uncertified;
        var first = database.Database.SqlQuery<string>($"SELECT hex(Id) || '/' || Hash AS Value FROM Commits ORDER BY rowid LIMIT 1").ToList();
        var identity = first.Count == 0 ? null
            : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("motif-record-store-identity/" + first[0])));
        // A format 7 store is certified only while its binding row copies the state row.
        if (format >= DerivedFormat && !BindingMatches(state)) return StoreVerification.Uncertified;
        return Trusted(certificate, state, identity, ownWriteFlag) ? StoreVerification.Certified : StoreVerification.Uncertified;
    }

    /// <summary>What a guarded write read before it set the crash flag.</summary>
    private sealed record WriteStart(StoreCertificate? Certificate, string? Identity, bool FlagSetHere);

    // The flag commits on its own, before the write's transaction, so a crash leaves it set.
    private async Task<WriteStart> BeginWriteAsync()
    {
        if (!Certifiable) return new(null, null, false);
        var certificate = await CertificateAsync();
        if (certificate is null) return new(null, null, false);
        var identity = await StoreIdentityAsync();
        var setHere = !certificate.WriteInProgress;
        if (setHere)
        {
            await database.Database.ExecuteSqlRawAsync("UPDATE RecordStoreCertificate SET WriteInProgress = 1");
            ownWriteFlag = true;
        }
        return new(certificate, identity, setHere);
    }

    // Clearing the flag must not hide the failure that brought the write here.
    private async Task TryAbandonWriteAsync(WriteStart start)
    {
        try { await AbandonWriteAsync(start); }
        catch (Exception) { }
    }

    private async Task AbandonWriteAsync(WriteStart start)
    {
        try
        {
            if (start.FlagSetHere) await database.Database.ExecuteSqlRawAsync("UPDATE RecordStoreCertificate SET WriteInProgress = 0");
        }
        finally { ownWriteFlag = false; }
    }

    /// <summary>The replay state a write extends: the certified one when trusted, else a full scan.</summary>
    private async Task<(ReplayState Replay, bool Scanned)> ReplayForWriteAsync(WriteStart start, StoreState state)
    {
        if (start.Certificate is { } certificate && Trusted(certificate, state, start.Identity) &&
            SetDigest.TryParse(certificate.CommitRows, out var commitRows) && SetDigest.TryParse(certificate.ChangeRows, out var changeRows))
        {
            var commits = await ReplayIntegrity.CommitIdsAsync(database);
            if (commits.Count == commitRows.Count) return (new(commitRows, changeRows, commits), false);
        }
        HistoryScans++;
        return (await ReplayIntegrity.ScanAsync(database, Workers), true);
    }

    /// <summary>Stages the certificate for the state row's new values; one SaveChanges commits both.</summary>
    private async Task StageCertificateAsync(StoreState state, ReplayState replay, string provenance)
    {
        if (!Certifiable) return;
        // An empty import has no commit to name the file, so there is nothing a certificate could bind to.
        if (await StoreIdentityAsync() is not { } identity) return;
        var certificate = await database.Set<StoreCertificate>().SingleOrDefaultAsync();
        if (certificate is null) database.Add(certificate = new StoreCertificate());
        certificate.Revision = state.Revision;
        certificate.ReplayDigest = state.ReplayDigest;
        certificate.SnapshotDigest = state.SnapshotDigest;
        certificate.ContentDigest = state.ContentDigest;
        certificate.IndexDigest = state.IndexDigest;
        certificate.CommitRows = replay.CommitRows.ToText();
        certificate.ChangeRows = replay.ChangeRows.ToText();
        certificate.Provenance = provenance;
        certificate.StoreId = identity;
        certificate.WriteInProgress = false;
    }

    private const string CertificateTable = """
        CREATE TABLE IF NOT EXISTS "RecordStoreCertificate" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_RecordStoreCertificate" PRIMARY KEY, "Revision" TEXT NOT NULL,
            "ReplayDigest" TEXT NOT NULL, "SnapshotDigest" TEXT NOT NULL, "ContentDigest" TEXT NOT NULL,
            "IndexDigest" TEXT NOT NULL, "CommitRows" TEXT NOT NULL, "ChangeRows" TEXT NOT NULL,
            "Provenance" TEXT NOT NULL, "StoreId" TEXT NOT NULL, "WriteInProgress" INTEGER NOT NULL)
        """;

    internal const string IdentityTable = """
        CREATE TABLE IF NOT EXISTS RecordContentIdentity (Identity BLOB NOT NULL PRIMARY KEY, Id INTEGER NOT NULL) WITHOUT ROWID
        """;

    /// <summary>Upgrades an older store to format 7 in the caller's transaction; a rollback undoes it.</summary>
    private async Task UpgradeInTransactionAsync()
    {
        if (readOnly || format >= StoreFormat || upgrading || format == LegacyStoreFormat) return;
        upgrading = true;
        upgradeFrom = format;
        upgradeClock = System.Diagnostics.Stopwatch.StartNew();
        if (format == PackedStoreFormat)
        {
            await database.Database.ExecuteSqlRawAsync(CertificateTable);
            await database.Database.ExecuteSqlRawAsync(IdentityTable);
            Progress?.Invoke("Upgrading the store to format 6: indexing its stored content once.");
            await WriteIdentityTableAsync(content.AllIdentities());
            content.PersistIdentities = true;
            content.PersistedLookup = LookupIdentity;
        }
        Progress?.Invoke("Upgrading the store to format 7: writing its record headers, chunk digests and publication binding.");
        CreateDerivedTables(database);
        await database.Database.ExecuteSqlRawAsync($"PRAGMA user_version={StoreFormat}");
        backfillDerived = true;
    }

    // Replaces the identity table inside the current transaction, in multi-row statements.
    private async Task WriteIdentityTableAsync(IEnumerable<KeyValuePair<ContentKey, int>> source)
    {
        await database.Database.ExecuteSqlRawAsync("DELETE FROM RecordContentIdentity");
        var identities = source.Select(pair => (Bytes: pair.Key.ToBytes(), pair.Value)).ToArray();
        Array.Sort(identities, static (a, b) => a.Bytes.AsSpan().SequenceCompareTo(b.Bytes));
        var connection = (SqliteConnection)database.Database.GetDbConnection();
        var transaction = (SqliteTransaction)database.Database.CurrentTransaction!.GetDbTransaction();
        const int rowsPerStatement = 400;
        await using var full = IdentityInsert(connection, transaction, rowsPerStatement);
        for (var start = 0; start < identities.Length; start += rowsPerStatement)
        {
            var count = Math.Min(rowsPerStatement, identities.Length - start);
            await using var partial = count == rowsPerStatement ? null : IdentityInsert(connection, transaction, count);
            var command = partial ?? full;
            for (var row = 0; row < count; row++)
            {
                command.Parameters[2 * row].Value = identities[start + row].Bytes;
                command.Parameters[2 * row + 1].Value = identities[start + row].Value;
            }
            command.ExecuteNonQuery();
        }
    }

    private static SqliteCommand IdentityInsert(SqliteConnection connection, SqliteTransaction transaction, int rows)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO RecordContentIdentity (Identity, Id) VALUES " +
            string.Join(",", Enumerable.Range(0, rows).Select(row => $"($i{row}, $d{row})"));
        for (var row = 0; row < rows; row++)
        {
            command.Parameters.Add($"$i{row}", SqliteType.Blob);
            command.Parameters.Add($"$d{row}", SqliteType.Integer);
        }
        return command;
    }

    private List<(byte[] Bytes, int Id)> ReadIdentityTable(CancellationToken ct)
    {
        var rows = new List<(byte[], int)>();
        var connection = (SqliteConnection)database.Database.GetDbConnection();
        using var command = connection.CreateCommand();
        command.Transaction = database.Database.CurrentTransaction?.GetDbTransaction() as SqliteTransaction;
        command.CommandText = "SELECT Identity, Id FROM RecordContentIdentity ORDER BY Identity";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            rows.Add(((byte[])reader[0], reader.GetInt32(1)));
        }
        return rows;
    }

    /// <summary>
    /// Rebuilds the content identity table from the stored content rows. The table is derived data that no digest covers,
    /// so this is the repair when <see cref="VerifyAsync"/> reports it damaged. It does not change history or the published point.
    /// </summary>
    public async Task RebuildContentIdentitiesAsync(CancellationToken ct = default)
    {
        if (readOnly) throw new InvalidOperationException("This store handle is read-only.");
        if (!Certifiable) throw new InvalidOperationException("A format 5 store has no identity table; its first write upgrades it.");
        using var writer = AcquireWriter();
        point = null;
        await PublishedStateAsync(ct);
        var fresh = new RecordContent();
        fresh.Load(await database.Set<ContentRow>().AsNoTracking().ToArrayAsync(ct), ct);
        await using var transaction = await database.Database.BeginTransactionAsync(ct);
        await WriteIdentityTableAsync(fresh.AllIdentities());
        await transaction.CommitAsync(ct);
    }

    private void CompleteUpgrade()
    {
        if (!upgrading) return;
        UpgradedFormat = true;
        format = StoreFormat;
        upgrading = false;
        backfillDerived = false;
        LastUpgrade = new(upgradeFrom, StoreFormat, upgradeClock?.Elapsed ?? TimeSpan.Zero, backfillTime);
    }

    private void AbandonUpgrade()
    {
        if (!upgrading) return;
        upgrading = false;
        backfillDerived = false;
        if (format < CertificateFormat)
        {
            content.PersistIdentities = false;
            content.PersistedLookup = null;
        }
    }

    private void UseIdentityTable()
    {
        if (readOnly || format < CertificateFormat) return;
        content.PersistIdentities = true;
        content.PersistedLookup = LookupIdentity;
    }

    private int? LookupIdentity(ContentKey key)
    {
        var connection = (SqliteConnection)database.Database.GetDbConnection();
        database.Database.OpenConnection();
        try
        {
            using var command = connection.CreateCommand();
            command.Transaction = database.Database.CurrentTransaction?.GetDbTransaction() as SqliteTransaction;
            command.CommandText = "SELECT Id FROM RecordContentIdentity WHERE Identity = $identity";
            command.Parameters.AddWithValue("$identity", key.ToBytes());
            return command.ExecuteScalar() is long id ? checked((int)id) : null;
        }
        finally { database.Database.CloseConnection(); }
    }

    /// <summary>A copy replaced this store's file (regenerate, bootstrap); follow the format it now has.</summary>
    private void RefreshFormat(string path)
    {
        var stored = ReadFormat(RecordSqlite.ConnectionString(path));
        if (stored < CertificateFormat || stored > StoreFormat || stored <= format) return;
        format = stored;
        UseIdentityTable();
    }

    /// <summary>
    /// Full verification: rescans every history row, decodes and digests every snapshot, re-reads the content and checks the
    /// index, then compares each digest with the state row and the certificate. A disagreement throws and names the table
    /// at fault; nothing is changed. When everything agrees a writing handle re-certifies the point (provenance
    /// <c>full-verify</c>, crash flag cleared; a format 5 store is upgraded by this write) and every handle reports
    /// <see cref="StoreVerification.Full"/>. It returns the exact state it verified. A reader of an older format that only reads is never upgraded by reading.
    /// </summary>
    public Task VerifyAsync(CancellationToken ct = default) => VerifyCertifyingAsync(ct);

    internal async Task<StoreState> VerifyCertifyingAsync(CancellationToken ct = default)
    {
        point = null;
        (StoreState state, ReplayState replay, VerifiedPoint acquired) result;
        try { result = await VerifyReadAsync(ct); }
        catch (StoreRevisionUnavailableException)
        {
            throw new InvalidDataException("Store verification failed. " + await NameRevisionDamageAsync(ct));
        }
        var (state, replay, acquired) = result;
        if (!readOnly)
        {
            // The scan ran without the writer lock; only the write that re-certifies needs it.
            if (AfterVerifyScan is { } scanned) await scanned();
            using var writer = AcquireWriter();
            await using var transaction = await database.Database.BeginTransactionAsync(ct);
            try
            {
                var now = await database.Set<StoreState>().AsNoTracking().SingleAsync(ct);
                if (!now.SamePoint(state))
                    throw new InvalidOperationException("The store changed while it was being verified; run VerifyAsync again.");
                // A crashed list operation commits history without moving the state row.
                if (await CommitRevisionAsync(ct) != state.Revision)
                    throw new InvalidOperationException("History changed while the store was being verified and is not published; the store was not re-certified.");
                await UpgradeInTransactionAsync();
                if (backfillDerived) WriteDerived(state, acquired.Derived!.Chunks, acquired.Derived.Headers);
                await StageCertificateAsync(state, replay, CertificateProvenance.FullVerify);
                await database.SaveChangesAsync(ct);
                database.ChangeTracker.Clear();
                await transaction.CommitAsync(ct);
                CompleteUpgrade();
            }
            catch
            {
                AbandonUpgrade();
                database.ChangeTracker.Clear();
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
        point = acquired with { Replay = replay, Derived = null };
        Verification = StoreVerification.Full;
        if (AfterVerifyCertified is { } certified) await certified();
        return state;
    }

    private async Task<string> NameRevisionDamageAsync(CancellationToken ct)
    {
        var stored = await database.Set<StoreState>().AsNoTracking().SingleAsync(ct);
        var certificate = await CertificateAsync(ct);
        var actual = await CommitRevisionAsync(ct);
        if (certificate?.Revision == actual) return "RecordStoreState: its revision differs from the commit history that the certificate matches.";
        if (certificate?.Revision == stored.Revision) return "Commits: the commit hashes no longer produce the revision that RecordStoreState and the certificate agree on.";
        return "RecordStoreState or Commits: the stored revision does not match the commit history.";
    }

    private Task<(StoreState, ReplayState, VerifiedPoint)> VerifyReadAsync(CancellationToken ct) =>
        ReadPublishedAsync(async state =>
        {
            var certificate = await CertificateAsync(ct);
            var identity = certificate is null ? null : await StoreIdentityAsync(ct);
            var findings = new List<string>();
            string? snapshot = null, index = null, contentDigest = null;
            VerifiedPoint? verified = null;
            ReplayState? scanned = null;
            try { verified = await AcquireAsync(state, ct, (kind, digest) => { if (kind == "snapshot") snapshot = digest; else index = digest; }); }
            catch (InvalidDataException failure) { findings.Add("Snapshots: " + failure.Message); }
            try { scanned = await ReplayIntegrity.ScanAsync(database, Workers, ct: ct, batchStarted: VerifyScanBatchStarted); }
            catch (InvalidDataException failure) { findings.Add("Commits or ChangeEntities: " + failure.Message); }
            try
            {
                var fresh = new RecordContent();
                fresh.Load(await database.Set<ContentRow>().AsNoTracking().ToArrayAsync(ct), ct);
                contentDigest = fresh.Digest(ct);
            }
            catch (InvalidDataException failure) { findings.Add("RecordContent: " + failure.Message); }
            if (Certifiable)
            {
                try
                {
                    var expected = new RecordContent();
                    expected.Load(await database.Set<ContentRow>().AsNoTracking().ToArrayAsync(ct), ct);
                    var wanted = expected.AllIdentities().Select(pair => (Bytes: pair.Key.ToBytes(), Id: pair.Value)).ToArray();
                    Array.Sort(wanted, static (a, b) => a.Bytes.AsSpan().SequenceCompareTo(b.Bytes));
                    var held = ReadIdentityTable(ct);
                    if (held.Count != wanted.Length || held.Where((row, i) => row.Id != wanted[i].Id || !row.Bytes.AsSpan().SequenceEqual(wanted[i].Bytes)).Any())
                        findings.Add($"RecordContentIdentity: it holds {held.Count} identities that do not match the {wanted.Length} stored content values; run RebuildContentIdentitiesAsync");
                }
                catch (InvalidDataException) { }
            }
            if (format >= DerivedFormat && verified?.Derived is { } derived) await AddDerivedFindingsAsync(findings, state, derived, ct);
            if (certificate is not null)
            {
                if (certificate.Revision != state.Revision) findings.Add("RecordStoreCertificate: its revision differs from RecordStoreState");
                if (certificate.StoreId != identity) findings.Add("RecordStoreCertificate: it was issued for another store file");
            }
            if (scanned is not null)
            {
                var tables = new List<string>();
                if (certificate is not null && SetDigest.TryParse(certificate.CommitRows, out var commitRows) &&
                    SetDigest.TryParse(certificate.ChangeRows, out var changeRows) && certificate.ReplayDigest == state.ReplayDigest)
                {
                    if (scanned.CommitRows != commitRows) tables.Add("Commits");
                    if (scanned.ChangeRows != changeRows) tables.Add("ChangeEntities");
                }
                Compare(findings, "replay", tables.Count > 0 ? string.Join(" and ", tables) : "Commits or ChangeEntities",
                    state.ReplayDigest, certificate?.ReplayDigest, scanned.Final());
            }
            if (snapshot is not null) Compare(findings, "snapshot", "Snapshots", state.SnapshotDigest, certificate?.SnapshotDigest, snapshot);
            if (contentDigest is not null) Compare(findings, "content", "RecordContent", state.ContentDigest, certificate?.ContentDigest, contentDigest);
            if (index is not null) Compare(findings, "index", "IncomingReferences", state.IndexDigest, certificate?.IndexDigest, index);
            if (findings.Count > 0) throw new InvalidDataException("Store verification failed. " + string.Join("; ", findings));
            return (state, scanned!, verified!);
        }, ct);

    // Three witnesses to one digest: state row, certificate and the computed value. The odd one out is named.
    private static void Compare(List<string> findings, string label, string table, string state, string? certificate, string actual)
    {
        if (state != actual)
        {
            if (certificate is null) findings.Add($"{table} or RecordStoreState: the {label} digest does not match the stored point");
            else if (certificate == actual) findings.Add($"RecordStoreState: its {label} digest differs from the verified {label}");
            else if (certificate == state) findings.Add($"{table}: the {label} digest does not match the stored point");
            else findings.Add($"RecordStoreState and {table}: the {label} digest differs from both the certificate and the verified {label}");
        }
        else if (certificate is not null && certificate != state)
            findings.Add($"RecordStoreCertificate: its {label} digest differs from the verified point");
    }
}
