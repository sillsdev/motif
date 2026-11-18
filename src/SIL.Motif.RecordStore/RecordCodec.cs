using System.Text.Json;
using System.Collections.Immutable;
using SIL.Harmony.Changes;
using SIL.Harmony.Core;

namespace SIL.Motif.RecordStore;

internal sealed class RecordCodec(RecordContent content)
{
    public byte[] Snapshot(IObjectBase entity)
    {
        var record = (GenericRecord)entity;
        using var output = new BinaryOutput();
        output.Byte(1);
        Header(output, record);
        output.Byte(record.DeletedAt.HasValue ? (byte)1 : (byte)0);
        if (record.DeletedAt.HasValue) output.Integer(record.DeletedAt.Value.UtcTicks);
        var fields = record.Values.Where(p => !RecordInitialization.IsInitial(record, p.Key, p.Value))
            .OrderBy(p => p.Key, StringComparer.Ordinal).ToArray();
        output.Unsigned((ulong)fields.Length);
        foreach (var (field, value) in fields)
        {
            Key(output, field);
            var type = RecordInitialization.Type(record, field);
            if (type is 14 or 16)
            {
                var alternatives = value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
                output.Unsigned((ulong)alternatives.Length);
                foreach (var alternative in alternatives) { Text(output, alternative.Name); Value(output, alternative.Value); }
            }
            else if (type >= 25)
            {
                output.Unsigned((ulong)value.GetArrayLength());
                foreach (var item in value.EnumerateArray()) Value(output, item);
            }
            else Value(output, value);
        }
        return output.Finish();
    }

    public IObjectBase Snapshot(byte[] bytes)
    {
        var input = Input(bytes);
        var record = Header(input);
        var deleted = input.Byte();
        if (deleted > 1) throw new InvalidDataException("Invalid deletion marker.");
        if (deleted == 1) record.DeletedAt = new DateTimeOffset(input.Integer(), TimeSpan.Zero);
        for (var n = input.Count(); n > 0; n--)
        {
            var field = Key(input);
            var type = RecordInitialization.Type(record, field);
            if (type is 14 or 16)
            {
                var alternatives = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                for (var count = input.Count(); count > 0; count--) alternatives.Add(Text(input), Value(input));
                record.Values[field] = JsonSerializer.SerializeToElement(alternatives);
            }
            else if (type >= 25)
            {
                var items = new List<JsonElement>();
                for (var count = input.Count(); count > 0; count--) items.Add(Value(input));
                record.Values[field] = JsonSerializer.SerializeToElement(items);
            }
            else record.Values[field] = Value(input);
        }
        input.End();
        return record;
    }

    public byte[] Change(IChange change)
    {
        using var output = new BinaryOutput();
        output.Byte(1);
        switch (change)
        {
            case CreateRecord create:
                output.Byte(1);
                Header(output, RecordInitialization.Create(create.EntityId, create.Kind, create.ClassId, create.Owner,
                    create.OwningField, create.OwnOrdinal, create.IdentityFields, create.Layout, create.Provenance));
                break;
            case SetRecordValue set:
                output.Byte(2); Guid(output, set.EntityId); Key(output, set.Field); Value(output, set.Value);
                break;
            case SetRecordAlternative alternative:
                output.Byte(3); Guid(output, alternative.EntityId); output.Unsigned((ulong)alternative.Field);
                Text(output, alternative.Tag); Value(output, alternative.Value);
                break;
            case InsertRecordItem item:
                output.Byte(4); Guid(output, item.EntityId); output.Unsigned((ulong)item.Field);
                Value(output, JsonSerializer.SerializeToElement(item.Item)); output.Unsigned((ulong)item.Index);
                break;
            default: throw new InvalidDataException("Unsupported record change.");
        }
        return output.Finish();
    }

