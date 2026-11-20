using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using SIL.Motif.Contract.HandAnalyses;
using SIL.Motif.Contract.HandAnalysis;

namespace SIL.Motif.RecordStore.HandAnalysis;

/// <summary>Field ids of the lexical model, resolved by owner, name and type from the retained schema.</summary>
internal sealed class LexSchema
{
    public readonly int Citation, LexemeForm, AlternateForms, Senses, EntryRefs, Msas, Homograph;
    public readonly int Gloss, Definition, SenseMsa, SubSenses;
    public readonly int FormText, FormMorphType, FormAbstract, Prefix, Postfix;
    public readonly int Components, VariantTypes, ComplexTypes, RefType, GlossPrepend, GlossAppend;
    public readonly int Name, Abbreviation, SubPossibilities, Possibilities;
    public readonly NativeUsageSchema Usage;

    public LexSchema(NativeUsageSchema usage)
    {
        Usage = usage;
        int Need(string owner, string name, int type) => usage.FieldId(owner, name, type)
            ?? throw new IndexUnavailableException($"The retained schema does not define {owner}.{name} exactly once.");
        Citation = Need("LexEntry", "CitationForm", 16); LexemeForm = Need("LexEntry", "LexemeForm", 23);
        AlternateForms = Need("LexEntry", "AlternateForms", 27); Senses = Need("LexEntry", "Senses", 27);
        EntryRefs = Need("LexEntry", "EntryRefs", 27); Msas = Need("LexEntry", "MorphoSyntaxAnalyses", 25);
        Homograph = Need("LexEntry", "HomographNumber", 2);
        Gloss = Need("LexSense", "Gloss", 16); Definition = Need("LexSense", "Definition", 14);
        SenseMsa = Need("LexSense", "MorphoSyntaxAnalysis", 24); SubSenses = Need("LexSense", "Senses", 27);
        FormText = Need("MoForm", "Form", 16); FormMorphType = Need("MoForm", "MorphType", 24);
        FormAbstract = Need("MoForm", "IsAbstract", 1);
        Prefix = Need("MoMorphType", "Prefix", 15); Postfix = Need("MoMorphType", "Postfix", 15);
        Components = Need("LexEntryRef", "ComponentLexemes", 28); VariantTypes = Need("LexEntryRef", "VariantEntryTypes", 28);
        ComplexTypes = Need("LexEntryRef", "ComplexEntryTypes", 28); RefType = Need("LexEntryRef", "RefType", 2);
        GlossPrepend = Need("LexEntryInflType", "GlossPrepend", 16); GlossAppend = Need("LexEntryInflType", "GlossAppend", 16);
        Name = Need("CmPossibility", "Name", 16); Abbreviation = Need("CmPossibility", "Abbreviation", 16);
        SubPossibilities = Need("CmPossibility", "SubPossibilities", 27); Possibilities = Need("CmPossibilityList", "Possibilities", 27);
    }
}

/// <summary>One allomorph with every fact a form lookup returns; nothing here refers to a native record.</summary>
internal sealed record FormRow(FormHit Hit, EncounterKey Encounter, bool IsPattern);

internal sealed record EntryRow(LexicalFact Fact, EncounterKey Encounter);

/// <summary>Candidate entries of one writing system, by collation key and by ordinal NFD text for prefixes.</summary>
internal sealed class TextIndex(Dictionary<string, List<int>> byKey, (string Nfd, int Entry)[] sorted)
{
    public readonly Dictionary<string, List<int>> ByKey = byKey;
    public readonly (string Nfd, int Entry)[] Sorted = sorted;
}

/// <summary>
/// The lexical half of a pinned point: entry closures, allomorph rows and their indexes. Immutable once built;
/// lookups count the rows they examine so a test can prove a path never falls back to a scan.
/// </summary>
internal sealed class LexiconProjection
{
    private readonly RowCounter counter;

    private LexiconProjection(RowCounter counter) => this.counter = counter;

