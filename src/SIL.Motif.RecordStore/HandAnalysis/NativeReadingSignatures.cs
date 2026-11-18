using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.RecordStore.HandAnalysis;

internal sealed class NativeReadingSignatures(IReadOnlyDictionary<Guid, GenericRecord> records, NativeUsageSchema schema)
{
    private readonly Dictionary<Guid, ImmutableArray<GenericRecord>> entrySenses = [];

    internal ReadingSignature? Build(ImmutableArray<BundleFact> bundles)
    {
        if (bundles.IsEmpty) return null;
        var morphemes = ImmutableArray.CreateBuilder<ReadingMorpheme>(bundles.Length);
        foreach (var bundle in bundles)
        {
            var morpheme = Morpheme(bundle);
            if (morpheme is null) return null;
            morphemes.Add(morpheme);
        }
        return new(morphemes.MoveToImmutable());
    }

    private ReadingMorpheme? Morpheme(BundleFact bundle)
    {
        if (bundle.MorphId is not { } morphId || bundle.MsaId is not { } msaId ||
            morphId == Guid.Empty || msaId == Guid.Empty || bundle.InflTypeId == Guid.Empty ||
            !records.TryGetValue(morphId, out var morph) || !records.TryGetValue(msaId, out var msa) ||
            EntryOf(morph) is not { } entry || EntryOf(msa) is not { } main ||
            Field("LexSense", "MorphoSyntaxAnalysis", 24, "MoMorphSynAnalysis") is not { } senseMsa)
            return null;

        var candidates = Candidates(entry, main, bundle.InflTypeId)
            .Where(s => Link(s, senseMsa) == msaId)
            .Select(s => s.Id).Distinct().ToArray();
        if (candidates.Length == 0 ||
            bundle.SenseId is { } explicitSense && !candidates.Contains(explicitSense))
            return null;

        Guid? sense = bundle.SenseId ?? (candidates.Length == 1 ? candidates[0] : null);
        var features = ImmutableArray.CreateBuilder<ReadingFeatureValue>();
        if (!MsaFeatures(msa, features, out var derivation)) return null;
        var stem = schema.Is(msa.ClassId, "MoStemMsa");
        return new(Id(entry.Id), Id(sense), sense is null ? [.. candidates.Select(id => Id(id)!)] : [],
            sense is null ? ReadingSenseStatus.Ambiguous : ReadingSenseStatus.Resolved,
            Id(msaId), Id(morphId), Id(bundle.InflTypeId), stem, derivation, features.ToImmutable());
    }

    private IEnumerable<GenericRecord> Candidates(GenericRecord entry, GenericRecord main, Guid? inflType)
    {
        if (entry.Id == main.Id) return inflType is null ? Senses(main) : [];
        var senses = Senses(main);
        var refsField = Field("LexEntry", "EntryRefs", 27, "LexEntryRef");
        var typeField = Field("LexEntryRef", "RefType", 2);
        var typesField = Field("LexEntryRef", "VariantEntryTypes", 28, "LexEntryType");
        var componentsField = Field("LexEntryRef", "ComponentLexemes", 28, "CmObject");
        if (refsField is null || typeField is null || componentsField is null || inflType is not null && typesField is null)
            return [];

        var components = new HashSet<Guid>();
        foreach (var reference in Members(entry, refsField.Value))
        {
            if (!records.TryGetValue(reference, out var link) || link.Owner != entry.Id || link.OwningField != refsField ||
                !schema.Is(link.ClassId, "LexEntryRef") ||
                !link.Values.TryGetValue(Key(typeField.Value), out var type) || type.GetInt32() != 0 ||
                inflType is { } id && !Members(link, typesField!.Value).Contains(id))
                continue;
            var targets = Members(link, componentsField.Value).Take(2).ToArray();
            if (targets.Length == 1) components.Add(targets[0]);
        }
        return components.Contains(main.Id) ? senses : senses.Where(s => components.Contains(s.Id));
    }

    private ImmutableArray<GenericRecord> Senses(GenericRecord entry)
    {
        if (entrySenses.TryGetValue(entry.Id, out var cached)) return cached;
        var entryField = Field("LexEntry", "Senses", 27, "LexSense");
        var senseField = Field("LexSense", "Senses", 27, "LexSense");
        if (entryField is null || senseField is null) return [];

        var senses = ImmutableArray.CreateBuilder<GenericRecord>();
        var pending = new Stack<GenericRecord>();
        var seen = new HashSet<Guid>();
        pending.Push(entry);
        while (pending.TryPop(out var parent))
        {
            var field = parent.Id == entry.Id ? entryField.Value : senseField.Value;
            foreach (var id in Members(parent, field))
            {
                if (id == Guid.Empty || !seen.Add(id) || !records.TryGetValue(id, out var child) ||
                    child.Owner != parent.Id || child.OwningField != field || !schema.Is(child.ClassId, "LexSense"))
                    return entrySenses[entry.Id] = [];
                senses.Add(child);
                pending.Push(child);
            }
        }
        return entrySenses[entry.Id] = senses.ToImmutable();
    }

    private GenericRecord? EntryOf(GenericRecord record)
    {
        if (record.Owner is not { } owner || !records.TryGetValue(owner, out var entry) ||
            entry.Id == Guid.Empty || !schema.Is(entry.ClassId, "LexEntry"))
            return null;

        if (!schema.Is(record.ClassId, "MoForm"))
            return record.OwningField == Field("LexEntry", "MorphoSyntaxAnalyses", 25, "MoMorphSynAnalysis") ? entry : null;
        return record.OwningField == Field("LexEntry", "LexemeForm", 23, "MoForm") ||
            record.OwningField == Field("LexEntry", "AlternateForms", 27, "MoForm") ? entry : null;
    }

