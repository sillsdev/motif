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
/// Proves the rule editor with the real parser on a synthetic language: the segment-only nasal chain (class to
/// nasal, archiphoneme deleted before a nasal, archiphoneme to the velar nasal elsewhere), rule order, disabling,
/// multi-item rules, insertion and optional context items. The sounds are synthetic; N is the archiphoneme and x
/// the velar nasal.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserRuleEditorTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    private static readonly string[] Letters = ["p", "t", "k", "m", "n", "x", "a", "u", "i", "N"];
    private static readonly string[] Stems = ["puk", "tak", "kan", "apa"];

    private sealed record Chain(string Path, Dictionary<CanonicalId, string> Names, LcmCache Cache,
        CanonicalId[] Rules, Dictionary<string, CanonicalId> Phonemes);

    /// <summary>The nasal chain authored in the order that fails: the elsewhere rule first.</summary>
    private Chain Build(bool featured)
    {
        var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache);
        var phonemes = new Dictionary<string, CanonicalId>();
        var place = featured ? MakeFeature(cache, "place", "labial", "coronal", "velar", "unspecified") : default;
        var nasal = featured ? MakeFeature(cache, "nasal", "plus", "minus") : default;
        foreach (var letter in Letters)
        {
            var bundle = new List<PhonologicalFeatureValue>();
            if (featured && SpecOf(letter) is { } spec)
            {
                bundle.Add(new(place.Feature, place.Values[spec.Place]));
                bundle.Add(new(nasal.Feature, nasal.Values[spec.Nasal ? "plus" : "minus"]));
            }
            phonemes[letter] = Created(ApplyOps(cache, AuthorPhonemeComposer.Build(cache,
                new(letter, [letter], bundle.Count == 0 ? null : bundle))), PhPhonemeSetPhonemesOperationKinds.Create);
        }
        var classes = new Dictionary<string, CanonicalId>();
        foreach (var (name, members) in new[] { ("NSLAB", "p"), ("NSCOR", "t"), ("NSVEL", "k"), ("NASAL", "mnx") })
            classes[name] = Created(ApplyOps(cache, AuthorNaturalClassComposer.Build(cache, new(name, name,
                members.Select(c => phonemes[c.ToString()]).ToArray()))), PhPhonDataNaturalClassesOperationKinds.Create);

        var category = AddCategory(cache, "Word");
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        foreach (var stem in Stems) names[AddStem(cache, stem, category).Form] = stem;
        var prefix = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent("paN",
            Vern(cache), MorphType(MoMorphTypeTags.kguidMorphPrefix), "nominalise", Anal(cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Unclassified, category))));
        names[Created(prefix, LexEntryLexemeFormOperationKinds.CreateLexemeForm)] = "paN";

        EnvironmentContext N() => new(Phoneme: phonemes["N"]);
        EnvironmentContext Plus() => new(Boundary: "morpheme");
        PhonologicalRuleItem Seg(string s) => new(Phoneme: phonemes[s]);
        PhonologicalRuleItem Class(string s) => new(NaturalClass: classes[s]);
        var rules = new List<CanonicalId>();
        void Add(string name, PhonologicalRuleItem[] input, PhonologicalRuleItem[] output,
            EnvironmentContext[] left, EnvironmentContext[] right)
        {
            var existing = cache.LangProject.PhonologicalDataOA.PhonRulesOS;
            rules.Add(Created(ApplyOps(cache, AuthorPhonologicalRuleComposer.Build(cache, new(name,
                PhonologicalRuleDirection.Simultaneous, input, output, left, right,
                existing.Count == 0 ? null : new Placement(Id(existing[^1]), null)))), PhPhonDataPhonRulesOperationKinds.Create));
        }
        Add("R3 N to x elsewhere", [Seg("N")], [Seg("x")], [], []);
        Add("R1a labial to m", [Class("NSLAB")], [Seg("m")], [N(), Plus()], []);
        Add("R1b coronal to n", [Class("NSCOR")], [Seg("n")], [N(), Plus()], []);
        Add("R1c velar to x", [Class("NSVEL")], [Seg("x")], [N(), Plus()], []);
        Add("R2 N deleted before a nasal", [Seg("N")], [], [], [Plus(), new(NaturalClass: classes["NASAL"])]);
        return new(cache.ProjectId.Path, names, cache, rules.ToArray(), phonemes);
    }

    private static (string Place, bool Nasal)? SpecOf(string letter) => letter switch
    {
        "p" => ("labial", false), "t" => ("coronal", false), "k" => ("velar", false),
        "m" => ("labial", true), "n" => ("coronal", true), "x" => ("velar", true), "N" => ("unspecified", true),
        _ => null,
    };

    private static (CanonicalId Feature, Dictionary<string, CanonicalId> Values) MakeFeature(
        LcmCache cache, string name, params string[] valueNames)
    {
        var values = new Dictionary<string, CanonicalId>();
        IFsClosedFeature feature = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            cache.LangProject.PhFeatureSystemOA ??= services.GetInstance<IFsFeatureSystemFactory>().Create();
            feature = services.GetInstance<IFsClosedFeatureFactory>().Create();
            cache.LangProject.PhFeatureSystemOA.FeaturesOC.Add(feature);
            feature.Name.set_String(cache.DefaultAnalWs, name);
            feature.Abbreviation.set_String(cache.DefaultAnalWs, name);
            foreach (var valueName in valueNames)
            {
                var value = services.GetInstance<IFsSymFeatValFactory>().Create();
                feature.ValuesOC.Add(value);
                value.Name.set_String(cache.DefaultAnalWs, valueName);
                value.Abbreviation.set_String(cache.DefaultAnalWs, valueName);
                values[valueName] = Id(value);
            }
        });
        return (Id(feature), values);
    }

    private static void Reorder(Chain chain) => ApplyOps(chain.Cache, OrderPhonologicalRulesComposer.Build(chain.Cache,
        new(Order: [chain.Rules[1], chain.Rules[2], chain.Rules[3], chain.Rules[4], chain.Rules[0]])));

    private async Task<string[]> Readings(PanGlossTracer tracer, Chain chain, string word)
    {
        var outcome = Assert.IsType<PanGlossTraceOutcome.Completed>(
            await tracer.TraceAsync(chain.Path, word, CancellationToken.None));
        var details = outcome.Details!;
        Assert.True(details.SearchCompleted, word);
        Assert.False(details.Capped || details.TimedOut || details.InvalidShape, word);
        var readings = outcome.Document!.Analyses.Select(analysis => string.Join("+", analysis.Morphs.Select(morph =>
            chain.Names.TryGetValue(CanonicalId.FromGuid(Guid.Parse(morph.FormId!)), out var name) ? name : "?" + morph.Form)))
            .Distinct().Order(StringComparer.Ordinal).ToArray();
        output.WriteLine($"{word}: {(readings.Length == 0 ? "no analysis" : string.Join(" | ", readings))}");
        return readings;
    }

    private static void Save(Chain chain) => new FwDataProjectLoader().Save(chain.Cache);

    [RealParserFact]
    public async Task TheNasalChainParsesOnlyOnceTheRulesAreInTheRightOrderAndStopsWhenARuleIsDisabled()
    {
        var chain = Build(featured: true);
        using var cache = chain.Cache;
        Save(chain);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);

        // The elsewhere rule comes first, so the archiphoneme is gone before the class rules can see it.
        Assert.Equal(["paN+puk"], await Readings(tracer, chain, "paxpuk"));
        Assert.Empty(await Readings(tracer, chain, "pamuk")); // expected failure: PanGloss issue #120 (featureless grammars ignore natural classes)

        Reorder(chain);
        Save(chain);
        Assert.Equal(["paN+puk"], await Readings(tracer, chain, "pamuk"));
        Assert.Equal(["paN+tak"], await Readings(tracer, chain, "panak"));
        Assert.Equal(["paN+kan"], await Readings(tracer, chain, "paxan"));
        Assert.Equal(["paN+apa"], await Readings(tracer, chain, "paxapa"));
        Assert.Empty(await Readings(tracer, chain, "paxpuk"));

        var deletion = chain.Rules[4];
        ApplyOps(cache, SetPhonologicalRuleDisabledComposer.Build(cache, new(deletion, false, true)));
        Save(chain);
        Assert.Empty(await Readings(tracer, chain, "pamuk")); // expected failure: PanGloss issue #120 (featureless grammars ignore natural classes)
        Assert.Equal(["paN+puk"], await Readings(tracer, chain, "paxmuk"));

        ApplyOps(cache, SetPhonologicalRuleDisabledComposer.Build(cache, new(deletion, true, false)));
        Save(chain);
        Assert.Equal(["paN+puk"], await Readings(tracer, chain, "pamuk"));

        // Moving one rule reaches the same order as naming the whole order.
        ApplyOps(cache, OrderPhonologicalRulesComposer.Build(cache, new(Rule: chain.Rules[0], Before: chain.Rules[1])));
        Save(chain);
        Assert.Empty(await Readings(tracer, chain, "pamuk")); // expected failure: PanGloss issue #120 (featureless grammars ignore natural classes)
    }

    [RealParserFact]
    public async Task ARuleEditedInPlaceRunsAsEditedAndADeletedRuleStopsRunning()
    {
        var chain = Build(featured: true);
        using var cache = chain.Cache;
        Reorder(chain);
        Save(chain);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        Assert.Equal(["paN+tak"], await Readings(tracer, chain, "panak"));

        // R1b now changes the coronal into the velar nasal instead.
        ApplyOps(cache, EditPhonologicalRuleComposer.Build(cache, new(chain.Rules[2],
            new(Name: "R1b coronal to x", Output: [new(Phoneme: chain.Phonemes["x"])]),
            new(Output: [new(Phoneme: chain.Phonemes["n"])]))));
        Save(chain);
        Assert.Empty(await Readings(tracer, chain, "panak"));
        Assert.Equal(["paN+tak"], await Readings(tracer, chain, "paxak"));

        ApplyOps(cache, DeletePhonologicalRuleComposer.Build(cache, new(chain.Rules[2], "R1b coronal to x")));
        Save(chain);
        Assert.Empty(await Readings(tracer, chain, "paxak"));
        Assert.Equal(["paN+tak"], await Readings(tracer, chain, "paxtak"));
    }

    [RealParserFact]
    public async Task AFeaturelessGrammarIgnoresTheSegmentClassesInTheChain_PanGlossIssue120()
    {
        // sillsdev/pangloss#120: featureless grammars ignore natural classes in rewrite rules.
        var chain = Build(featured: false);
        using var cache = chain.Cache;
        Reorder(chain);
        Save(chain);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        Assert.Empty(await Readings(tracer, chain, "pamuk")); // expected failure: PanGloss issue #120 (featureless grammars ignore natural classes)
    }

    [RealParserFact]
    public async Task AnInsertionRuleAndADeletionRuleParse()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, Letters);
        var category = AddCategory(cache, "Word");
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        foreach (var stem in new[] { "pta", "kini" }) names[AddStem(cache, stem, category).Form] = stem;
        PhonologicalRuleItem Seg(string s) => new(Phoneme: PhonemeId(cache, s));
        var first = Created(ApplyOps(cache, AuthorPhonologicalRuleComposer.Build(cache, new("insert i between p and t",
            PhonologicalRuleDirection.Simultaneous, [], [Seg("i")], [new(Phoneme: PhonemeId(cache, "p"))],
            [new(Phoneme: PhonemeId(cache, "t"))]))), PhPhonDataPhonRulesOperationKinds.Create);
        ApplyOps(cache, AuthorPhonologicalRuleComposer.Build(cache, new("delete n before i",
            PhonologicalRuleDirection.Simultaneous, [Seg("n")], [], [], [new(Phoneme: PhonemeId(cache, "i"))],
            new Placement(first, null))));
        var chain = new Chain(cache.ProjectId.Path, names, cache, [], []);
        Save(chain);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);

        Assert.Equal(["pta"], await Readings(tracer, chain, "pita"));
        Assert.Empty(await Readings(tracer, chain, "pta"));
        Assert.Equal(["kini"], await Readings(tracer, chain, "kii"));
        Assert.Empty(await Readings(tracer, chain, "kini"));
    }

    [RealParserFact]
    public async Task AClassRestrictedRuleAtAMorphemeBoundaryChangesOnlyStemsOfThatClass()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, Letters.Append("w").ToArray());
        var category = AddCategory(cache, "Verb");
        var classA = AddInflectionClass(cache, category, "A");
        var classB = AddInflectionClass(cache, category, "B");
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        names[AddStem(cache, "kau", category, classA).Form] = "kau";
        names[AddStem(cache, "tau", category, classB).Form] = "tau";
        var suffix = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent("wa",
            Vern(cache), MorphType(MoMorphTypeTags.kguidMorphSuffix), "passive", Anal(cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, category, category))));
        names[Created(suffix, LexEntryLexemeFormOperationKinds.CreateLexemeForm)] = "wa";
        var rule = Created(ApplyOps(cache, AuthorPhonologicalRuleComposer.Build(cache, new("u to i before the suffix w",
            PhonologicalRuleDirection.Simultaneous, [new(Phoneme: PhonemeId(cache, "u"))],
            [new(Phoneme: PhonemeId(cache, "i"))], [], [new(Boundary: "morpheme"), new(Phoneme: PhonemeId(cache, "w"))]))),
            PhPhonDataPhonRulesOperationKinds.Create);
        var chain = new Chain(cache.ProjectId.Path, names, cache, [], []);
        Save(chain);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);

        Assert.Equal(["kau+wa"], await Readings(tracer, chain, "kaiwa"));
        Assert.Equal(["tau+wa"], await Readings(tracer, chain, "taiwa"));

        ApplyOps(cache, EditPhonologicalRuleFeaturesComposer.Build(cache, new(rule, RequiredFeatures: [classA])));
        Save(chain);
        Assert.Equal(["kau+wa"], await Readings(tracer, chain, "kaiwa"));
        Assert.Empty(await Readings(tracer, chain, "kauwa"));
        Assert.Equal(["tau+wa"], await Readings(tracer, chain, "tauwa"));
        Assert.Empty(await Readings(tracer, chain, "taiwa"));
    }

    [RealParserFact]
    public async Task AGlottalStopIsDeletedAtTheEndOfAWordOnly()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, Letters.Append("q").ToArray());
        var category = AddCategory(cache, "Word");
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        foreach (var stem in new[] { "kaq", "qaka" }) names[AddStem(cache, stem, category).Form] = stem;
        ApplyOps(cache, AuthorPhonologicalRuleComposer.Build(cache, new("glottal deleted word finally",
            PhonologicalRuleDirection.Simultaneous, [new(Phoneme: PhonemeId(cache, "q"))], [], [],
            [new(Boundary: "word")])));
        var chain = new Chain(cache.ProjectId.Path, names, cache, [], []);
        Save(chain);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);

        Assert.Equal(["kaq"], await Readings(tracer, chain, "ka"));
        Assert.Empty(await Readings(tracer, chain, "kaq"));
        Assert.Equal(["qaka"], await Readings(tracer, chain, "qaka"));
    }

    [RealParserFact]
    public async Task ARuleExcludedByAFeatureStaysOffTheWholeReduplicatedWord()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, Letters);
        var ws = Anal(cache);
        var category = AddCategory(cache, "Word");
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        names[AddStem(cache, "kuna", category).Form] = "kuna";
        var kept = AddStem(cache, "kuni", category);
        names[kept.Form] = "kuni";
        var process = ApplyOps(cache, AuthorAffixProcessComposer.Build(cache, new AuthorAffixProcessIntent(
            MorphType(MoMorphTypeTags.kguidMorphPrefix), "plural", ws,
            new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, category, category),
            [new("rest", Remaining: true)], [new(Copy: "rest"), new(Copy: "rest")])));
        names[Created(process, LexEntryLexemeFormOperationKinds.CreateLexemeForm)] = "RED";
        var rule = Created(ApplyOps(cache, AuthorPhonologicalRuleComposer.Build(cache, new("u deleted between k and n",
            PhonologicalRuleDirection.Simultaneous, [new(Phoneme: PhonemeId(cache, "u"))], [],
            [new(Phoneme: PhonemeId(cache, "k"))], [new(Phoneme: PhonemeId(cache, "n"))]))),
            PhPhonDataPhonRulesOperationKinds.Create);
        var feature = Created(ApplyOps(cache, AuthorExceptionFeatureComposer.Build(cache,
            new("Keeps u", ws, "ku"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        ApplyOps(cache, EditExceptionFeaturesComposer.Build(cache, new(kept.Msa, [feature])));
        ApplyOps(cache, EditPhonologicalRuleFeaturesComposer.Build(cache, new(rule, ExcludedFeatures: [feature])));
        var chain = new Chain(cache.ProjectId.Path, names, cache, [], []);
        Save(chain);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);

        Assert.Equal(["RED+kuna"], await Readings(tracer, chain, "knakna"));
        Assert.Empty(await Readings(tracer, chain, "kunakuna"));
        Assert.Equal(["RED+kuni"], await Readings(tracer, chain, "kunikuni"));
        Assert.Empty(await Readings(tracer, chain, "knikni"));
    }

    [RealParserFact]
    public async Task AnOptionalContextItemMatchesWithAndWithoutTheItem()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, Letters);
        var category = AddCategory(cache, "Word");
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        foreach (var stem in new[] { "kanp", "kanup", "kant" }) names[AddStem(cache, stem, category).Form] = stem;
        ApplyOps(cache, AuthorPhonologicalRuleComposer.Build(cache, new("n to m before optional u then p",
            PhonologicalRuleDirection.Simultaneous, [new(Phoneme: PhonemeId(cache, "n"))],
            [new(Phoneme: PhonemeId(cache, "m"))], [],
            [new(Phoneme: PhonemeId(cache, "u"), Minimum: 0, Maximum: 1), new(Phoneme: PhonemeId(cache, "p"))])));
        var chain = new Chain(cache.ProjectId.Path, names, cache, [], []);
        Save(chain);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);

        Assert.Equal(["kanp"], await Readings(tracer, chain, "kamp"));
        Assert.Equal(["kanup"], await Readings(tracer, chain, "kamup"));
        Assert.Equal(["kant"], await Readings(tracer, chain, "kant"));
        Assert.Empty(await Readings(tracer, chain, "kanp"));
        Assert.Empty(await Readings(tracer, chain, "kamt"));
    }

    private static CanonicalId PhonemeId(LcmCache cache, string text) =>
        CanonicalId.FromGuid(cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].PhonemesOC
            .SelectMany(phoneme => phoneme.CodesOS.Select(code => (Code: code, Phoneme: phoneme)))
            .Single(pair => pair.Code.Representation.get_String(cache.DefaultVernWs)?.Text == text).Phoneme.Guid);
}