    public IChange Change(byte[] bytes)
    {
        var input = Input(bytes);
        var kind = input.Byte();
        IChange change;
        if (kind == 1)
        {
            var r = Header(input);
            change = new CreateRecord(r.Id, r.Kind, r.ClassId, r.Owner, r.OwningField, r.OwnOrdinal, r.IdentityFields, r.Layout, r.Provenance);
        }
        else
        {
            var id = Guid(input);
            change = kind switch
            {
                2 => new SetRecordValue(id, Key(input), Value(input)),
                3 => new SetRecordAlternative(id, checked((int)input.Unsigned()), Text(input), Value(input)),
                4 => new InsertRecordItem(id, checked((int)input.Unsigned()), Value(input).GetGuid(), checked((int)input.Unsigned())),
                _ => throw new InvalidDataException("Unknown record change kind.")
            };
        }
        input.End();
        return change;
    }

    private static BinaryInput Input(byte[] bytes)
    {
        var input = new BinaryInput(bytes);
        if (input.Byte() != 1) throw new InvalidDataException("Unknown record codec version.");
        return input;
    }

    private void Header(BinaryOutput output, GenericRecord record)
    {
        Guid(output, record.Id);
        Text(output, record.Kind);
        output.Unsigned((ulong)record.ClassId);
        output.Byte(record.Owner.HasValue ? (byte)1 : (byte)0);
        if (record.Owner.HasValue) Guid(output, record.Owner.Value);
        output.Integer(record.OwningField);
        output.Integer(record.OwnOrdinal);
        output.Byte(record.IdentityFields);
        Value(output, record.Layout);
        if (record.Provenance is null) output.Unsigned(0);
        else
        {
            if (record.Provenance.EncounterOrdinal < 0 || record.Provenance.EncounterOrdinal == long.MaxValue)
                throw new InvalidDataException("Invalid encounter ordinal.");
            output.Unsigned((ulong)record.Provenance.EncounterOrdinal + 1);
            Text(output, record.Provenance.TraversalVersion);
            Value(output, JsonSerializer.SerializeToElement(record.Provenance.Vectors));
            Value(output, JsonSerializer.SerializeToElement(record.Provenance.RepositoryOrdinals));
        }
    }

    private GenericRecord Header(BinaryInput input)
    {
        var id = Guid(input);
        var kind = Text(input);
        var classId = checked((int)input.Unsigned());
        var ownerFlag = input.Byte();
        if (ownerFlag > 1) throw new InvalidDataException("Invalid owner marker.");
        Guid? owner = ownerFlag == 1 ? Guid(input) : null;
        var field = checked((int)input.Integer());
        var ordinal = checked((int)input.Integer());
        var flags = input.Byte();
        if (flags > 15) throw new InvalidDataException("Invalid identity marker.");
        var layout = Value(input);
        var encounter = input.Unsigned();
        NativeProvenance? provenance = encounter == 0 ? null : new NativeProvenance(checked((long)(encounter - 1)), Text(input),
            Value(input).Deserialize<ImmutableDictionary<int, ImmutableArray<Guid>>>()
                ?? throw new InvalidDataException("Missing native vector provenance."))
        {
            RepositoryOrdinals = Value(input).Deserialize<ImmutableDictionary<string, long>>()
                ?? throw new InvalidDataException("Missing native repository provenance.")
        };
        return RecordInitialization.Create(id, kind, classId, owner, field, ordinal, flags, layout, provenance);
    }

    private void Guid(BinaryOutput output, Guid value) => Value(output, JsonSerializer.SerializeToElement(value));
    private Guid Guid(BinaryInput input) => Value(input).GetGuid();
    private void Value(BinaryOutput output, JsonElement value) => output.Unsigned((ulong)content.Put(value));
    private JsonElement Value(BinaryInput input) => content.Get(checked((int)input.Unsigned()));
    private void Text(BinaryOutput output, string value) => Value(output, JsonSerializer.SerializeToElement(value));
    private string Text(BinaryInput input) => Value(input).GetString() ?? throw new InvalidDataException("Missing record text.");
    private static void Key(BinaryOutput output, string key)
    {
        if (uint.TryParse(key, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var field)
            && key == field.ToString(System.Globalization.CultureInfo.InvariantCulture))
        { output.Byte(0); output.Unsigned(field); }
        else { output.Byte(1); output.Text(key); }
    }
    private static string Key(BinaryInput input) => input.Byte() switch
    {
        0 => checked((uint)input.Unsigned()).ToString(System.Globalization.CultureInfo.InvariantCulture),
        1 => input.Text(),
        _ => throw new InvalidDataException("Invalid field key.")
    };
}
