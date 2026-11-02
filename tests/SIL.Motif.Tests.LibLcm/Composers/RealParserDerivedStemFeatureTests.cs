using SIL.LCModel;
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
/// A stem's MsFeatures value survives derivation unless the derivational affix names a different value of the same
/// feature in its ToMsFeatures: the pinned parser overlays ToMsFeatures on the stem's features with priority.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserDerivedStemFeatureTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    private static readonly string[] Letters = ["p", "a", "g", "k", "u", "n", "l", "i", "t"];

    [RealParserFact]
    public async Task ADerivationalAffixReplacesTheStemsValueOfAFeatureSoARequirementOfTheOtherValueIsMet()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, Letters);
        var ws = Anal(cache);
        var verb = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Verb", ws, "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var slot = Created(ApplyOps(cache, AuthorAffixSlotComposer.Build(cache,
            new AuthorAffixSlotIntent(verb, "Outer", ws, true))), PartOfSpeechAffixSlotsOperationKinds.Create);
        ApplyOps(cache, AuthorAffixTemplateComposer.Build(cache,
            new AuthorAffixTemplateIntent(verb, "Verb template", ws, [slot], [], true)));
        var featureProposal = ApplyOps(cache, AuthorInflectionFeatureComposer.Build(cache,
            new("transitivity", ws, [new("Intransitive", "intr"), new("Transitive", "tr")], "tv")));
        var feature = Created(featureProposal, FsFeatureSystemFeaturesOperationKinds.Create);
        var values = featureProposal.Operations.Where(op => op.Kind == FsClosedFeatureValuesOperationKinds.Create)
            .Select(op => op.EntityId!.Value).ToArray();
        CanonicalId Value(string text) => values.Single(id =>
            ((IFsSymFeatVal)cache.ServiceLocator.ObjectRepository.GetObject(id.ToGuid()))
                .Name.get_String(cache.DefaultAnalWs).Text == text);

        // A second feature the derivation never names: the stem is first person and the derived stem must stay so.
        var personProposal = ApplyOps(cache, AuthorInflectionFeatureComposer.Build(cache,
            new("person", ws, [new("First", "1"), new("Second", "2")], "ps")));
        var person = Created(personProposal, FsFeatureSystemFeaturesOperationKinds.Create);
        var personValues = personProposal.Operations.Where(op => op.Kind == FsClosedFeatureValuesOperationKinds.Create)
            .Select(op => op.EntityId!.Value).ToArray();
        CanonicalId Person(string text) => personValues.Single(id =>
            ((IFsSymFeatVal)cache.ServiceLocator.ObjectRepository.GetObject(id.ToGuid()))
                .Name.get_String(cache.DefaultAnalWs).Text == text);

        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        var stem = AddStem(cache, "kaun", verb);
        names[stem.Form] = "kaun";
        var stemFeatures = Created(ApplyOps(cache, AuthorFeatureStructureComposer.Build(cache, new(stem.Msa))),
            MoStemMsaMsFeaturesOperationKinds.CreateMsFeatures);
        ApplyOps(cache, AuthorFeatureValueComposer.Build(cache, new(stemFeatures, feature, Value("Intransitive"))));
        ApplyOps(cache, AuthorFeatureValueComposer.Build(cache, new(stemFeatures, person, Person("First"))));

        foreach (var (form, personValue) in new[] { ("ku", "First"), ("li", "Second") })
        {
            var requirer = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent(form,
                Vern(cache), MorphType(MoMorphTypeTags.kguidMorphPrefix), form, ws,
                new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, verb, Slots: [slot]))));
            names[Created(requirer, LexEntryLexemeFormOperationKinds.CreateLexemeForm)] = form;
            var requirement = Created(ApplyOps(cache, AuthorFeatureStructureComposer.Build(cache,
                new(Created(requirer, LexEntryMorphoSyntaxAnalysesOperationKinds.Create)))),
                MoInflAffMsaInflFeatsOperationKinds.CreateInflFeats);
            ApplyOps(cache, AuthorFeatureValueComposer.Build(cache, new(requirement, feature, Value("Transitive"))));
            ApplyOps(cache, AuthorFeatureValueComposer.Build(cache, new(requirement, person, Person(personValue))));
        }

        var derived = ApplyOps(cache, AuthorAffixProcessComposer.Build(cache, new AuthorAffixProcessIntent(
            MorphType(MoMorphTypeTags.kguidMorphPrefix), "causative", ws,
            new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, verb, verb),
            [new("rest", Remaining: true)],
            [new(Insert: [Phoneme(cache, "p"), Phoneme(cache, "a"), Phoneme(cache, "g")]), new(Copy: "rest")])));
        var derivedMsa = Created(derived, LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
        names[Created(derived, LexEntryLexemeFormOperationKinds.CreateLexemeForm)] = "pag";
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);

        // The stem is intransitive and the derived stem still is: the requirement fails either way.
        Assert.Empty(await Readings(tracer, path, "kukaun", names));
        Assert.Equal(["pag+kaun"], await Readings(tracer, path, "pagkaun", names));
        Assert.Empty(await Readings(tracer, path, "kupagkaun", names));

        var side = Created(ApplyOps(cache, AuthorFeatureStructureComposer.Build(cache, new(derivedMsa, "to"))),
            MoDerivAffMsaMsFeaturesOperationKinds.CreateToMsFeatures);
        ApplyOps(cache, AuthorFeatureValueComposer.Build(cache, new(side, feature, Value("Transitive"))));
        Assert.Equal(side.ToGuid(), Get<IMoDerivAffMsa>(cache, derivedMsa).ToMsFeaturesOA!.Guid);
        new FwDataProjectLoader().Save(cache);

        // The derivation now makes the stem transitive; the bare stem is still intransitive.
        Assert.Equal(["ku+pag+kaun"], await Readings(tracer, path, "kupagkaun", names));
        Assert.Empty(await Readings(tracer, path, "kukaun", names));
        Assert.Equal(["pag+kaun"], await Readings(tracer, path, "pagkaun", names));
        // The derivation names transitivity only, so the stem's person is kept: li (needs second person) still fails.
        Assert.Empty(await Readings(tracer, path, "lipagkaun", names));
    }

    [RealParserFact]
    public async Task ADerivationalAffixCanRequireAFeatureValueOfTheStemItAttachesTo()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, Letters);
        var ws = Anal(cache);
        var verb = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Verb", ws, "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var slot = Created(ApplyOps(cache, AuthorAffixSlotComposer.Build(cache,
            new AuthorAffixSlotIntent(verb, "Outer", ws, true))), PartOfSpeechAffixSlotsOperationKinds.Create);
        ApplyOps(cache, AuthorAffixTemplateComposer.Build(cache,
            new AuthorAffixTemplateIntent(verb, "Verb template", ws, [slot], [], true)));
        var featureProposal = ApplyOps(cache, AuthorInflectionFeatureComposer.Build(cache,
            new("transitivity", ws, [new("Intransitive", "intr"), new("Transitive", "tr")], "tv")));
        var feature = Created(featureProposal, FsFeatureSystemFeaturesOperationKinds.Create);
        var values = featureProposal.Operations.Where(op => op.Kind == FsClosedFeatureValuesOperationKinds.Create)
            .Select(op => op.EntityId!.Value).ToArray();
        CanonicalId Value(string text) => values.Single(id =>
            ((IFsSymFeatVal)cache.ServiceLocator.ObjectRepository.GetObject(id.ToGuid()))
                .Name.get_String(cache.DefaultAnalWs).Text == text);
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        names[AddStem(cache, "lilit", verb).Form] = "lilit";
        foreach (var (form, value) in new[] { ("kaun", "Intransitive"), ("palit", "Transitive") })
        {
            var stem = AddStem(cache, form, verb);
            names[stem.Form] = form;
            var features = Created(ApplyOps(cache, AuthorFeatureStructureComposer.Build(cache, new(stem.Msa))),
                MoStemMsaMsFeaturesOperationKinds.CreateMsFeatures);
            ApplyOps(cache, AuthorFeatureValueComposer.Build(cache, new(features, feature, Value(value))));
        }
        var wantsTransitive = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent("ku",
            Vern(cache), MorphType(MoMorphTypeTags.kguidMorphPrefix), "ku", ws,
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, verb, Slots: [slot]))));
        names[Created(wantsTransitive, LexEntryLexemeFormOperationKinds.CreateLexemeForm)] = "ku";
        var wanted = Created(ApplyOps(cache, AuthorFeatureStructureComposer.Build(cache,
            new(Created(wantsTransitive, LexEntryMorphoSyntaxAnalysesOperationKinds.Create)))),
            MoInflAffMsaInflFeatsOperationKinds.CreateInflFeats);
        ApplyOps(cache, AuthorFeatureValueComposer.Build(cache, new(wanted, feature, Value("Transitive"))));
        var derived = ApplyOps(cache, AuthorAffixProcessComposer.Build(cache, new AuthorAffixProcessIntent(
            MorphType(MoMorphTypeTags.kguidMorphPrefix), "causative", ws,
            new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, verb, verb),
            [new("rest", Remaining: true)],
            [new(Insert: [Phoneme(cache, "p"), Phoneme(cache, "a"), Phoneme(cache, "g")]), new(Copy: "rest")])));
        names[Created(derived, LexEntryLexemeFormOperationKinds.CreateLexemeForm)] = "pag";
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        Assert.Equal(["pag+kaun"], await Readings(tracer, path, "pagkaun", names));
        Assert.Equal(["pag+palit"], await Readings(tracer, path, "pagpalit", names));
        Assert.Equal(["pag+lilit"], await Readings(tracer, path, "paglilit", names));
        Assert.Equal(["ku+pag+lilit"], await Readings(tracer, path, "kupaglilit", names));

        var requirement = Created(ApplyOps(cache, AuthorFeatureStructureComposer.Build(cache,
            new(Created(derived, LexEntryMorphoSyntaxAnalysesOperationKinds.Create), "from"))),
            MoDerivAffMsaMsFeaturesOperationKinds.CreateFromMsFeatures);
        ApplyOps(cache, AuthorFeatureValueComposer.Build(cache, new(requirement, feature, Value("Intransitive"))));
        new FwDataProjectLoader().Save(cache);

        Assert.Equal(["pag+kaun"], await Readings(tracer, path, "pagkaun", names));
        Assert.Empty(await Readings(tracer, path, "pagpalit", names));
        // Unification: a stem with no value passes and the value is imposed on the derived word.
        Assert.Equal(["pag+lilit"], await Readings(tracer, path, "paglilit", names));
        // ... and the required intransitive is now on the derived word, so a later transitive requirement fails.
        Assert.Empty(await Readings(tracer, path, "kupaglilit", names));
    }

    private static CanonicalId Phoneme(LcmCache cache, string text) =>
        CanonicalId.FromGuid(cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].PhonemesOC
            .SelectMany(phoneme => phoneme.CodesOS.Select(code => (Code: code, Phoneme: phoneme)))
            .Single(pair => pair.Code.Representation.get_String(cache.DefaultVernWs)?.Text == text).Phoneme.Guid);

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
            .Distinct().Order(StringComparer.Ordinal).ToArray();
        output.WriteLine($"{word}: {(readings.Length == 0 ? "no analysis" : string.Join(" | ", readings))}");
        return readings;
    }
}