    public CountedRows<EntryRow> Entries { get; private init; } = null!;
    public ImmutableDictionary<Guid, int> EntryIndex { get; private init; } = ImmutableDictionary<Guid, int>.Empty;
    public ImmutableDictionary<Guid, int> SenseEntry { get; private init; } = ImmutableDictionary<Guid, int>.Empty;
    public ImmutableDictionary<Guid, int> MsaEntry { get; private init; } = ImmutableDictionary<Guid, int>.Empty;
    public ImmutableDictionary<Guid, int> RefEntry { get; private init; } = ImmutableDictionary<Guid, int>.Empty;
    public ImmutableDictionary<Guid, SenseFact> SenseById { get; private init; } = ImmutableDictionary<Guid, SenseFact>.Empty;
    public ImmutableDictionary<Guid, MsaFact> MsaById { get; private init; } = ImmutableDictionary<Guid, MsaFact>.Empty;
    public CountedRows<FormRow> Forms { get; private init; } = null!;
    public ImmutableDictionary<Guid, int> FormIndex { get; private init; } = ImmutableDictionary<Guid, int>.Empty;
    public ImmutableDictionary<(string Ws, string Key), ImmutableArray<int>> FormsByKey { get; private init; } =
        ImmutableDictionary<(string, string), ImmutableArray<int>>.Empty;
    /// <summary>Pattern allomorphs only, by the marker pair of their type.</summary>
    public ImmutableDictionary<(Guid Type, string Before, string After), ImmutableArray<int>> PatternsByPair { get; private init; } =
        ImmutableDictionary<(Guid, string, string), ImmutableArray<int>>.Empty;
    public ImmutableDictionary<Guid, ImmutableArray<int>> FormsByEntry { get; private init; } =
        ImmutableDictionary<Guid, ImmutableArray<int>>.Empty;
    public ImmutableDictionary<(LexicalField Field, string Ws), TextIndex> Search { get; private init; } =
        ImmutableDictionary<(LexicalField, string), TextIndex>.Empty;
    public ImmutableDictionary<Guid, ImmutableArray<Guid>> AnalysesUsing { get; private init; } =
        ImmutableDictionary<Guid, ImmutableArray<Guid>>.Empty;

    /// <summary>Rows read by lookups since the projection was built.</summary>
    public long Touched => counter.Value;

    public static LexiconProjection Build(IReadOnlyDictionary<Guid, GenericRecord> native, NativeUsageSchema usage,
        WfiProjection wfi, ProjectProjection project, IFormCollation collation, CancellationToken ct)
    {
        try { return BuildProjection(native, usage, wfi, project, collation, ct); }
        catch (Exception exception) when (exception is not IndexUnavailableException && exception is InvalidDataException
            or KeyNotFoundException or ArgumentException or FormatException or JsonException or InvalidOperationException)
        {
            throw new IndexUnavailableException("A native lexical fact cannot be read: " + exception.Message, exception);
        }
    }

    private static LexiconProjection BuildProjection(IReadOnlyDictionary<Guid, GenericRecord> native, NativeUsageSchema usage,
        WfiProjection wfi, ProjectProjection project, IFormCollation collation, CancellationToken ct)
    {
        var schema = new LexSchema(usage);
        var entryRecords = native.Values.Where(r => usage.Is(r.ClassId, "LexEntry")).ToList();
        entryRecords.Sort((a, b) => WfiProjection.Ordinal(a, "LexEntry").CompareTo(WfiProjection.Ordinal(b, "LexEntry")));
        var morphAnalyses = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var (analysis, reading) in wfi.Readings)
            foreach (var bundle in reading.Fact.Bundles)
                foreach (var id in new[] { bundle.MorphId, bundle.SenseId, bundle.MsaId, bundle.InflTypeId }.OfType<Guid>())
                {
                    if (!morphAnalyses.TryGetValue(id, out var set)) morphAnalyses[id] = set = [];
                    set.Add(analysis);
                }

