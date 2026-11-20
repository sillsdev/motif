using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace SIL.Motif.RecordStore;

/// <summary>Order-independent digest of every history row plus the commit identities it covers.</summary>
internal sealed record ReplayState(SetDigest Digest, IReadOnlySet<Guid> Commits)
{
    public string Final() => Digest.Final("replay");
}

internal static class ReplayIntegrity
{
    /// <summary>Rows per batch handed to a hashing worker.</summary>
    internal const int DefaultBatchRows = 2048;

    public static async Task<string> DigestAsync(RecordDatabase database, int? workers = null) => (await ScanAsync(database, workers)).Final();

    /// <summary>
    /// Digests every history row. The rows are read in table order on the caller's connection (so an open transaction's
    /// own writes are seen); only encoding and hashing fan out over <paramref name="workers"/> threads, and the partial
    /// sums are added. A failure is the one the sequential scan would meet first, the lowest failing row's.
    /// </summary>
    public static async Task<ReplayState> ScanAsync(RecordDatabase database, int? workers = null, int batchRows = DefaultBatchRows,
        CancellationToken ct = default, Action<string, long>? batchStarted = null)
    {
        var width = workers ?? ParallelWork.DefaultWorkers;
        var commits = await Rows(database, "Commits", "", null, width, batchRows, ct, batchStarted);
        var changes = await Rows(database, "ChangeEntities", "", null, width, batchRows, ct, batchStarted);
        return new(commits.Digest.Combine(changes.Digest), commits.Ids.ToHashSet());
    }

    /// <summary>Adds the rows of the one commit this handle just wrote; any other unseen commit is refused.</summary>
    public static async Task<ReplayState> ExtendAsync(RecordDatabase database, ReplayState prior, Guid written)
    {
        var digest = prior.Digest;
        var commits = new HashSet<Guid>(prior.Commits);
        var existing = await Rows(database, "Commits", "", null, 1, DefaultBatchRows, default, null, identitiesOnly: true);
        var added = existing.Ids.Where(id => !commits.Contains(id)).ToList();
        if (added.Count != 1 || added[0] != written)
            throw new ForeignCommitException("History contains a commit this handle did not write; reopen and verify the store.");
        foreach (var id in added)
        {
            digest = digest.Combine((await Rows(database, "Commits", "WHERE Id = $id", id, 1, DefaultBatchRows, default, null)).Digest);
            digest = digest.Combine((await Rows(database, "ChangeEntities", "WHERE CommitId = $id", id, 1, DefaultBatchRows, default, null)).Digest);
            commits.Add(id);
        }
        return new(digest, commits);
    }

    /// <summary>Row encodings summed into a digest, and the commit identities seen.</summary>
    private sealed class Partial
    {
        public SetDigest Digest;
        public readonly List<Guid> Ids = [];
    }

    private sealed record Batch(long First, List<object[]> Rows);

    private static async Task<Partial> Rows(RecordDatabase database, string table, string where, Guid? id, int workers, int batchRows,
        CancellationToken ct, Action<string, long>? batchStarted, bool identitiesOnly = false)
    {
        await database.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = database.Database.GetDbConnection().CreateCommand();
            command.Transaction = database.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = identitiesOnly ? $"SELECT Id FROM {table} {where}" : $"SELECT * FROM {table} {where}";
            if (id.HasValue)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = "$id";
                parameter.Value = NetworkGuidConverter.Write(id.Value);
                command.Parameters.Add(parameter);
            }
            await using var reader = await command.ExecuteReaderAsync(ct);
            var identity = table == "Commits" ? reader.GetOrdinal("Id") : -1;
            var overall = new Partial();
            if (workers <= 1)
            {
                var number = 0L;
                while (await reader.ReadAsync(ct))
                {
                    if (number % batchRows == 0) { ct.ThrowIfCancellationRequested(); batchStarted?.Invoke(table, number / batchRows); }
                    Add(overall, table, identity, Values(reader));
                    number++;
                }
                return overall;
            }
            var queue = Channel.CreateBounded<Batch>(new BoundedChannelOptions(workers * 2) { SingleWriter = true });
            var parts = new Partial[workers];
            var failure = (Seq: long.MaxValue, Error: (Exception?)null);
            var gate = new object();
            var consumers = Enumerable.Range(0, workers).Select(worker => Task.Run(async () =>
            {
                var part = parts[worker] = new Partial();
                await foreach (var batch in queue.Reader.ReadAllAsync(CancellationToken.None))
                {
                    if (ct.IsCancellationRequested) continue;
                    var seq = batch.First / batchRows;
                    try
                    {
                        batchStarted?.Invoke(table, seq);
                        // Row by row so the first failing row of the batch is the one reported.
                        foreach (var row in batch.Rows) Add(part, table, identity, row);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
                    catch (Exception error)
                    {
                        lock (gate) if (seq < failure.Seq) failure = (seq, error);
                    }
                }
            })).ToArray();
            Exception? readError = null;
            try
            {
                var rows = new List<object[]>(batchRows);
                var first = 0L;
                var next = 0L;
                while (await reader.ReadAsync(ct))
                {
                    // Later batches cannot hold an earlier failure, so stop reading once one has failed.
                    if (Volatile.Read(ref failure.Seq) != long.MaxValue) break;
                    rows.Add(Values(reader));
                    next++;
                    if (rows.Count < batchRows) continue;
                    await queue.Writer.WriteAsync(new Batch(first, rows), ct);
                    rows = new(batchRows);
                    first = next;
                }
                if (rows.Count > 0 && Volatile.Read(ref failure.Seq) == long.MaxValue) await queue.Writer.WriteAsync(new Batch(first, rows), ct);
            }
            catch (Exception error) { readError = error; }
            finally
            {
                queue.Writer.TryComplete();
                await Task.WhenAll(consumers);
            }
            ct.ThrowIfCancellationRequested();
            // A queued row's failure is lower than any later read error, as in the sequential scan.
            if (failure.Error is not null) ExceptionDispatchInfo.Capture(failure.Error).Throw();
            if (readError is not null) ExceptionDispatchInfo.Capture(readError).Throw();
            foreach (var part in parts)
            {
                overall.Digest = overall.Digest.Combine(part.Digest);
                overall.Ids.AddRange(part.Ids);
            }
            return overall;
        }
        finally { await database.Database.CloseConnectionAsync(); }
    }

    private static object[] Values(System.Data.Common.DbDataReader reader)
    {
        var values = new object[reader.FieldCount];
        reader.GetValues(values);
        return values;
    }

    private static void Add(Partial part, string table, int identity, object[] values)
    {
        using var row = new BinaryOutput();
        row.Text(table);
        foreach (var value in values)
        {
            switch (value)
            {
                case DBNull: row.Byte(0); break;
                case long number: row.Byte(1); row.Integer(number); break;
                case double number: row.Byte(2); row.Integer(BitConverter.DoubleToInt64Bits(number)); break;
                case string text: row.Byte(3); row.Text(text); break;
                case byte[] bytes: row.Byte(4); row.Bytes(bytes); break;
                default: throw new InvalidDataException("Unsupported stored replay value.");
            }
        }
        part.Digest = part.Digest.Add(row.Finish());
        if (identity >= 0) part.Ids.Add(new Guid((byte[])values[identity], bigEndian: true));
    }
}
