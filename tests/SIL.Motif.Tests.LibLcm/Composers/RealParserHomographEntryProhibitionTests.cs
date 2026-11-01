using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>
/// A morpheme prohibition names one entry's grammatical analysis, so it restricts one of two stems spelled alike
/// while the other still takes the prefix; no category or exception feature can tell two same-category homographs apart.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserHomographEntryProhibitionTests(PristineProjectFixture pristine)
{
    [RealParserFact]
    public async Task AProhibitionNamingOneHomographEntryStopsOnlyThatEntryTakingThePrefix()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "m", "a", "b", "g");
        var ws = Anal(cache);
        var verb = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Verb", ws, "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var slot = Created(ApplyOps(cache, AuthorAffixSlotComposer.Build(cache,
            new AuthorAffixSlotIntent(verb, "PREFIX", ws, true))), PartOfSpeechAffixSlotsOperationKinds.Create);
        ApplyOps(cache, AuthorAffixTemplateComposer.Build(cache,
            new AuthorAffixTemplateIntent(verb, "Verb template", ws, [slot], [], true)));
        var first = AddStem(cache, "bag", verb);
        var second = AddStem(cache, "bag", verb);
        var prefix = Created(ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent("ma",
            Vern(cache), MorphType(MoMorphTypeTags.kguidMorphPrefix), "ma", Anal(cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, verb, Slots: [slot])))),
            LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);

        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        Assert.Equal(2, (await Readings(tracer, path, "mabag")).Count);
        Assert.Equal(2, (await Readings(tracer, path, "bag")).Count);

        ApplyOps(cache, AuthorAdhocProhibitionComposer.Build(cache, new AuthorAdhocProhibitionIntent(
            AdhocProhibitionPayload.Morpheme, prefix, [second.Msa], "adjacent-to-left")));
        new FwDataProjectLoader().Save(cache);

        var prefixed = await Readings(tracer, path, "mabag");
        Assert.Equal(first.Form, Assert.Single(prefixed)[1]);
        Assert.Equal(2, (await Readings(tracer, path, "bag")).Count);
    }

    [RealParserFact]
    public async Task AProhibitionNamingOneHomographStopsAReduplicatingPrefixProcessOnThatStemOnly()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "b", "a", "g");
        var verb = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Verb", Anal(cache), "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var first = AddStem(cache, "bag", verb);
        var second = AddStem(cache, "bag", verb);
        var phonemes = Phonemes(cache);
        var process = ApplyOps(cache, AuthorAffixProcessComposer.Build(cache, new AuthorAffixProcessIntent(
            MorphType(MoMorphTypeTags.kguidMorphPrefix), "R", Anal(cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Unclassified, verb),
            [new("c", Phoneme: phonemes["b"]), new("v", Phoneme: phonemes["a"]), new("rest", Remaining: true)],
            [new(Copy: "c"), new(Copy: "v"), new(Copy: "c"), new(Copy: "v"), new(Copy: "rest")])));
        var processMsa = Created(process, LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);

        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        Assert.Equal(2, (await Readings(tracer, path, "babag")).Count);

        ApplyOps(cache, AuthorAdhocProhibitionComposer.Build(cache, new AuthorAdhocProhibitionIntent(
            AdhocProhibitionPayload.Morpheme, processMsa, [second.Msa], "adjacent-to-left")));
        new FwDataProjectLoader().Save(cache);

        var reduplicated = await Readings(tracer, path, "babag");
        Assert.Equal(first.Form, reduplicated.Single().Last());
        Assert.Equal(2, (await Readings(tracer, path, "bag")).Count);
    }

    private static Dictionary<string, CanonicalId> Phonemes(LcmCache cache) =>
        cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].PhonemesOC
            .SelectMany(phoneme => phoneme.CodesOS.Select(code =>
                (Text: code.Representation.get_String(cache.DefaultVernWs)?.Text, Phoneme: phoneme)))
            .Where(item => item.Text is not null)
            .ToDictionary(item => item.Text!, item => CanonicalId.FromGuid(item.Phoneme.Guid), StringComparer.Ordinal);

    private static async Task<List<CanonicalId[]>> Readings(PanGlossTracer tracer, string path, string word)
    {
        var outcome = Assert.IsType<PanGlossTraceOutcome.Completed>(
            await tracer.TraceAsync(path, word, CancellationToken.None));
        Assert.True(outcome.Details!.SearchCompleted, word);
        return outcome.Document!.Analyses.Select(analysis => analysis.Morphs
            .Select(morph => CanonicalId.FromGuid(Guid.Parse(morph.FormId!))).ToArray()).ToList();
    }
}
