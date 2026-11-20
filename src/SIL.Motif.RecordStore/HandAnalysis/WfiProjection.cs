using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.HandAnalysis;

namespace SIL.Motif.RecordStore.HandAnalysis;

/// <summary>Field and class ids of the Wfi model, resolved by owner, name, type and destination from the retained schema.</summary>
internal sealed class WfiSchema
{
    public int WordformForm, Spelling, Analyses, Category, Bundles, Meanings, Evaluations;
    public int ConcreteForm, BundleForm, Morph, Msa, Sense, InflType, GlossForm, AgentHuman, AgentApproves;
    public readonly NativeUsageSchema Usage;

    public WfiSchema(NativeUsageSchema usage)
    {
        Usage = usage;
        int Field(string owner, string name, int type, string? destination = null) =>
            usage.FieldId(owner, name, type, destination)
                ?? throw new IndexUnavailableException($"The retained schema does not define {owner}.{name} exactly once.");
        WordformForm = Field("WfiWordform", "Form", 16); Spelling = Field("WfiWordform", "SpellingStatus", 2);
        Analyses = Field("WfiWordform", "Analyses", 25, "WfiAnalysis");
        Category = Field("WfiAnalysis", "Category", 24, "PartOfSpeech"); Bundles = usage.BundlesField;
        Meanings = Field("WfiAnalysis", "Meanings", 25, "WfiGloss");
        Evaluations = Field("WfiAnalysis", "Evaluations", 26, "CmAgentEvaluation");
        ConcreteForm = Field("MoForm", "Form", 16);
        BundleForm = Field("WfiMorphBundle", "Form", 14); Morph = Field("WfiMorphBundle", "Morph", 24, "MoForm");
        Msa = Field("WfiMorphBundle", "Msa", 24, "MoMorphSynAnalysis"); Sense = Field("WfiMorphBundle", "Sense", 24, "LexSense");
        InflType = Field("WfiMorphBundle", "InflType", 24, "LexEntryInflType");
        GlossForm = Field("WfiGloss", "Form", 16);
        AgentHuman = Field("CmAgent", "Human", 1); AgentApproves = Field("CmAgent", "Approves", 23, "CmAgentEvaluation");
        if (usage.ClassId("CmAgent") is null) throw new IndexUnavailableException("The retained schema has no agent class.");
    }
}

internal sealed record EncounterKey(long Ordinal, long RepositoryOrdinal);

internal sealed record WordHeader(Guid Id, ImmutableDictionary<string, string> Forms, int SpellingStatus,
    ImmutableArray<Guid> Analyses, EncounterKey Encounter);

internal sealed record NativeReading(long Ordinal, WordFact.Reading Fact);

/// <summary>Immutable Wfi facts of one verified point; holds no records, database or lazy native objects.</summary>
internal sealed class WfiProjection
{
    public const string Repository = "WfiWordform";

    public ImmutableArray<WordHeader> Headers { get; private init; }
    public ImmutableDictionary<Guid, WordHeader> HeaderById { get; private init; } = ImmutableDictionary<Guid, WordHeader>.Empty;
    public ImmutableDictionary<(string Ws, string Nfd), ImmutableArray<Guid>> ByKey { get; private init; } =
        ImmutableDictionary<(string Ws, string Nfd), ImmutableArray<Guid>>.Empty;
    public ImmutableDictionary<Guid, NativeReading> Readings { get; private init; } = ImmutableDictionary<Guid, NativeReading>.Empty;
    public ImmutableDictionary<Guid, Guid> GlossOwner { get; private init; } = ImmutableDictionary<Guid, Guid>.Empty;
    public ImmutableDictionary<Guid, Guid> BundleOwner { get; private init; } = ImmutableDictionary<Guid, Guid>.Empty;
    public string FactsDigest { get; private init; } = "";

