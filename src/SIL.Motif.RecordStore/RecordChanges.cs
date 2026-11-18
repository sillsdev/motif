using SIL.Harmony.Entities;
using SIL.Harmony.Config;
using System.Text.Json;
using SIL.Harmony;
using SIL.Harmony.Changes;
using SIL.Harmony.Core;

namespace SIL.Motif.RecordStore;

internal sealed class CreateRecord(Guid entityId, string kind, int classId, Guid? owner, int owningField,
    int ownOrdinal, byte identityFields, JsonElement layout, NativeProvenance? provenance)
    : CreateChange<GenericRecord>(entityId), ISelfNamedType<CreateRecord>
{
    public string Kind { get; } = kind;
    public int ClassId { get; } = classId;
    public Guid? Owner { get; } = owner;
    public int OwningField { get; } = owningField;
    public int OwnOrdinal { get; } = ownOrdinal;
    public byte IdentityFields { get; } = identityFields;
    public JsonElement Layout { get; } = layout;
    public NativeProvenance? Provenance { get; } = provenance;
    public override ValueTask<GenericRecord> NewEntity(Commit commit, IChangeContext context) => ValueTask.FromResult(
        RecordInitialization.Create(EntityId, Kind, ClassId, Owner, OwningField, OwnOrdinal, IdentityFields, Layout, Provenance));
}

internal sealed class SetRecordValue(Guid entityId, string field, JsonElement value)
    : EditChange<GenericRecord>(entityId), ISelfNamedType<SetRecordValue>
{
    public string Field { get; } = field;
    public JsonElement Value { get; } = value.Clone();
    public override ValueTask ApplyChange(GenericRecord entity, IChangeContext context)
    {
        // A whole-value write would discard list history; list fields change only through list operations.
        if (int.TryParse(Field, out _) && RecordInitialization.Type(entity, Field) >= 25) return ValueTask.CompletedTask;
        entity.Values[Field] = Value;
        return ValueTask.CompletedTask;
    }
}

internal sealed class SetRecordAlternative(Guid entityId, int field, string tag, JsonElement value)
    : EditChange<GenericRecord>(entityId), ISelfNamedType<SetRecordAlternative>
{
    public int Field { get; } = field;
    public string Tag { get; } = tag;
    public JsonElement Value { get; } = value.Clone();
    public override ValueTask ApplyChange(GenericRecord entity, IChangeContext context)
    {
        var key = Field.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var alternatives = entity.Values.TryGetValue(key, out var prior)
            ? prior.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal) : new();
        alternatives[Tag] = Value;
        entity.Values[key] = JsonSerializer.SerializeToElement(alternatives);
        return ValueTask.CompletedTask;
    }
}

/// <summary>The compact encoding of an initial-chain list insert: the item appended at the next position of the import order.</summary>
internal sealed class InsertRecordItem(Guid entityId, int field, Guid item, int index)
    : EditChange<GenericRecord>(entityId), ISelfNamedType<InsertRecordItem>
{
    public int Field { get; } = field;
    public Guid Item { get; } = item;
    public int Index { get; } = index;
    public override ValueTask ApplyChange(GenericRecord entity, IChangeContext context)
    {
        var history = entity.HistoryFor(Field);
        if (history.Ops.Count != 0 || Index != history.Initial.Count)
            throw new InvalidDataException("An initial list insert must append to an unedited list.");
        // Only import writes these, so a refusal means the stored baseline itself is damaged.
        if (entity.ApplyListOp(Field, history.NextInitial(Item)) == ListOutcome.Refused)
            throw new InvalidDataException("An initial list insert was refused.");
        return ValueTask.CompletedTask;
    }
}
