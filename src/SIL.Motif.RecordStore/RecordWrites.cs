using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace SIL.Motif.RecordStore;

public sealed partial class RecordStore
{
    private bool requiresReopen;
    private readonly string writeLockPath;

    // Keep the inode stable so an older handle and a new opener cannot lock different files.
    private FileStream AcquireWriter() => new(writeLockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    /// <summary>
    /// Plans a deletion against the current store revision. Cost: reacquires a full reference snapshot
    /// (about 4 s on the Indonesian copy, about 85 s on the Cebuano copy); UI callers must not call this per
    /// interaction until lazy snapshot decoding exists, and should hold a published snapshot instead.
    /// <paramref name="acknowledgedPlanToken"/> is the <see cref="DeletionImpact.PlanToken"/> the user was shown;
    /// it must equal the token of the plan computed now.
    /// </summary>
    public async Task<DeletionImpact> PrepareDeletionAsync(Guid target, string expectedRevision, string? acknowledgedPlanToken)
    {
        var index = await ReferencesAsync();
        if (index.Revision != expectedRevision) throw new StaleRevisionException("Deletion warning belongs to another store revision.");
        var impact = index.DeletionImpact(target);
        impact.RequireDeletionAllowed(acknowledgedPlanToken);
        return impact;
    }

    /// <summary>
    /// Guarded field write. Cost: reacquires a full snapshot and verifies it more than once (about 4 s or more
    /// on the Indonesian copy, about 85 s or more on Cebuano); UI callers must not call this per interaction until
    /// lazy snapshot decoding exists.
    /// </summary>
    public async Task<string> SetFieldAsync(Guid target, int field, JsonElement expectedValue, JsonElement value,
        string expectedRevision, Guid clientId)
    {
        using var writer = AcquireWriter();
        await using var transaction = await database.Database.BeginTransactionAsync();
        var writingStarted = false;
        try
        {
            var state = await PublishedStateAsync();
            if (state.Revision != expectedRevision) throw new StaleRevisionException("Field write belongs to another store revision.");
            if (state.ReplayDigest != await ReplayIntegrity.DigestAsync(database))
                throw new InvalidDataException("History is corrupt; the field write was refused.");
            var records = await ReadRecordsAsync();
            VerifySnapshots(state, records);
            var record = records.SingleOrDefault(r => r.Id == target && r.Kind == "lcm")
                ?? throw new InvalidOperationException("A field write requires a known native record.");
            var key = field.ToString(CultureInfo.InvariantCulture);
            if (field is 101 or 102 or 104 or 105 || !record.Values.TryGetValue(key, out var prior))
                throw new InvalidOperationException("Identity, ownership and unknown fields cannot be written through this seam.");
            if (!JsonElement.DeepEquals(prior, expectedValue)) throw new StalePriorValueException("The expected field value is stale.");
            var type = RecordInitialization.Type(record, key);
            ValidateField(type, field, value, records);
            if (!(await ReferencesAsync()).Incoming([target]).NativeComplete)
                throw new InvalidOperationException("A field write requires complete native reference closure.");
            if (JsonElement.DeepEquals(prior, value)) return state.Revision;
            record.Values[key] = value.Clone();
            if (ReferenceProjection.Build(records).Limitations.Length != 0)
                throw new InvalidOperationException("The field write would leave incomplete native references.");
            var expectedDigest = SnapshotDigest(records);
            writingStarted = true;
            await model.AddChange(clientId, new SetRecordValue(target, key, value));
            database.ChangeTracker.Clear();
            var written = await ReadRecordsAsync();
            if (SnapshotDigest(written) != expectedDigest) throw new InvalidDataException("The field write did not produce its prepared state.");
            var revision = await CommitRevisionAsync();
            await PublishIndexAsync(written, revision);
            await transaction.CommitAsync();
            return revision;
        }
        catch
        {
            requiresReopen |= writingStarted;
            database.ChangeTracker.Clear();
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static void ValidateField(int type, int field, JsonElement value, IReadOnlyList<GenericRecord> records)
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
        var referenced = records.SingleOrDefault(r => r.Kind == "lcm" && r.Id == value.GetGuid());
        if (referenced is null || NativeUsageSchema.Read(records)?.AcceptsReference(field, referenced.ClassId) != true)
            throw new InvalidOperationException("The reference target is unavailable or has the wrong native class.");
    }
}