    /// <summary>The same facts with each reading's native use count and default-rank key bound to the point.</summary>
    public WfiProjection WithUseCounts(IReadOnlyDictionary<Guid, long> counts) => new()
    {
        Headers = Headers, HeaderById = HeaderById, ByKey = ByKey, GlossOwner = GlossOwner, BundleOwner = BundleOwner,
        FactsDigest = FactsDigest,
        Readings = Readings.ToImmutableDictionary(p => p.Key, p =>
        {
            var count = counts.GetValueOrDefault(p.Key) + p.Value.Fact.Glosses.Sum(g => counts.GetValueOrDefault(g.Id));
            return p.Value with { Fact = p.Value.Fact with { OccurrenceUseCount = count, DefaultRankKey = RankKey(count, p.Value) } };
        })
    };

    /// <summary>Orders by use count descending, then native encounter, as one string that compares ordinally.</summary>
    internal static string RankKey(long count, NativeReading reading) =>
        string.Create(CultureInfo.InvariantCulture, $"{long.MaxValue - count:D19}:{reading.Fact.EncounterKey}");

    public static string Nfd(string text) => text.Normalize(NormalizationForm.FormD);

    /// <summary>Builds from a whole record list.</summary>
    public static WfiProjection FromRecords(IReadOnlyList<GenericRecord> all)
    {
        var usage = NativeUsageSchema.Read(all) ?? throw new IndexUnavailableException(
            "Native schema, traversal version or class membership disagrees with the native coverage record.");
        return Build(all.Where(r => r.Kind == "lcm").ToDictionary(r => r.Id), usage);
    }

    public static WfiProjection Build(IReadOnlyDictionary<Guid, GenericRecord> native, NativeUsageSchema? usage, CancellationToken ct = default)
    {
        try { return BuildProjection(native, usage, ct); }
        catch (Exception exception) when (exception is not IndexUnavailableException && exception is InvalidDataException
            or KeyNotFoundException or ArgumentException or FormatException or JsonException or InvalidOperationException)
        {
            throw new IndexUnavailableException("A native Wfi fact cannot be read: " + exception.Message, exception);
        }
    }