    private bool MsaFeatures(GenericRecord msa, ImmutableArray<ReadingFeatureValue>.Builder features, out ReadingDerivation? derivation)
    {
        derivation = null;
        var owner = schema.ClassName(msa.ClassId);
        switch (owner)
        {
            case "MoStemMsa":
                return AddFeatures(msa, owner, "MsFeatures", ReadingFeatureRole.Stem, features);
            case "MoInflAffMsa":
                return AddFeatures(msa, owner, "InflFeats", ReadingFeatureRole.Inflection, features);
            case "MoDerivStepMsa":
                return AddFeatures(msa, owner, "MsFeatures", ReadingFeatureRole.DerivationalStep, features) &&
                    AddFeatures(msa, owner, "InflFeats", ReadingFeatureRole.Inflection, features);
            case "MoDerivAffMsa":
                if (!TryLink(msa, owner, "FromPartOfSpeech", 24, "PartOfSpeech", out var from) ||
                    !TryLink(msa, owner, "ToPartOfSpeech", 24, "PartOfSpeech", out var to)) return false;
                derivation = new(Id(from?.Id), Id(to?.Id));
                return AddFeatures(msa, owner, "FromMsFeatures", ReadingFeatureRole.DerivationInput, features) &&
                    AddFeatures(msa, owner, "ToMsFeatures", ReadingFeatureRole.DerivationOutput, features);
            case "MoUnclassifiedAffixMsa":
                return true;
            default:
                return false;
        }
    }

    private bool AddFeatures(GenericRecord msa, string owner, string name, ReadingFeatureRole role,
        ImmutableArray<ReadingFeatureValue>.Builder features) =>
        TryLink(msa, owner, name, 23, "FsFeatStruc", out var structure) &&
        (structure is null || AddStructure(structure, role, [], features));

    private bool AddStructure(GenericRecord structure, ReadingFeatureRole role, ImmutableArray<string> path,
        ImmutableArray<ReadingFeatureValue>.Builder features)
    {
        var pending = new Stack<(GenericRecord Structure, ImmutableArray<string> Path)>();
        var visited = new HashSet<Guid>();
        pending.Push((structure, path));
        var specs = Field("FsFeatStruc", "FeatureSpecs", 25, "FsFeatureSpecification");
        var disjunctions = Field("FsFeatStruc", "FeatureDisjunctions", 25, "FsFeatStrucDisj");
        if (specs is null || disjunctions is null) return false;

        while (pending.TryPop(out var item))
        {
            if (!visited.Add(item.Structure.Id) || Members(item.Structure, disjunctions.Value).Any()) return false;
            foreach (var id in Members(item.Structure, specs.Value))
            {
                if (!records.TryGetValue(id, out var spec) || spec.Owner != item.Structure.Id || spec.OwningField != specs ||
                    !TryLink(spec, "FsFeatureSpecification", "Feature", 24, "FsFeatDefn", out var feature) || feature is null)
                    return false;
                var next = item.Path.Add(Id(feature.Id)!);
                if (schema.Is(spec.ClassId, "FsComplexValue"))
                {
                    if (!schema.Is(feature.ClassId, "FsComplexFeature") ||
                        !TryLink(spec, "FsComplexValue", "Value", 23, "FsAbstractStructure", out var inner) ||
                        inner is null || !schema.Is(inner.ClassId, "FsFeatStruc")) return false;
                    pending.Push((inner, next));
                }
                else if (schema.Is(spec.ClassId, "FsClosedValue"))
                {
                    if (!schema.Is(feature.ClassId, "FsClosedFeature") ||
                        !TryLink(spec, "FsClosedValue", "Value", 24, "FsSymFeatVal", out var value) ||
                        value is null || value.Owner != feature.Id ||
                        value.OwningField != Field("FsClosedFeature", "Values", 25, "FsSymFeatVal"))
                        return false;
                    features.Add(new(role, next, [Id(value.Id)!]));
                }
                else return false;
            }
        }
        return true;
    }

    private bool TryLink(GenericRecord source, string owner, string name, int type, string destination, out GenericRecord? target)
    {
        target = null;
        if (Field(owner, name, type, destination) is not { } field) return false;
        if (!source.Values.TryGetValue(Key(field), out var value) || value.ValueKind == JsonValueKind.Null) return true;
        return value.ValueKind == JsonValueKind.String && value.TryGetGuid(out var id) && id != Guid.Empty &&
            records.TryGetValue(id, out target) && schema.Is(target.ClassId, destination) &&
            (type != 23 || target.Owner == source.Id && target.OwningField == field);
    }

    private int? Field(string owner, string name, int type, string? destination = null) =>
        schema.FieldId(owner, name, type, destination);

    private static IEnumerable<Guid> Members(GenericRecord record, int field) =>
        record.Values.TryGetValue(Key(field), out var value) ? value.EnumerateArray().Select(item => item.GetGuid()) : [];

    private static Guid? Link(GenericRecord record, int field) =>
        record.Values.TryGetValue(Key(field), out var value) && value.ValueKind == JsonValueKind.String && value.TryGetGuid(out var id)
            ? id : null;

    private static string Key(int field) => field.ToString(CultureInfo.InvariantCulture);
    private static string? Id(Guid? guid) => guid is { } value ? CanonicalId.FromGuid(value).Value : null;
}
