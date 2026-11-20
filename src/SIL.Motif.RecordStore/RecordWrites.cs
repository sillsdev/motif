using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace SIL.Motif.RecordStore;

public sealed partial class RecordStore
{
    private bool requiresReopen;
    private VerifiedPoint? point;
    private readonly string writeLockPath;

    // Keep the inode stable so an older handle and a new opener cannot lock different files.
    private FileStream AcquireWriter() => readOnly
        ? throw new InvalidOperationException("This store handle is read-only.")
        : new(writeLockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    /// <summary>
    /// Plans a deletion against the current store revision. The first call on a handle verifies the published point
    /// (the reference-snapshot acquisition cost); later calls reuse that verified point while the published state
    /// is unchanged and cost one state read. <paramref name="acknowledgedPlanToken"/> is the
    /// <see cref="DeletionImpact.PlanToken"/> the user was shown; it must equal the token of the plan computed now.
    /// </summary>
    public async Task<DeletionImpact> PrepareDeletionAsync(Guid target, string expectedRevision, string? acknowledgedPlanToken)
    {
        var verified = await PointAsync();
        if (verified.State.Revision != expectedRevision) throw new StaleRevisionException("Deletion warning belongs to another store revision.");
        var impact = verified.GetPlanningIndex().DeletionImpact(target);
        impact.RequireDeletionAllowed(acknowledgedPlanToken);
        return impact;
    }

    /// <summary>
    /// Guarded field write. The first call on a handle verifies the published point and scans history once; later
    /// calls update the retained point, digests and persisted index incrementally, so their cost does not grow with
    /// the project. Anything that makes the published state differ from the retained point forces a full reacquisition.
    /// </summary>
    public async Task<string> SetFieldAsync(Guid target, int field, JsonElement expectedValue, JsonElement value,
        string expectedRevision, Guid clientId)
    {
        using var writer = AcquireWriter();
        await using var transaction = await database.Database.BeginTransactionAsync();
        var writingStarted = false;
        try
        {
            var verified = await PointAsync();
            var state = verified.State;
            if (state.Revision != expectedRevision) throw new StaleRevisionException("Field write belongs to another store revision.");
            var replay = verified.Replay ?? await ReplayIntegrity.ScanAsync(database, Workers);
            if (state.ReplayDigest != replay.Final()) throw new InvalidDataException("History is corrupt; the field write was refused.");
            var record = verified.Records.GetValueOrDefault(target)
                ?? throw new InvalidOperationException("A field write requires a known native record.");
            var key = field.ToString(CultureInfo.InvariantCulture);
            if (field is 101 or 102 or 104 or 105 || !record.Values.TryGetValue(key, out var prior))
                throw new InvalidOperationException("Identity, ownership and unknown fields cannot be written through this seam.");
            if (!JsonElement.DeepEquals(prior, expectedValue)) throw new StalePriorValueException("The expected field value is stale.");
            var type = RecordInitialization.Type(record, key);
            if (type >= 25) throw new ListFieldWriteException("A list field cannot be written as a whole value; use list operations.");
            ValidateField(type, field, value, verified.Records, verified.Index.UsageSchema);
            if (!verified.Index.Incoming([target]).NativeComplete)
                throw new InvalidOperationException("A field write requires complete native reference closure.");
            if (JsonElement.DeepEquals(prior, value)) return state.Revision;
            // Only a non-owning single reference carries an index edge; other supported types have none.
            NativeReference? removed = type == 24 && prior.ValueKind != JsonValueKind.Null ? new(target, field, -1, prior.GetGuid(), false) : null;
            NativeReference? added = type == 24 && value.ValueKind != JsonValueKind.Null ? new(target, field, -1, value.GetGuid(), false) : null;
            var updated = (GenericRecord)record.Copy();
            updated.Values[key] = value.Clone();
            var expectedItem = SnapshotItem(updated);
            writingStarted = true;
            var commit = await model.AddChange(clientId, new SetRecordValue(target, key, value));
            database.ChangeTracker.Clear();
            var written = await model.GetLatest<GenericRecord>(target);
            if (written is null || !SnapshotItem(written).AsSpan().SequenceEqual(expectedItem))
                throw new InvalidDataException("The field write did not produce its prepared state.");
            var revision = await CommitRevisionAsync();
            var extended = await ReplayIntegrity.ExtendAsync(database, replay, commit.Id);
            var snapshot = verified.Snapshot.Remove(SnapshotItem(record)).Add(expectedItem);
            var index = verified.IndexDigest.WithEdge(removed, added);
            await ReferencePersistence.ApplyAsync(database, removed, added);
            var stored = await database.Set<StoreState>().SingleAsync();
            stored.Revision = revision;
            stored.IndexDigest = index.Final();
            stored.ReplayDigest = extended.Final();
            stored.SnapshotDigest = snapshot.Final("snapshot");
            stored.ContentDigest = content.Digest();
            stored.Published = true;
            await database.SaveChangesAsync();
            var published = await database.Set<StoreState>().AsNoTracking().SingleAsync();
            database.ChangeTracker.Clear();
            await transaction.CommitAsync();
            var records = new Dictionary<Guid, GenericRecord>(verified.Records) { [target] = updated };
            point = new VerifiedPoint(published, records, verified.Index.WithEdge(revision, removed, added), snapshot, index)
            { Replay = extended, Anchors = verified.Anchors, ProjectId = verified.ProjectId };
            return revision;
        }
        catch
        {
            requiresReopen |= writingStarted;
            if (writingStarted) point = null;
            database.ChangeTracker.Clear();
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static void ValidateField(int type, int field, JsonElement value, IReadOnlyDictionary<Guid, GenericRecord> records, NativeUsageSchema? schema)
    {
        var valid = type switch
        {
            1 => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            2 => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
            6 => value.ValueKind == JsonValueKind.String && value.TryGetGuid(out _),
            24 => value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.String && value.TryGetGuid(out _),
            _ => false
        };
        if (!valid) throw new InvalidOperationException("Unsupported field write or value; ownership, multilingual and list writes require typed contracts.");
        if (type != 24 || value.ValueKind == JsonValueKind.Null) return;
        if (!records.TryGetValue(value.GetGuid(), out var referenced) || schema?.AcceptsReference(field, referenced.ClassId) != true)
            throw new InvalidOperationException("The reference target is unavailable or has the wrong native class.");
    }
}
