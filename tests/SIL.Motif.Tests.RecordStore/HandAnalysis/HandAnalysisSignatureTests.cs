using System.Collections.Immutable;
using System.Text.Json;
using SIL.LCModel;
using CanonicalJson = SIL.Motif.Contract.Canonicalization.CanonicalJson;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Ids;
using SIL.Motif.RecordStore;
using SIL.Motif.RecordStore.HandAnalysis;
using SIL.Motif.RecordStore.Lcm;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.RecordStore.HandAnalyses;

[Collection(LcmCacheTestCollection.Name)]
public sealed class HandAnalysisSignatureTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("motif-hand-signature-").FullName;

    [Theory]
    [InlineData(false, false, "ambiguous")]
    [InlineData(true, false, "explicit")]
    [InlineData(true, true, "explicit")]
    public async Task NativeBundlesMatchTheSharedReadingVector(bool explicitSense, bool unsupported, string vectorName)
    {
        using var cache = NewLangProjFixture.CreateCache(root);
        var records = Records(cache.LangProject.Guid, explicitSense, unsupported).ToArray();
        await using var host = await HandAnalysisHost.OpenAsync(await Import(cache, records));
        IHandAnalysisIndex index = host;
        await using var pin = (await index.PinAsync(cache.LangProject.Guid, null, default)).Value();
        var page = await pin.Words(TestWord.Reading(GuidOf(31)), new());
        var reading = Assert.IsType<WordFact.Reading>(Assert.Single(page.Items));
        Assert.Equal(new Guid?[] { GuidOf(33), GuidOf(32) }, reading.Bundles.Select(b => b.Id));
        if (unsupported)
        {
            Assert.Null(reading.Signature);
            Assert.Contains(reading.Limitations, l => l.Code == "reading-signature-unavailable");
            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "HandAnalysis", "reading-signature-vectors.json")));
        var vector = Assert.Single(document.RootElement.EnumerateArray(), row => row.GetProperty("name").GetString() == vectorName);
        var signature = Assert.IsType<ReadingSignature>(reading.Signature);
        Assert.Equal(vector.GetProperty("id").GetString(), signature.Id);
        Assert.Equal(CanonicalJson.CanonicalizeToUtf8(vector.GetProperty("signature").GetRawText()), signature.GetCanonicalBytes());
        Assert.Equal(new ReadingSignature(ReadingSignatureFixture.ParserFacts(explicitSense)), signature);
    }

    private async Task<string> Import(LcmCache cache, GenericRecord[] records)
    {
        var schema = new LcmGraphReader(cache).Schema();
        var manifest = new GenericRecord { Id = LcmRecordBridge.ManifestId, Kind = "manifest" };
        manifest.Values["schema"] = schema;
        var types = schema.GetProperty("fields").EnumerateArray()
            .ToDictionary(f => f.GetProperty("id").GetInt32(), f => f.GetProperty("type").GetInt32());
        var layouts = records.GroupBy(r => r.ClassId).ToImmutableDictionary(g => g.Key,
            g => g.First().Values.Keys.Select(int.Parse).ToImmutableDictionary(f => f, f => types[f]));
        var coverage = NativeImportCoverage.Capture(records.Select(r => new ReferenceRecord(r.Id, r.ClassId, r.Owner, r.OwningField)),
            layouts, records[0].Provenance!.TraversalVersion, schema);
        var path = Path.Combine(root, "signature-" + Guid.NewGuid().ToString("N") + ".sqlite");
        await using var store = new SIL.Motif.RecordStore.RecordStore(path);
        await store.ImportAsync(records.Append(manifest).Select(r => new RecordImport(r, types)), Guid.NewGuid(), coverage);
        return path;
    }

    /// <summary>A present native integer of the wrong kind makes the index unavailable; it is never read as an empty or zero value.</summary>
    [Theory]
    [InlineData(1, LexEntryTags.kflidHomographNumber, "\"x\"")]
    [InlineData(1, LexEntryTags.kflidHomographNumber, "null")]
    [InlineData(1, LexEntryTags.kflidHomographNumber, "true")]
    [InlineData(22, LexEntryRefTags.kflidRefType, "\"1\"")]
    [InlineData(22, LexEntryRefTags.kflidRefType, "null")]
    public async Task ACorruptNativeValueRefusesTheIndexInsteadOfAnsweringPlausibly(int record, int field, string json)
    {
        using var cache = NewLangProjFixture.CreateCache(root);
        var records = Records(cache.LangProject.Guid, true, false).ToArray();
        var target = Assert.Single(records, r => r.Id == GuidOf(record));
        target.Values[field.ToString()] = JsonDocument.Parse(json).RootElement.Clone();
        await using var host = await HandAnalysisHost.OpenAsync(await Import(cache, records));
        var outcome = await ((IHandAnalysisIndex)host).PinAsync(cache.LangProject.Guid, null, default);
        Assert.Null(outcome.Value);
        Assert.Equal(RefusalCodes.HandAnalysisIndexUnavailable, outcome.Refusal?.Code);
    }

    /// <summary>Import already stops wrong-kind references, so the readers are checked directly: a wrong kind is unavailable, only a missing key is empty.</summary>
    [Fact]
    public void TheLexiconReadersRefuseWrongKindsAndTreatOnlyAMissingKeyAsEmpty()
    {
        static GenericRecord With(string json)
        {
            var record = new GenericRecord { Id = Guid.NewGuid() };
            record.Values["1"] = JsonDocument.Parse(json).RootElement.Clone();
            return record;
        }
        var good = Guid.NewGuid();
        var none = new GenericRecord { Id = Guid.NewGuid() };
        Assert.Null(LexiconProjection.Atom(none, 1));
        Assert.Null(LexiconProjection.Atom(With("null"), 1));
        Assert.Equal(good, LexiconProjection.Atom(With($"\"{good}\""), 1));
        Assert.Empty(LexiconProjection.Seq(none, 1));
        Assert.Equal([good], LexiconProjection.Seq(With($"[\"{good}\"]"), 1));
        Assert.Empty(LexiconProjection.Multi(none, 1));
        Assert.Throws<IndexUnavailableException>(() => LexiconProjection.Int(none, 1));
        foreach (var json in new[] { "\"x\"", "7", "[]", "{}", "true" })
            Assert.Throws<IndexUnavailableException>(() => LexiconProjection.Atom(With(json), 1));
        foreach (var json in new[] { "\"x\"", "7", "{}", "null", "[7]", "[null]", "[\"x\"]" })
            Assert.Throws<IndexUnavailableException>(() => LexiconProjection.Seq(With(json), 1).ToArray());
        foreach (var json in new[] { "\"x\"", "7", "[]", "null", "true" })
            Assert.Throws<IndexUnavailableException>(() => LexiconProjection.Multi(With(json), 1));
        foreach (var json in new[] { "\"x\"", "null", "true", "1.5", "[]", "{}" })
            Assert.Throws<IndexUnavailableException>(() => LexiconProjection.Int(With(json), 1));
    }

    private static IEnumerable<GenericRecord> Records(Guid project, bool explicitSense, bool unsupported)
    {
        static GenericRecord Record(int id, int cls, int parent, int owningField, string? repository = null, int ordinal = 0,
            params (int Field, object? Value)[] fields) => new()
        {
            Id = GuidOf(id), ClassId = cls, Owner = parent == 0 ? null : GuidOf(parent), OwningField = owningField,
            Values = fields.ToDictionary(f => f.Field.ToString(), f => JsonSerializer.SerializeToElement(f.Value)),
            Provenance = new(id, "signature-fixture", ImmutableDictionary<int, ImmutableArray<Guid>>.Empty)
            {
                RepositoryOrdinals = Ordinals(cls, id, repository, ordinal)
            }
        };

        static GenericRecord Entry(int id, int? morph, int? msa, int[] senses, params int[] references) =>
            Record(id, LexEntryTags.kClassId, 0, 0, fields:
                [(LexEntryTags.kflidLexemeForm, morph is { } form ? GuidOf(form) : null),
                 (LexEntryTags.kflidMorphoSyntaxAnalyses, msa is { } analysis ? Ids(analysis) : Ids()),
                 (LexEntryTags.kflidSenses, Ids(senses)), (LexEntryTags.kflidEntryRefs, Ids(references)),
                 (LexEntryTags.kflidHomographNumber, 0), (LexEntryTags.kflidCitationForm, new { })]);

        static GenericRecord Sense(int id, int entry, int msa) =>
            Record(id, LexSenseTags.kClassId, entry, LexEntryTags.kflidSenses, fields:
                [(LexSenseTags.kflidMorphoSyntaxAnalysis, GuidOf(msa)), (LexSenseTags.kflidSenses, Ids())]);

        static GenericRecord Structure(int id, int parent, int field, params int[] specs) =>
            Record(id, FsFeatStrucTags.kClassId, parent, field, fields:
                [(FsFeatStrucTags.kflidFeatureSpecs, Ids(specs)), (FsFeatStrucTags.kflidFeatureDisjunctions, Ids())]);

        static GenericRecord Complex(int id, int parent, int feature, int structure) =>
            Record(id, FsComplexValueTags.kClassId, parent, FsFeatStrucTags.kflidFeatureSpecs, fields:
                [(FsFeatureSpecificationTags.kflidFeature, GuidOf(feature)), (FsComplexValueTags.kflidValue, GuidOf(structure))]);

        static GenericRecord Closed(int id, int parent, int symbol) =>
            Record(id, FsClosedValueTags.kClassId, parent, FsFeatStrucTags.kflidFeatureSpecs, fields:
                [(FsFeatureSpecificationTags.kflidFeature, GuidOf(8)), (FsClosedValueTags.kflidValue, GuidOf(symbol))]);

        static GenericRecord Bundle(int id, int ordinal, int morph, int? sense, int msa, int? infl) =>
            Record(id, WfiMorphBundleTags.kClassId, 31, WfiAnalysisTags.kflidMorphBundles, "WfiMorphBundle", ordinal,
                (WfiMorphBundleTags.kflidMorph, GuidOf(morph)), (WfiMorphBundleTags.kflidSense, sense is { } s ? GuidOf(s) : null),
                (WfiMorphBundleTags.kflidMsa, GuidOf(msa)), (WfiMorphBundleTags.kflidInflType, infl is { } i ? GuidOf(i) : null),
                (WfiMorphBundleTags.kflidForm, new { }));

        yield return new GenericRecord { Id = project, ClassId = LangProjectTags.kClassId,
            Provenance = new(0, "signature-fixture", ImmutableDictionary<int, ImmutableArray<Guid>>.Empty) };
        yield return Entry(1, 5, 4, [2, 3]);
        yield return Sense(2, 1, 4);
        yield return Sense(3, 1, 4);
        yield return Record(4, MoStemMsaTags.kClassId, 1, LexEntryTags.kflidMorphoSyntaxAnalyses,
            fields: [(MoStemMsaTags.kflidMsFeatures, GuidOf(40))]);
        yield return Record(5, MoStemAllomorphTags.kClassId, 1, LexEntryTags.kflidLexemeForm, fields: [(MoFormTags.kflidForm, new { })]);
        yield return Record(7, FsComplexFeatureTags.kClassId, 0, 0);
        yield return Record(8, FsClosedFeatureTags.kClassId, 0, 0,
            fields: [(FsClosedFeatureTags.kflidValues, Ids(9, 10, 18, 19))]);
        yield return Record(9, FsSymFeatValTags.kClassId, 8, FsClosedFeatureTags.kflidValues);
        yield return Record(10, FsSymFeatValTags.kClassId, 8, FsClosedFeatureTags.kflidValues);
        yield return Entry(11, 14, null, [], 22);
        yield return Entry(21, null, 13, [12]);
        yield return Record(22, LexEntryRefTags.kClassId, 11, LexEntryTags.kflidEntryRefs, fields:
            [(LexEntryRefTags.kflidRefType, LexEntryRefTags.krtVariant), (LexEntryRefTags.kflidComponentLexemes, Ids(21)),
             (LexEntryRefTags.kflidVariantEntryTypes, Ids(15))]);
        yield return Sense(12, 21, 13);
        yield return Record(13, MoDerivAffMsaTags.kClassId, 21, LexEntryTags.kflidMorphoSyntaxAnalyses, fields:
            [(MoDerivAffMsaTags.kflidFromPartOfSpeech, GuidOf(16)), (MoDerivAffMsaTags.kflidToPartOfSpeech, GuidOf(17)),
             (MoDerivAffMsaTags.kflidFromMsFeatures, GuidOf(50)), (MoDerivAffMsaTags.kflidToMsFeatures, GuidOf(54))]);
        yield return Record(14, MoAffixAllomorphTags.kClassId, 11, LexEntryTags.kflidLexemeForm, fields: [(MoFormTags.kflidForm, new { })]);
        yield return Record(15, LexEntryInflTypeTags.kClassId, 0, 0);
        yield return Record(16, PartOfSpeechTags.kClassId, 0, 0);
        yield return Record(17, PartOfSpeechTags.kClassId, 0, 0);
        yield return Record(18, FsSymFeatValTags.kClassId, 8, FsClosedFeatureTags.kflidValues);
        yield return Record(19, FsSymFeatValTags.kClassId, 8, FsClosedFeatureTags.kflidValues);
        yield return Record(20, FsComplexFeatureTags.kClassId, 0, 0);
        yield return Record(30, WfiWordformTags.kClassId, 0, 0, "WfiWordform", 0,
            (WfiWordformTags.kflidAnalyses, Ids(31)), (WfiWordformTags.kflidForm, new { }), (WfiWordformTags.kflidSpellingStatus, 0));
        yield return Record(31, WfiAnalysisTags.kClassId, 30, WfiWordformTags.kflidAnalyses, "WfiAnalysis", 0,
            (WfiAnalysisTags.kflidMorphBundles, Ids(33, 32)));
        yield return Bundle(33, 1, 5, explicitSense ? 2 : null, 4, null);
        yield return Bundle(32, 0, 14, 12, 13, 15);
        yield return Structure(40, 4, MoStemMsaTags.kflidMsFeatures, 41, 45);
        yield return Complex(41, 40, 7, 42);
        yield return Structure(42, 41, FsComplexValueTags.kflidValue, 43, 44);
        yield return Closed(43, 42, 9);
        yield return Closed(44, 42, 10);
        yield return Complex(45, 40, 20, 46);
        yield return Structure(46, 45, FsComplexValueTags.kflidValue, 47);
        yield return Closed(47, 46, 9);
        yield return Structure(50, 13, MoDerivAffMsaTags.kflidFromMsFeatures, 51);
        yield return Complex(51, 50, 7, 52);
        yield return Structure(52, 51, FsComplexValueTags.kflidValue, 53);
        yield return Closed(53, 52, 18);
        yield return Structure(54, 13, MoDerivAffMsaTags.kflidToMsFeatures, 55);
        yield return Complex(55, 54, 7, 56);
        yield return Structure(56, 55, FsComplexValueTags.kflidValue, 57);
        yield return unsupported
            ? Record(57, FsOpenValueTags.kClassId, 56, FsFeatStrucTags.kflidFeatureSpecs,
                fields: [(FsFeatureSpecificationTags.kflidFeature, GuidOf(8))])
            : Closed(57, 56, 19);
    }

    private static ImmutableDictionary<string, long> Ordinals(int cls, int id, string? repository, int ordinal)
    {
        var name = repository ?? cls switch
        {
            LexEntryTags.kClassId => "LexEntry",
            LexSenseTags.kClassId => "LexSense",
            MoStemAllomorphTags.kClassId or MoAffixAllomorphTags.kClassId => "MoForm",
            _ => null
        };
        return name is null ? ImmutableDictionary<string, long>.Empty
            : ImmutableDictionary<string, long>.Empty.Add(name, repository is null ? id : ordinal);
    }

    private static Guid GuidOf(int part) => CanonicalId.Parse(ReadingSignatureFixture.Id(part)).ToGuid();
    private static Guid[] Ids(params int[] parts) => parts.Select(GuidOf).ToArray();

    public void Dispose() => Directory.Delete(root, true);
}
