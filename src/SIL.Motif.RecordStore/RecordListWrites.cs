using SIL.Harmony.Changes;

namespace SIL.Motif.RecordStore;

public sealed partial class RecordStore
{
    /// <summary>
    /// Commits authored list operations and republishes the index, digests and derived tables. This is the entry the typed list
    /// edit contract will use; it is internal until that contract has its own guarded revision and reference checks.
    /// The history rows and the publication are one transaction: a crash or failure at any point before its commit leaves the
    /// store exactly as it was, so no history can be committed ahead of its publication. A refused operation writes nothing.
    /// </summary>
    internal async Task CommitListOperationsAsync(IEnumerable<ListRecordOp> operations, Guid clientId)
    {
        using var writer = AcquireWriter();
        point = null;
        var state = await PublishedStateAsync();
        // The republication rescans history, so the old rows must match the stored digest before they are absorbed.
        if (state.ReplayDigest != await ReplayIntegrity.DigestAsync(database, Workers))
            throw new InvalidDataException("History is corrupt; the list operations were refused.");
        var batch = operations.ToArray();
        // An empty batch writes nothing, so it must not republish either.
        if (batch.Length == 0) return;
        // The snapshots are about to be republished, so they too must match the stored digest first.
        var current = await DecodeLatestAsync(digest: true, default);
        VerifySnapshots(state, current.Digest);
        // Dry-run against current records: a refused operation is rejected here, before anything is stored.
        var records = current.Records.ToDictionary(r => r.Id, r => (GenericRecord)r.Copy());
        foreach (var operation in batch)
        {
            if (!records.TryGetValue(operation.EntityId, out var record))
                throw new ListOperationRefusedException("A list operation names an unknown record.");
            if (record.ApplyListOp(operation.Field, operation.Operation, operation.Seen) == ListOutcome.Refused)
                throw new ListOperationRefusedException("A list operation is malformed, beyond a list limit, or not addressed to a list field.");
        }
        await PublishHistoryAsync(batch.Cast<IChange>().ToArray(), clientId);
    }

    /// <summary>Test seam: commits any changes through the same guarded one-transaction publication, with no list dry-run.</summary>
    internal async Task CommitChangesAsync(IEnumerable<IChange> changes, Guid clientId)
    {
        using var writer = AcquireWriter();
        point = null;
        var state = await PublishedStateAsync();
        if (state.ReplayDigest != await ReplayIntegrity.DigestAsync(database, Workers))
            throw new InvalidDataException("History is corrupt; the changes were refused.");
        VerifySnapshots(state, (await DecodeLatestAsync(digest: true, default)).Digest);
        await PublishHistoryAsync(changes.ToArray(), clientId);
    }

    private async Task PublishHistoryAsync(IChange[] batch, Guid clientId)
    {
        await using var transaction = await database.Database.BeginTransactionAsync();
        try
        {
            // AddManyChanges starts its own transaction; AddChanges joins this one.
            foreach (var part in batch.Chunk(MaximumChangesPerCommit)) await model.AddChanges(clientId, part);
            database.ChangeTracker.Clear();
            WriteStageReached?.Invoke(WriteStage.HistoryWritten);
            var latest = await DecodeLatestAsync(digest: false, default);
            await PublishIndexAsync(latest.Records, await CommitRevisionAsync(), tombstones: latest.Tombstones, refuseUnprovenNative: true);
            WriteStageReached?.Invoke(WriteStage.StateWritten);
            await transaction.CommitAsync();
            WriteStageReached?.Invoke(WriteStage.Committed);
        }
        catch
        {
            requiresReopen = true;
            throw;
        }
    }
}
