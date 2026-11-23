using SIL.Harmony.Changes;

namespace SIL.Motif.RecordStore;

public sealed partial class RecordStore
{
    /// <summary>
    /// Commits authored list operations and republishes the index and digests. This is the entry the typed list
    /// edit contract will use; it is internal until that contract has its own guarded revision and reference checks.
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
        // Dry-run against current records: a refused operation is rejected here, before anything is stored.
        var records = (await ReadRecordsAsync()).ToDictionary(r => r.Id, r => (GenericRecord)r.Copy());
        foreach (var operation in batch)
        {
            if (!records.TryGetValue(operation.EntityId, out var record))
                throw new ListOperationRefusedException("A list operation names an unknown record.");
            if (record.ApplyListOp(operation.Field, operation.Operation, operation.Seen) == ListOutcome.Refused)
                throw new ListOperationRefusedException("A list operation is malformed, beyond a list limit, or not addressed to a list field.");
        }
        // The history commits before the publication does; a crash between them leaves the certificate flagged.
        await MarkWriteInProgressAsync();
        await model.AddManyChanges(clientId, batch.Cast<IChange>(), () => null, MaximumChangesPerCommit);
        database.ChangeTracker.Clear();
        WriteStageReached?.Invoke(WriteStage.HistoryWritten);
        await PublishIndexAsync(await ReadRecordsAsync(), await CommitRevisionAsync());
    }
}
