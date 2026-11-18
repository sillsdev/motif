using SIL.Harmony.Entities;
using System.Text.Json;
using SIL.Harmony.Core;
using SIL.ListCrdt;

namespace SIL.Motif.RecordStore;

public sealed class GenericRecord : IObjectBase<GenericRecord>
{
    public Guid Id { get; init; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string Kind { get; init; } = "lcm";
    public int ClassId { get; init; }
    public Guid? Owner { get; init; }
    public int OwningField { get; init; }
    public Dictionary<string, JsonElement> Values { get; set; } = new(StringComparer.Ordinal);

    public NativeProvenance? Provenance { get; init; }
    internal int OwnOrdinal { get; init; }
    internal byte IdentityFields { get; init; }
    internal JsonElement Layout { get; init; } = RecordInitialization.EmptyLayout;
    internal Dictionary<int, ListHistory> Lists { get; init; } = [];

    internal ListHistory HistoryFor(int field)
    {
        if (Lists.TryGetValue(field, out var history)) return history;
        history = new ListHistory();
        if (Values.TryGetValue(field.ToString(System.Globalization.CultureInfo.InvariantCulture), out var value) &&
            value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) history.Initial.Add(item.GetGuid());
        Lists[field] = history;
        return history;
    }

    internal ListOutcome ApplyListOp(int field, ListOp operation, IReadOnlyDictionary<string, long>? seen = null)
    {
        var key = field.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (RecordInitialization.Type(this, key) < 25) return ListOutcome.Refused;
        var history = HistoryFor(field);
        var outcome = history.TryApply(operation, seen);
        if (outcome != ListOutcome.Applied) return outcome;
        var distinct = RecordInitialization.Type(this, key) is 25 or 26;
        Values[key] = JsonSerializer.SerializeToElement(history.Visible(distinct));
        return outcome;
    }

    // Deletion must go through an explicit usage-warning transaction, never Harmony's link cascade.
    public Guid[] GetReferences() => [];
    public void RemoveReference(Guid id, CommitBase commit) =>
        throw new InvalidOperationException("Referenced records require an explicit deletion transaction.");

    public IObjectBase Copy() => new GenericRecord
    {
        Id = Id, DeletedAt = DeletedAt, Kind = Kind, ClassId = ClassId, Owner = Owner,
        OwningField = OwningField, Values = new(Values, StringComparer.Ordinal),
        OwnOrdinal = OwnOrdinal, IdentityFields = IdentityFields, Layout = Layout, Provenance = Provenance,
        Lists = Lists.ToDictionary(p => p.Key, p => p.Value.Clone())
    };
}

/// <summary>
/// Ordered owning and reference sequences are list CRDT operations (SIL.ListCrdt). Policy: a list never loses an
/// item to a concurrent edit; a delete wins over a concurrent move; a move that loses its position register is kept
/// and <see cref="Conflicts"/> reports it only when the winning move's author had not received it (a true concurrent
/// conflict; a sequential supersession is not one); collections show a target once.
/// </summary>
public interface IRecordList
{
    IReadOnlyList<Guid> Read(GenericRecord record, int field);
    IReadOnlyList<ListConflict> Conflicts(GenericRecord record, int field);
    IReadOnlyList<ListCheckpointRow> CheckpointRows(GenericRecord record, int field);
}