    private static WfiProjection BuildProjection(IReadOnlyDictionary<Guid, GenericRecord> native, NativeUsageSchema? usage, CancellationToken ct)
    {
        if (usage is null) throw new IndexUnavailableException("The native schema proof is unavailable.");
        var schema = new WfiSchema(usage);
        var kinds = new Dictionary<int, string?>();
        string? Kind(int classId)
        {
            if (kinds.TryGetValue(classId, out var known)) return known;
            return kinds[classId] = new[] { "WfiWordform", "WfiAnalysis", "WfiMorphBundle", "WfiGloss", "CmAgentEvaluation" }
                .FirstOrDefault(name => usage.Is(classId, name));
        }
        var wordforms = new List<GenericRecord>();
        var ordinals = new HashSet<(string Repository, long Ordinal)>();
        foreach (var record in native.Values)
        {
            ct.ThrowIfCancellationRequested();
            var kind = Kind(record.ClassId);
            if (record.Provenance is not { } provenance) throw new IndexUnavailableException("A native record has no provenance.");
            if (kind == "CmAgentEvaluation" != provenance.RepositoryOrdinals.ContainsKey("CmAgentEvaluation"))
                throw new IndexUnavailableException("Class membership disagrees with captured repository membership.");
            if (kind is not null && (!provenance.RepositoryOrdinals.TryGetValue(kind, out var ordinal) ||
                ordinal < 0 || !ordinals.Add((kind, ordinal))))
                throw new IndexUnavailableException("A Wfi repository ordinal is missing, negative or duplicated.");
            if (kind == "WfiWordform") wordforms.Add(record);
        }
        wordforms.Sort((a, b) => Ordinal(a, Repository).CompareTo(Ordinal(b, Repository)));
        var headers = ImmutableArray.CreateBuilder<WordHeader>(wordforms.Count);
        var byKey = new Dictionary<(string Ws, string Nfd), List<Guid>>();
        var readings = new Dictionary<Guid, NativeReading>();
        var glossOwner = new Dictionary<Guid, Guid>();
        var bundleOwner = new Dictionary<Guid, Guid>();
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var signatures = new NativeReadingSignatures(native, usage);
        foreach (var wordform in wordforms)
        {
            ct.ThrowIfCancellationRequested();
            var forms = Texts(wordform.Values[Key(schema.WordformForm)]);
            var analyses = Members(wordform, schema.Analyses).OrderBy(id => Ordinal(Get(native, id), "WfiAnalysis")).ToImmutableArray();
            var header = new WordHeader(wordform.Id, forms, Int(wordform, schema.Spelling), analyses, Encounter(wordform, Repository));
            headers.Add(header);
            foreach (var (ws, text) in forms)
            {
                var key = (ws, Nfd(text));
                if (!byKey.TryGetValue(key, out var ids)) byKey[key] = ids = [];
                ids.Add(wordform.Id);
            }
            foreach (var id in analyses)
            {
                ct.ThrowIfCancellationRequested();
                var reading = Reading(native, schema, signatures, Get(native, id), wordform.Id);
                readings.Add(id, reading);
                foreach (var gloss in reading.Fact.Glosses) glossOwner.Add(gloss.Id, id);
                foreach (var bundle in reading.Fact.Bundles) bundleOwner.Add(bundle.Id!.Value, id);
            }
        }
        foreach (var header in headers)
        {
            ct.ThrowIfCancellationRequested();
            digest.AppendData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                header.Id, header.Encounter, header.SpellingStatus, Forms = FormBytes(header.Forms),
                Analyses = header.Analyses.Select(id => readings[id].Fact).Select(r => new
                {
                    r.Analysis, r.CategoryId, r.EncounterKey, r.Evaluations, Signature = r.Signature?.Id,
                    Bundles = r.Bundles.Select(b => new { b.Id, b.MemberId, b.MorphId, b.SenseId, b.MsaId, b.InflTypeId,
                        Forms = FormBytes(b.Forms), ConcreteForms = FormBytes(b.ConcreteForms) }),
                    Glosses = r.Glosses.Select(g => new { g.Id, Forms = FormBytes(g.Forms) })
                })
            }));
        }
        var ordered = headers.MoveToImmutable();
        return new()
        {
            Headers = ordered, HeaderById = ordered.ToImmutableDictionary(h => h.Id),
            ByKey = byKey.ToImmutableDictionary(p => p.Key, p => p.Value.ToImmutableArray()), Readings = readings.ToImmutableDictionary(),
            GlossOwner = glossOwner.ToImmutableDictionary(), BundleOwner = bundleOwner.ToImmutableDictionary(),
            FactsDigest = Convert.ToHexStringLower(digest.GetHashAndReset())
        };
    }

    private static NativeReading Reading(IReadOnlyDictionary<Guid, GenericRecord> native, WfiSchema schema,
        NativeReadingSignatures signatures, GenericRecord analysis, Guid wordform)
    {
        var limits = ImmutableArray.CreateBuilder<ReadLimitation>();
        var members = Members(analysis, schema.Bundles);
        var list = analysis.Lists.GetValueOrDefault(schema.Bundles);
        var identities = list is { Ops.Count: > 0 }
            ? list.State().Elements() : members.Select(id => ListHistory.ElementOf(id, 0));
        var bundles = members.Zip(identities, (id, member) =>
        {
            if (ListHistory.ItemOf(member) != id)
                throw new IndexUnavailableException("A bundle list member does not identify the retained bundle.");
            var b = Get(native, id);
            var morph = Ref(native, schema, b, schema.Morph);
            var forms = Texts(b.Values[Key(schema.BundleForm)]);
            ImmutableDictionary<string, string> concrete = forms;
            if (morph is { } morphId && Get(native, morphId).Values.TryGetValue(Key(schema.ConcreteForm), out var value))
                concrete = Texts(value).SetItems(forms);
            else if (forms.IsEmpty)
                limits.Add(new("concrete-form-unavailable", [id], "The bundle has no available concrete form."));

            return new BundleFact(id, member, morph, Ref(native, schema, b, schema.Sense),
                Ref(native, schema, b, schema.Msa), Ref(native, schema, b, schema.InflType), forms, concrete);
        }).ToImmutableArray();
        if (bundles.Length != members.Count() || bundles.Length != identities.Count())
            throw new IndexUnavailableException("Bundle list identity does not cover the retained bundle vector.");

        var glosses = Members(analysis, schema.Meanings).Select(id => Get(native, id))
            .OrderBy(g => Ordinal(g, "WfiGloss")).Select(g => new GlossFact(g.Id, Texts(g.Values[Key(schema.GlossForm)])))
            .ToImmutableArray();
        var evaluations = Members(analysis, schema.Evaluations).Select(id => Get(native, id))
            .OrderBy(e => Ordinal(e, "CmAgentEvaluation")).Select(e =>
            {
                var agent = e.Owner is { } owner && native.TryGetValue(owner, out var a) && schema.Usage.Is(a.ClassId, "CmAgent") ? a : null;
                bool? human = agent is not null && agent.Values.TryGetValue(Key(schema.AgentHuman), out var h)
                    ? h.GetBoolean() : null;
                if (human is null)
                    limits.Add(new("evaluation-agent-unavailable", [e.Id], "The native evaluation agent could not be resolved."));
                return new EvaluationFact(e.Id, agent?.Id, human, e.OwningField == schema.AgentApproves);
            }).ToImmutableArray();
        var encounter = Encounter(analysis, "WfiAnalysis");
        var signature = signatures.Build(bundles);
        if (signature is null)
            limits.Add(new("reading-signature-unavailable", [analysis.Id], "The native morpheme path does not prove an exact Reading identity."));
        return new(encounter.RepositoryOrdinal, new(new AnalysisRef.Native(analysis.Id), wordform,
            Ref(native, schema, analysis, schema.Category), null, bundles, glosses, evaluations, null, null,
            string.Create(CultureInfo.InvariantCulture, $"{encounter.RepositoryOrdinal:D19}:{encounter.Ordinal:D19}"),
            null, null, signature, limits.ToImmutable()));
    }

    internal static GenericRecord Get(IReadOnlyDictionary<Guid, GenericRecord> native, Guid id) =>
        native.TryGetValue(id, out var record) ? record : throw new IndexUnavailableException("A Wfi member record is missing.");

    internal static string Key(int field) => field.ToString(CultureInfo.InvariantCulture);

    internal static long Ordinal(GenericRecord record, string repository) =>
        record.Provenance!.RepositoryOrdinals.TryGetValue(repository, out var ordinal) ? ordinal
            : throw new IndexUnavailableException("A native record lacks its repository ordinal.");

    internal static EncounterKey Encounter(GenericRecord record, string repository) =>
        new(record.Provenance!.EncounterOrdinal, Ordinal(record, repository));

    private static Guid? Ref(IReadOnlyDictionary<Guid, GenericRecord> native, WfiSchema schema, GenericRecord record, int field)
    {
        if (!record.Values.TryGetValue(Key(field), out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String || !value.TryGetGuid(out var id) ||
            !native.TryGetValue(id, out var target) || !schema.Usage.AcceptsReference(field, target.ClassId))
            throw new IndexUnavailableException("A Wfi reference has an invalid or unavailable destination.");
        return id;
    }

    internal static int Int(GenericRecord record, int field) => record.Values[Key(field)].GetInt32();

    /// <summary>Native member order: the captured vector when present, else the stored list.</summary>
    internal static IEnumerable<Guid> Members(GenericRecord record, int field) =>
        record.Provenance!.Vectors.TryGetValue(field, out var native) ? native
            : record.Values.TryGetValue(Key(field), out var v) ? v.EnumerateArray().Select(x => x.GetGuid()) : [];

    internal static ImmutableDictionary<string, string> Texts(JsonElement multi) =>
        multi.EnumerateObject().ToImmutableDictionary(p => p.Name, p => Text(p.Value), StringComparer.Ordinal);

    private static IEnumerable<KeyValuePair<string, string>> FormBytes(ImmutableDictionary<string, string> forms) =>
        forms.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, Convert.ToBase64String(MemoryMarshal.AsBytes(p.Value.AsSpan()))));

    internal static string Text(JsonElement text)
    {
        if (text.ValueKind == JsonValueKind.Null) return "";
        if (text.ValueKind != JsonValueKind.Object || !text.TryGetProperty("text", out var raw) ||
            !NativeTextReferences.TryRaw(raw, out var restored))
            throw new InvalidDataException("Malformed native UTF-16 text.");
        return restored ?? "";
    }
}
