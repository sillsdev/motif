using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>
/// Whether the two further affix-process output kinds, modify-from-input (FieldWorks MoModifyFromInput) and
/// insert-by-class (MoInsertNC), authored through the composer and applied as a Proposal, are the processes the
/// real parser runs. The sounds are synthetic: p and t are voiceless, b and d voiced, p and b labial, t and d coronal.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserAffixProcessOutputKindTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    [RealParserFact]
    public async Task ModifyFromInputVoicesTheStemInitialConsonantAndParsesTheDerivedForms()
    {
        using var cache = pristine.NewScratch();
        var world = Build(cache, "pata", "tapa");
        var names = new Dictionary<CanonicalId, string>(world.Names, CanonicalId.ByGuid)
        {
            [Process(cache, world.Category, [new("c", NaturalClass: world.Voiceless), new("rest", Remaining: true)],
                [new(Modify: "c", ModifyFeatures: world.Voiced), new(Copy: "rest")])] = "VOICE",
        };
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);

        Assert.Equal(["VOICE+pata"], await Readings(tracer, path, "bata", names));
        Assert.Equal(["VOICE+tapa"], await Readings(tracer, path, "dapa", names));
        Assert.Equal(["pata"], await Readings(tracer, path, "pata", names));
        foreach (var control in new[] { "bapa", "data", "pbata" })
            Assert.Empty(await Readings(tracer, path, control, names));
    }

    [RealParserFact]
    public async Task InsertByClassInsertsASegmentOfTheClassAndParsesTheDerivedForms()
    {
        using var cache = pristine.NewScratch();
        var world = Build(cache, "pata", "tapa");
        var names = new Dictionary<CanonicalId, string>(world.Names, CanonicalId.ByGuid)
        {
            [Process(cache, world.Category, [new("rest", Remaining: true)],
                [new(InsertClass: world.VoicedLabial), new(Copy: "rest")])] = "B",
            [Process(cache, world.Category, [new("rest", Remaining: true)],
                [new(Copy: "rest"), new(InsertClass: world.BSegments)])] = "SB",
            [Process(cache, world.Category, [new("rest", Remaining: true)],
                [new(Copy: "rest"), new(InsertClass: world.BdSegments)])] = "SBD",
        };
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);

        Assert.Equal(["B+pata"], await Readings(tracer, path, "bpata", names));
        Assert.Equal(["B+tapa"], await Readings(tracer, path, "btapa", names));
        // A class of several segments leaves the choice to the parser: either member is read as the inserted segment.
        Assert.Equal(["pata+SB", "pata+SBD"], await Readings(tracer, path, "patab", names));
        Assert.Equal(["pata+SBD"], await Readings(tracer, path, "patad", names));
        Assert.Equal(["pata"], await Readings(tracer, path, "pata", names));
        foreach (var control in new[] { "dpata", "ppata" })
            Assert.Empty(await Readings(tracer, path, control, names));
    }

    private async Task<string[]> Readings(PanGlossTracer tracer, string path, string word,
        IReadOnlyDictionary<CanonicalId, string> names)
    {
        var outcome = Assert.IsType<PanGlossTraceOutcome.Completed>(
            await tracer.TraceAsync(path, word, CancellationToken.None));
        var details = outcome.Details!;
        Assert.True(details.SearchCompleted, word);
        Assert.False(details.Capped || details.TimedOut || details.InvalidShape, word);
        var readings = outcome.Document!.Analyses.Select(analysis => string.Join("+", analysis.Morphs.Select(morph =>
            names.TryGetValue(CanonicalId.FromGuid(Guid.Parse(morph.FormId!)), out var name) ? name : "?" + morph.Form)))
            .Distinct().ToArray();
        output.WriteLine($"{word}: {(readings.Length == 0 ? "no analysis" : string.Join(" | ", readings))}");
        return readings;
    }

    private static World Build(LcmCache cache, params string[] stems)
    {
        RealParserProject.PrepareForParsing(cache);
        var features = AddFeatures(cache);
        var phonemes = new Dictionary<string, CanonicalId>();
        foreach (var (letter, values) in new[]
                 {
                     ("p", new[] { features.VoiceMinus, features.Labial }), ("b", new[] { features.VoicePlus, features.Labial }),
                     ("t", new[] { features.VoiceMinus, features.Coronal }), ("d", new[] { features.VoicePlus, features.Coronal }),
                     ("a", new[] { features.VoicePlus, features.Vowel }),
                 })
            phonemes.Add(letter, Created(ApplyOps(cache, AuthorPhonemeComposer.Build(cache, new(letter, [letter], values))),
                PhPhonemeSetPhonemesOperationKinds.Create));
        CanonicalId Class(string name, string abbreviation, IReadOnlyList<PhonologicalFeatureValue>? values = null,
            IReadOnlyList<CanonicalId>? members = null) =>
            Created(ApplyOps(cache, AuthorNaturalClassComposer.Build(cache,
                new AuthorNaturalClassIntent(name, abbreviation, members, values))), PhPhonDataNaturalClassesOperationKinds.Create);
        var category = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Synthetic word", Anal(cache), "W"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        foreach (var stem in stems)
            names[Created(ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent(stem, Vern(cache),
                CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem), stem, Anal(cache),
                new LexicalEntryMsa(LexicalEntryMsaKind.Stem, category)))), LexEntryLexemeFormOperationKinds.CreateLexemeForm)] = stem;
        return new World(category, names, Class("Voiceless", "VL", [features.VoiceMinus]),
            Class("Voiced", "VD", [features.VoicePlus]), Class("Voiced labial", "VLB", [features.VoicePlus, features.Labial]),
            Class("B only", "BO", members: [phonemes["b"]]), Class("B or d", "BD", members: [phonemes["b"], phonemes["d"]]));
    }

    private static CanonicalId Process(LcmCache cache, CanonicalId category, IReadOnlyList<AffixProcessInputPart> input,
        IReadOnlyList<AffixProcessOutputStep> steps) =>
        Created(ApplyOps(cache, AuthorAffixProcessComposer.Build(cache, new AuthorAffixProcessIntent(
            CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix), "process", Anal(cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Unclassified, category), input, steps))),
            LexEntryLexemeFormOperationKinds.CreateLexemeForm);

    private static Features AddFeatures(LcmCache cache)
    {
        IFsClosedFeature voice = null!, place = null!;
        IFsSymFeatVal plus = null!, minus = null!, labial = null!, coronal = null!, vowel = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var closed = cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>();
            var symbolic = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>();
            voice = closed.Create();
            place = closed.Create();
            cache.LangProject.PhFeatureSystemOA.FeaturesOC.Add(voice);
            cache.LangProject.PhFeatureSystemOA.FeaturesOC.Add(place);
            voice.Name.set_String(cache.DefaultAnalWs, "voice");
            place.Name.set_String(cache.DefaultAnalWs, "place");
            IFsSymFeatVal Value(IFsClosedFeature feature, string name)
            {
                var value = symbolic.Create();
                feature.ValuesOC.Add(value);
                value.Name.set_String(cache.DefaultAnalWs, name);
                return value;
            }
            plus = Value(voice, "plus");
            minus = Value(voice, "minus");
            labial = Value(place, "labial");
            coronal = Value(place, "coronal");
            vowel = Value(place, "vowel");
        });
        PhonologicalFeatureValue Pair(IFsClosedFeature feature, IFsSymFeatVal value) =>
            new(CanonicalId.FromGuid(feature.Guid), CanonicalId.FromGuid(value.Guid));
        return new(Pair(voice, plus), Pair(voice, minus), Pair(place, labial), Pair(place, coronal), Pair(place, vowel));
    }

    private sealed record Features(PhonologicalFeatureValue VoicePlus, PhonologicalFeatureValue VoiceMinus,
        PhonologicalFeatureValue Labial, PhonologicalFeatureValue Coronal, PhonologicalFeatureValue Vowel);

    private sealed record World(CanonicalId Category, Dictionary<CanonicalId, string> Names, CanonicalId Voiceless,
        CanonicalId Voiced, CanonicalId VoicedLabial, CanonicalId BSegments, CanonicalId BdSegments);
}