        var entries = ImmutableArray.CreateBuilder<EntryRow>(entryRecords.Count);
        var forms = new List<FormRow>();
        var formIndex = new Dictionary<Guid, int>();
        var byKey = new Dictionary<(string, string), List<int>>();
        var byPair = new Dictionary<(Guid, string, string), List<int>>();
        var byEntry = new Dictionary<Guid, List<int>>();
        var senseEntry = new Dictionary<Guid, int>();
        var msaEntry = new Dictionary<Guid, int>();
        var refEntry = new Dictionary<Guid, int>();
        var senseById = new Dictionary<Guid, SenseFact>();
        var msaById = new Dictionary<Guid, MsaFact>();
        var search = new Dictionary<(LexicalField, string), Dictionary<string, List<int>>>();
        var searchText = new Dictionary<(LexicalField, string), List<(string, int)>>();
        void Index(LexicalField field, string ws, string text, int entry)
        {
            if (text.Length == 0) return;
            var nfd = WfiProjection.Nfd(text);
            var slot = (field, ws);
            if (!search.TryGetValue(slot, out var keys)) { search[slot] = keys = []; searchText[slot] = []; }
            if (!keys.TryGetValue(collation.Key(ws, nfd), out var rows)) keys[collation.Key(ws, nfd)] = rows = [];
            if (rows.Count == 0 || rows[^1] != entry) { rows.Add(entry); searchText[slot].Add((nfd, entry)); }
        }

        foreach (var entry in entryRecords)
        {
            ct.ThrowIfCancellationRequested();
            var ordinal = entries.Count;
            var encounter = WfiProjection.Encounter(entry, "LexEntry");
            var citation = Multi(entry, schema.Citation);
            var lexemeId = Atom(entry, schema.LexemeForm);
            var alternateIds = Seq(entry, schema.AlternateForms).ToImmutableArray();
            var formRecords = (lexemeId is { } lex ? new[] { lex } : []).Concat(alternateIds).Select(id => WfiProjection.Get(native, id)).ToArray();
            var headword = formRecords.Length > 0 && lexemeId is not null ? Multi(formRecords[0], schema.FormText) : ImmutableDictionary<string, string>.Empty;
            headword = citation.Where(p => p.Value.Length > 0).Aggregate(headword, (h, p) => h.SetItem(p.Key, p.Value));
            var homograph = Int(entry, schema.Homograph);

            var senses = ImmutableArray.CreateBuilder<SenseFact>();
            void Walk(Guid owner, Guid? parent, string prefix, IEnumerable<Guid> ids)
            {
                var n = 0;
                foreach (var id in ids)
                {
                    ct.ThrowIfCancellationRequested();
                    var record = WfiProjection.Get(native, id);
                    var number = prefix.Length == 0 ? (++n).ToString(CultureInfo.InvariantCulture)
                        : prefix + "." + (++n).ToString(CultureInfo.InvariantCulture);
                    var fact = new SenseFact(id, parent, number, Multi(record, schema.Gloss), Multi(record, schema.Definition),
                        Atom(record, schema.SenseMsa));
                    senses.Add(fact);
                    senseById[id] = fact; senseEntry[id] = ordinal;
                    Walk(owner, id, number, Seq(record, schema.SubSenses));
                }
            }
            Walk(entry.Id, null, "", Seq(entry, schema.Senses));

            var msas = ImmutableArray.CreateBuilder<MsaFact>();
            foreach (var id in WfiProjection.Members(entry, schema.Msas))
            {
                var msa = Msa(native, usage, WfiProjection.Get(native, id));
                msas.Add(msa); msaById[id] = msa; msaEntry[id] = ordinal;
            }

            var references = ImmutableArray.CreateBuilder<LexicalReferenceFact>();
            var inflected = new List<InflectionalTypeFact>();
            var order = 0;
            foreach (var id in Seq(entry, schema.EntryRefs))
            {
                var record = WfiProjection.Get(native, id);
                var variant = Int(record, schema.RefType) == 0;
                var types = Seq(record, variant ? schema.VariantTypes : schema.ComplexTypes).ToImmutableArray();
                var components = Seq(record, schema.Components).Select(c => Component(native, usage, c)).ToImmutableArray();
                references.Add(new(id, entry.Id, variant ? "variant" : "complex-form", order++, components, types));
                refEntry[id] = ordinal;
                foreach (var type in types)
                    if (usage.Is(WfiProjection.Get(native, type).ClassId, "LexEntryInflType") &&
                        inflected.All(i => i.Id != type))
                    {
                        var t = WfiProjection.Get(native, type);
                        inflected.Add(new(type, Multi(t, schema.GlossPrepend), Multi(t, schema.GlossAppend)));
                    }
            }

            var fact = new LexicalFact(entry.Id, headword, citation, homograph, lexemeId, alternateIds, senses.ToImmutable(),
                msas.ToImmutable(), references.ToImmutable(), [.. inflected], []);
            entries.Add(new(fact, encounter));

            for (var position = 0; position < formRecords.Length; position++)
            {
                var record = formRecords[position];
                var texts = Multi(record, schema.FormText);
                var typeId = Atom(record, schema.FormMorphType);
                var type = typeId is { } t ? project.MorphTypeById.GetValueOrDefault(t) : null;
                var pattern = usage.Is(record.ClassId, "MoAffixProcess");
                var markers = type is null ? [] : project.PairsOf(type);
                var keys = texts.ToImmutableDictionary(p => p.Key, p => collation.Key(p.Key, WfiProjection.Nfd(p.Value)), StringComparer.Ordinal);
                var discriminator = type is null ? null : $"{type.Id:N}|{type.Markers.Before}|{type.Markers.After}";
                var hit = new FormHit(record.Id, usage.ClassName(record.ClassId)!, entry.Id, lexemeId == record.Id, position, texts,
                    typeId, record.Values.TryGetValue(WfiProjection.Key(schema.FormAbstract), out var abs) && abs.ValueKind == JsonValueKind.True,
                    pattern && discriminator is not null ? new FormPattern(discriminator, texts.Values.FirstOrDefault() ?? "") : null,
                    morphAnalyses.TryGetValue(record.Id, out var used) ? used.Count : 0,
                    $"{WfiProjection.Ordinal(record, "MoForm"):D19}:{record.Provenance!.EncounterOrdinal:D19}", "", "", keys);
                var index = forms.Count;
                forms.Add(new(hit, WfiProjection.Encounter(record, "MoForm"), pattern));
                formIndex[record.Id] = index;
                foreach (var (ws, key) in keys) Add(byKey, (ws, key), index);
                if (pattern) foreach (var pair in markers) Add(byPair, (type!.Id, pair.Before, pair.After), index);
                Add(byEntry, entry.Id, index);
                foreach (var (ws, text) in texts) Index(LexicalField.Form, ws, text, ordinal);
            }
            foreach (var (ws, text) in headword) Index(LexicalField.Headword, ws, text, ordinal);
            foreach (var sense in fact.Senses)
                foreach (var (ws, text) in sense.Gloss) Index(LexicalField.Sense, ws, text, ordinal);
        }

