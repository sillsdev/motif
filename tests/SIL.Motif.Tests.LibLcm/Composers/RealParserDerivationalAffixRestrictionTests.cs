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

/// <summary>Checks a process affix's derivational MSA restriction with PanGloss.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserDerivationalAffixRestrictionTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    private static readonly string[] Letters = ["p", "a", "g", "k", "u", "n", "l", "i", "t"];

    [RealParserFact]
    public async Task ADerivationalPrefixRestrictedToAClassStopsApplyingToUnmarkedStems()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, Letters);
        var verb = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Verb", Anal(cache), "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var noun = AddCategory(cache, "Noun");
        var marked = AddInflectionClass(cache, verb, "Class 1");
        var alternateClass = AddInflectionClass(cache, verb, "Class 2");
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        var markedStem = AddStem(cache, "kaun", verb, marked);
        var unmarkedStem = AddStem(cache, "lilit", verb);
        names[markedStem.Form] = "kaun";
        names[unmarkedStem.Form] = "lilit";
        names[AddStem(cache, "palit", verb, alternateClass).Form] = "palit";
        var created = ApplyOps(cache, AuthorAffixProcessComposer.Build(cache, new AuthorAffixProcessIntent(
            MorphType(MoMorphTypeTags.kguidMorphPrefix), "agentive", Anal(cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, verb, noun),
            [new("rest", Remaining: true)],
            [new(Insert: [Phoneme(cache, "p"), Phoneme(cache, "a"), Phoneme(cache, "g")]), new(Copy: "rest")])));
        var msa = Created(created, LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
        names[Created(created, LexEntryLexemeFormOperationKinds.CreateLexemeForm)] = "pag";
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);

        Assert.Equal(["pag+kaun"], await Readings(tracer, path, "pagkaun", names));
        Assert.Equal(["pag+lilit"], await Readings(tracer, path, "paglilit", names));
        Assert.Equal(["pag+palit"], await Readings(tracer, path, "pagpalit", names));

        ApplyOps(cache, EditDerivationalAffixRestrictionComposer.Build(cache, new(msa, FromInflectionClass: marked)));
        new FwDataProjectLoader().Save(cache);

        Assert.Equal(["pag+kaun"], await Readings(tracer, path, "pagkaun", names));
        Assert.Empty(await Readings(tracer, path, "paglilit", names));
        Assert.Empty(await Readings(tracer, path, "pagpalit", names));

        ApplyOps(cache, EditDerivationalAffixRestrictionComposer.Build(cache,
            new(msa, FromInflectionClass: alternateClass)));
        Assert.Equal(alternateClass.ToGuid(), Get<IMoDerivAffMsa>(cache, msa).FromInflectionClassRA!.Guid);
        new FwDataProjectLoader().Save(cache);

        Assert.Empty(await Readings(tracer, path, "pagkaun", names));
        Assert.Empty(await Readings(tracer, path, "paglilit", names));
        Assert.Equal(["pag+palit"], await Readings(tracer, path, "pagpalit", names));

        ApplyOps(cache, EditDerivationalAffixRestrictionComposer.Build(cache,
            new(msa, ClearFromInflectionClass: true)));
        Assert.Null(Get<IMoDerivAffMsa>(cache, msa).FromInflectionClassRA);
        new FwDataProjectLoader().Save(cache);

        Assert.Equal(["pag+kaun"], await Readings(tracer, path, "pagkaun", names));
        Assert.Equal(["pag+lilit"], await Readings(tracer, path, "paglilit", names));
        Assert.Equal(["pag+palit"], await Readings(tracer, path, "pagpalit", names));
    }

    [RealParserFact]
    public async Task ADerivedStemGetsTheToClassSoAClassRestrictedInflectionalPrefixAttachesAndClearingTakesItAway()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, Letters);
        var ws = Anal(cache);
        var verb = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Verb", ws, "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var stemClass = AddInflectionClass(cache, verb, "Native");
        var derivedClass = AddInflectionClass(cache, verb, "Derived");
        var slot = Created(ApplyOps(cache, AuthorAffixSlotComposer.Build(cache,
            new AuthorAffixSlotIntent(verb, "Outer", ws, true))), PartOfSpeechAffixSlotsOperationKinds.Create);
        ApplyOps(cache, AuthorAffixTemplateComposer.Build(cache,
            new AuthorAffixTemplateIntent(verb, "Verb template", ws, [slot], [], true)));
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        names[AddStem(cache, "kaun", verb, stemClass).Form] = "kaun";
        names[Created(ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent("ku",
            Vern(cache), MorphType(MoMorphTypeTags.kguidMorphPrefix), "ku", ws,
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, verb, Slots: [slot]),
            InflectionClasses: [derivedClass]))), LexEntryLexemeFormOperationKinds.CreateLexemeForm)] = "ku";
        var created = ApplyOps(cache, AuthorAffixProcessComposer.Build(cache, new AuthorAffixProcessIntent(
            MorphType(MoMorphTypeTags.kguidMorphPrefix), "again", ws,
            new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, verb, verb),
            [new("rest", Remaining: true)],
            [new(Insert: [Phoneme(cache, "p"), Phoneme(cache, "a"), Phoneme(cache, "g")]), new(Copy: "rest")])));
        var msa = Created(created, LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
        names[Created(created, LexEntryLexemeFormOperationKinds.CreateLexemeForm)] = "pag";
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);

        Assert.Equal(["pag+kaun"], await Readings(tracer, path, "pagkaun", names));
        Assert.Empty(await Readings(tracer, path, "kupagkaun", names));

        ApplyOps(cache, EditDerivationalAffixRestrictionComposer.Build(cache, new(msa, ToInflectionClass: derivedClass)));
        new FwDataProjectLoader().Save(cache);
        Assert.Equal(["ku+pag+kaun"], await Readings(tracer, path, "kupagkaun", names));

        ApplyOps(cache, EditDerivationalAffixRestrictionComposer.Build(cache, new(msa, ClearToInflectionClass: true)));
        new FwDataProjectLoader().Save(cache);
        Assert.Empty(await Readings(tracer, path, "kupagkaun", names));
    }

    [RealParserFact]
    public async Task AProhibitionStopsAClassRestrictedDerivationalPrefixOnOneStemOfTheClassAndNotItsNeighbour()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, Letters);
        var verb = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Verb", Anal(cache), "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var noun = AddCategory(cache, "Noun");
        var host = AddInflectionClass(cache, verb, "Host");
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        var kept = AddStem(cache, "kaun", verb, host);
        var excluded = AddStem(cache, "palit", verb, host);
        names[kept.Form] = "kaun";
        names[excluded.Form] = "palit";
        var created = ApplyOps(cache, AuthorAffixProcessComposer.Build(cache, new AuthorAffixProcessIntent(
            MorphType(MoMorphTypeTags.kguidMorphPrefix), "agentive", Anal(cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, verb, noun),
            [new("rest", Remaining: true)],
            [new(Insert: [Phoneme(cache, "p"), Phoneme(cache, "a"), Phoneme(cache, "g")]), new(Copy: "rest")])));
        var msa = Created(created, LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
        names[Created(created, LexEntryLexemeFormOperationKinds.CreateLexemeForm)] = "pag";
        ApplyOps(cache, EditDerivationalAffixRestrictionComposer.Build(cache, new(msa, FromInflectionClass: host)));
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);

        Assert.Equal(["pag+kaun"], await Readings(tracer, path, "pagkaun", names));
        Assert.Equal(["pag+palit"], await Readings(tracer, path, "pagpalit", names));

        ApplyOps(cache, AuthorAdhocProhibitionComposer.Build(cache, new AuthorAdhocProhibitionIntent(
            AdhocProhibitionPayload.Morpheme, msa, [excluded.Msa], "adjacent-to-left")));
        new FwDataProjectLoader().Save(cache);

        Assert.Equal(["pag+kaun"], await Readings(tracer, path, "pagkaun", names));
        Assert.Empty(await Readings(tracer, path, "pagpalit", names));
        Assert.Equal(["palit"], await Readings(tracer, path, "palit", names));
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
