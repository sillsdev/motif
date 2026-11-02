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
/// A noun stem that is also a verb stem plus a derivational suffix has two readings after a prefix. A prohibition
/// naming the noun stem removes only the stored-stem reading; a second one naming the verb stem and the suffix
/// analysis together removes the derived reading, because a derivational suffix is a morpheme a prohibition can name.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserDerivedStemProhibitionTests(PristineProjectFixture pristine)
{
    [RealParserFact]
    public async Task AStemProhibitionLeavesTheDerivedReadingAndTheSuffixProhibitionStopsIt()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "k", "i", "o", "m", "b", "a", "j");
        var ws = Anal(cache);
        var verb = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Verb", ws, "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var noun = AddCategory(cache, "Noun");
        var slot = Created(ApplyOps(cache, AuthorAffixSlotComposer.Build(cache,
            new AuthorAffixSlotIntent(noun, "PREFIX", ws, true))), PartOfSpeechAffixSlotsOperationKinds.Create);
        ApplyOps(cache, AuthorAffixTemplateComposer.Build(cache,
            new AuthorAffixTemplateIntent(noun, "Noun template", ws, [slot], [], true)));
        var stored = AddStem(cache, "ombaji", noun);
        var root = AddStem(cache, "omb", verb);
        var suffix = Created(ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent("aji",
            Vern(cache), MorphType(MoMorphTypeTags.kguidMorphSuffix), "agent", ws,
            new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, verb, noun)))),
            LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
        var prefix = Created(ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent("ki",
            Vern(cache), MorphType(MoMorphTypeTags.kguidMorphPrefix), "ki", ws,
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, noun, Slots: [slot])))),
            LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);

        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        Assert.Equal(2, (await Readings(tracer, path, "kiombaji")).Count);

        ApplyOps(cache, AuthorAdhocProhibitionComposer.Build(cache, new AuthorAdhocProhibitionIntent(
            AdhocProhibitionPayload.Morpheme, prefix, [stored.Msa], "adjacent-to-left")));
        new FwDataProjectLoader().Save(cache);
        var derived = Assert.Single(await Readings(tracer, path, "kiombaji"));
        Assert.Equal(root.Form, derived[1]);

        ApplyOps(cache, AuthorAdhocProhibitionComposer.Build(cache, new AuthorAdhocProhibitionIntent(
            AdhocProhibitionPayload.Morpheme, prefix, [root.Msa, suffix], "adjacent-to-left")));
        new FwDataProjectLoader().Save(cache);
        Assert.Empty(await Readings(tracer, path, "kiombaji"));
    }

    private static async Task<List<CanonicalId[]>> Readings(PanGlossTracer tracer, string path, string word)
    {
        var outcome = Assert.IsType<PanGlossTraceOutcome.Completed>(
            await tracer.TraceAsync(path, word, CancellationToken.None));
        Assert.True(outcome.Details!.SearchCompleted, word);
        return outcome.Document!.Analyses.Select(analysis => analysis.Morphs
            .Select(morph => CanonicalId.FromGuid(Guid.Parse(morph.FormId!))).ToArray()).ToList();
    }
}
