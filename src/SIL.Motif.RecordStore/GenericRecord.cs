using SIL.Harmony.Entities;
using System.Text.Json;
using SIL.Harmony.Core;

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

    // Deletion must go through an explicit usage-warning transaction, never Harmony's link cascade.
    public Guid[] GetReferences() => [];
    public void RemoveReference(Guid id, CommitBase commit) =>
        throw new InvalidOperationException("Referenced records require an explicit deletion transaction.");

    public IObjectBase Copy() => new GenericRecord
    {
        Id = Id, DeletedAt = DeletedAt, Kind = Kind, ClassId = ClassId, Owner = Owner,
        OwningField = OwningField, Values = new(Values, StringComparer.Ordinal)
    };
}

public interface IRecordList
{
    IReadOnlyList<Guid> Read(GenericRecord record, int field);
    void Insert(GenericRecord record, int field, Guid item, int index);
}

internal sealed class IndexRecordList : IRecordList
{
    public IReadOnlyList<Guid> Read(GenericRecord record, int field) =>
        record.Values.TryGetValue(field.ToString(System.Globalization.CultureInfo.InvariantCulture), out var value)
            ? value.EnumerateArray().Select(x => x.GetGuid()).ToArray() : [];

    public void Insert(GenericRecord record, int field, Guid item, int index)
    {
        var items = Read(record, field).ToList();
        if (index < 0 || index > items.Count) throw new ArgumentOutOfRangeException(nameof(index));
        items.Insert(index, item);
        record.Values[field.ToString(System.Globalization.CultureInfo.InvariantCulture)] = JsonSerializer.SerializeToElement(items);
    }
}
