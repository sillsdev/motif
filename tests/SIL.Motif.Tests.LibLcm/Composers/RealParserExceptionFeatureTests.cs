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

/// <summary>Checks with PanGloss that exception features restrict inflectional and derivational affixes.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserExceptionFeatureTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    private static readonly string[] Letters = ["p", "a", "g", "k", "u", "n", "l", "i", "t"];

    [RealParserFact]
    public async Task AnAffixThatRequiresAnExceptionFeatureAttachesOnlyToStemsThatCarryIt()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, Letters);
        var ws = Anal(cache);
        var verb = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Verb", ws, "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var noun = AddCategory(cache, "Noun");
        var slot = Created(ApplyOps(cache, AuthorAffixSlotComposer.Build(cache,
            new AuthorAffixSlotIntent(verb, "Outer", ws, true))), PartOfSpeechAffixSlotsOperationKinds.Create);
        ApplyOps(cache, AuthorAffixTemplateComposer.Build(cache,
            new AuthorAffixTemplateIntent(verb, "Verb template", ws, [slot], [], true)));
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        var tagged = AddStem(cache, "kaun", verb);
        names[tagged.Form] = "kaun";
        names[AddStem(cache, "lilit", verb).Form] = "lilit";
        var prefix = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent("ku",
            Vern(cache), MorphType(MoMorphTypeTags.kguidMorphPrefix), "ku", ws,
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, verb, Slots: [slot]))));
        names[Created(prefix, LexEntryLexemeFormOperationKinds.CreateLexemeForm)] = "ku";
        var inflectional = Created(prefix, LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
        var process = ApplyOps(cache, AuthorAffixProcessComposer.Build(cache, new AuthorAffixProcessIntent(
            MorphType(MoMorphTypeTags.kguidMorphPrefix), "agentive", ws,
            new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, verb, noun),
            [new("rest", Remaining: true)],
            [new(Insert: [Phoneme(cache, "p"), Phoneme(cache, "a"), Phoneme(cache, "g")]), new(Copy: "rest")])));
        var derivational = Created(process, LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
        names[Created(process, LexEntryLexemeFormOperationKinds.CreateLexemeForm)] = "pag";
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);

        Assert.Equal(["ku+kaun"], await Readings(tracer, path, "kukaun", names));
        Assert.Equal(["ku+lilit"], await Readings(tracer, path, "kulilit", names));
        Assert.Equal(["pag+kaun"], await Readings(tracer, path, "pagkaun", names));
        Assert.Equal(["pag+lilit"], await Readings(tracer, path, "paglilit", names));

        var feature = Created(ApplyOps(cache, AuthorExceptionFeatureComposer.Build(cache,
            new("Takes ku and pag", ws, "kp"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        ApplyOps(cache, EditExceptionFeaturesComposer.Build(cache, new(tagged.Msa, [feature])));
        ApplyOps(cache, EditExceptionFeaturesComposer.Build(cache, new(inflectional, [feature])));
        ApplyOps(cache, EditDerivationalAffixRestrictionComposer.Build(cache,
            new(derivational, FromExceptionFeatures: [feature])));
        new FwDataProjectLoader().Save(cache);

        Assert.Equal(["ku+kaun"], await Readings(tracer, path, "kukaun", names));
        Assert.Empty(await Readings(tracer, path, "kulilit", names));
        Assert.Equal(["pag+kaun"], await Readings(tracer, path, "pagkaun", names));
        Assert.Empty(await Readings(tracer, path, "paglilit", names));

        ApplyOps(cache, EditExceptionFeaturesComposer.Build(cache, new(tagged.Msa, [])));
        new FwDataProjectLoader().Save(cache);

        Assert.Empty(await Readings(tracer, path, "kukaun", names));
        Assert.Empty(await Readings(tracer, path, "pagkaun", names));

        ApplyOps(cache, EditExceptionFeaturesComposer.Build(cache, new(inflectional, [])));
        ApplyOps(cache, EditDerivationalAffixRestrictionComposer.Build(cache,
            new(derivational, FromExceptionFeatures: [])));
        new FwDataProjectLoader().Save(cache);

        Assert.Equal(["ku+lilit"], await Readings(tracer, path, "kulilit", names));
        Assert.Equal(["pag+lilit"], await Readings(tracer, path, "paglilit", names));
    }

    [RealParserFact]
    public async Task ARuleThatExcludesOrRequiresAnExceptionFeatureAppliesOnlyToTheStemsItAllows()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, Letters);
        var ws = Anal(cache);
        var verb = AddCategory(cache, "Verb");
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        var kept = AddStem(cache, "kaun", verb);
        names[kept.Form] = "kaun";
        names[AddStem(cache, "taun", verb).Form] = "taun";
        var created = ApplyOps(cache, AuthorPhonologicalRuleComposer.Build(cache, new("u becomes i between a and n",
            PhonologicalRuleDirection.Simultaneous, [new(Phoneme: Phoneme(cache, "u"))],
            [new(Phoneme: Phoneme(cache, "i"))], [new(Phoneme: Phoneme(cache, "a"))],
            [new(Phoneme: Phoneme(cache, "n"))])));
        var rule = Created(created, PhPhonDataPhonRulesOperationKinds.Create);
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);

        Assert.Empty(await Readings(tracer, path, "kaun", names));
        Assert.Equal(["kaun"], await Readings(tracer, path, "kain", names));
        Assert.Empty(await Readings(tracer, path, "taun", names));
        Assert.Equal(["taun"], await Readings(tracer, path, "tain", names));

        var feature = Created(ApplyOps(cache, AuthorExceptionFeatureComposer.Build(cache,
            new("Keeps u", ws, "ku"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        ApplyOps(cache, EditExceptionFeaturesComposer.Build(cache, new(kept.Msa, [feature])));
        ApplyOps(cache, EditPhonologicalRuleFeaturesComposer.Build(cache, new(rule, ExcludedFeatures: [feature])));
        new FwDataProjectLoader().Save(cache);

        Assert.Equal(["kaun"], await Readings(tracer, path, "kaun", names));
        Assert.Empty(await Readings(tracer, path, "kain", names));
        Assert.Empty(await Readings(tracer, path, "taun", names));
        Assert.Equal(["taun"], await Readings(tracer, path, "tain", names));

        ApplyOps(cache, EditPhonologicalRuleFeaturesComposer.Build(cache,
            new(rule, RequiredFeatures: [feature], ExcludedFeatures: [])));
        new FwDataProjectLoader().Save(cache);

        Assert.Empty(await Readings(tracer, path, "kaun", names));
        Assert.Equal(["kaun"], await Readings(tracer, path, "kain", names));
        Assert.Equal(["taun"], await Readings(tracer, path, "taun", names));
        Assert.Empty(await Readings(tracer, path, "tain", names));

        ApplyOps(cache, EditPhonologicalRuleFeaturesComposer.Build(cache, new(rule, RequiredFeatures: [])));
        new FwDataProjectLoader().Save(cache);

        Assert.Equal(["kaun"], await Readings(tracer, path, "kain", names));
        Assert.Equal(["taun"], await Readings(tracer, path, "tain", names));
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
            .Distinct().Order(StringComparer.Ordinal).ToArray();
        output.WriteLine($"{word}: {(readings.Length == 0 ? "no analysis" : string.Join(" | ", readings))}");
        return readings;
    }

    private static CanonicalId Phoneme(LcmCache cache, string text) =>
        CanonicalId.FromGuid(cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].PhonemesOC
            .SelectMany(phoneme => phoneme.CodesOS.Select(code => (Code: code, Phoneme: phoneme)))
            .Single(pair => pair.Code.Representation.get_String(cache.DefaultVernWs)?.Text == text).Phoneme.Guid);
}