        var built = entries.MoveToImmutable();
        var counter = new RowCounter();
        return new(counter)
        {
            Entries = new(built, counter), EntryIndex = built.Select((e, i) => (e.Fact.EntryId, i)).ToImmutableDictionary(p => p.EntryId, p => p.i),
            SenseEntry = senseEntry.ToImmutableDictionary(), MsaEntry = msaEntry.ToImmutableDictionary(),
            RefEntry = refEntry.ToImmutableDictionary(), SenseById = senseById.ToImmutableDictionary(),
            MsaById = msaById.ToImmutableDictionary(), Forms = new([.. forms], counter),
            FormIndex = formIndex.ToImmutableDictionary(),
            FormsByKey = byKey.ToImmutableDictionary(p => p.Key, p => p.Value.ToImmutableArray()),
            PatternsByPair = byPair.ToImmutableDictionary(p => p.Key, p => p.Value.ToImmutableArray()),
            FormsByEntry = byEntry.ToImmutableDictionary(p => p.Key, p => p.Value.ToImmutableArray()),
            Search = search.ToImmutableDictionary(p => p.Key, p =>
                new TextIndex(p.Value, searchText[p.Key].OrderBy(x => x.Item1, StringComparer.Ordinal).ThenBy(x => x.Item2).ToArray())),
            AnalysesUsing = morphAnalyses.ToImmutableDictionary(p => p.Key, p => p.Value.ToImmutableArray())
        };
    }

    private static void Add<TKey>(Dictionary<TKey, List<int>> map, TKey key, int row) where TKey : notnull
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        list.Add(row);
    }

    private static HandAnalysisVariantComponent Component(IReadOnlyDictionary<Guid, GenericRecord> native,
        NativeUsageSchema usage, Guid id)
    {
        var record = WfiProjection.Get(native, id);
        if (usage.Is(record.ClassId, "LexEntry")) return new(id, null);
        var owner = record.Owner is { } o ? WfiProjection.Get(native, o) : null;
        while (owner is not null && !usage.Is(owner.ClassId, "LexEntry"))
            owner = owner.Owner is { } up ? WfiProjection.Get(native, up) : null;
        return new(owner?.Id, id);
    }

    private static MsaFact Msa(IReadOnlyDictionary<Guid, GenericRecord> native, NativeUsageSchema usage, GenericRecord msa)
    {
        var name = usage.ClassName(msa.ClassId)!;
        Guid? Pos(string field) => usage.FieldId(name, field, 24) is { } id ? Atom(msa, id) : null;
        var key = new List<string> { name };
        foreach (var (field, value) in msa.Values.OrderBy(p => int.Parse(p.Key, CultureInfo.InvariantCulture)))
        {
            var type = RecordInitialization.Type(msa, field);
            if (type is 23 or 25 or 27 || value.ValueKind == JsonValueKind.Null) continue;
            key.Add(field + "=" + (type is 26 or 28 && value.ValueKind == JsonValueKind.Array
                ? string.Join(",", value.EnumerateArray().Select(x => x.GetString())) : value.ToString()));
        }
        return new(msa.Id, name, Pos("PartOfSpeech"), Pos("FromPartOfSpeech"), Pos("ToPartOfSpeech"), string.Join("|", key));
    }

    private static IndexUnavailableException Corrupt(GenericRecord record, int field, string expected) =>
        new($"Native field {field} of {record.Id} is not {expected}.");

    /// <summary>A required integer: a missing or wrong-kind value makes the index unavailable.</summary>
    internal static int Int(GenericRecord record, int field) =>
        record.Values.TryGetValue(WfiProjection.Key(field), out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)
            ? n : throw Corrupt(record, field, "an integer");

    /// <summary>An optional reference. The native store omits an empty atom, so a missing key or null is no reference; any other kind makes the index unavailable.</summary>
    internal static Guid? Atom(GenericRecord record, int field)
    {
        if (!record.Values.TryGetValue(WfiProjection.Key(field), out var v) || v.ValueKind == JsonValueKind.Null) return null;
        return v.ValueKind == JsonValueKind.String && v.TryGetGuid(out var id) ? id : throw Corrupt(record, field, "a reference");
    }

    /// <summary>An optional sequence. The native store omits an empty sequence; any present value that is not an array of references makes the index unavailable.</summary>
    internal static IEnumerable<Guid> Seq(GenericRecord record, int field)
    {
        if (!record.Values.TryGetValue(WfiProjection.Key(field), out var v)) return [];
        if (v.ValueKind != JsonValueKind.Array) throw Corrupt(record, field, "a reference sequence");
        var ids = new List<Guid>();
        foreach (var x in v.EnumerateArray())
            ids.Add(x.ValueKind == JsonValueKind.String && x.TryGetGuid(out var id) ? id : throw Corrupt(record, field, "a reference sequence"));
        return ids;
    }

    /// <summary>An optional multi-string. The native store omits an empty one; any present value that is not an object of texts makes the index unavailable.</summary>
    internal static ImmutableDictionary<string, string> Multi(GenericRecord record, int field)
    {
        if (!record.Values.TryGetValue(WfiProjection.Key(field), out var v)) return ImmutableDictionary<string, string>.Empty;
        if (v.ValueKind != JsonValueKind.Object) throw Corrupt(record, field, "a multi-string");
        return WfiProjection.Texts(v);
    }
}
