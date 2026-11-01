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
/// One form of a later affix is blocked after one specific form of an earlier affix, as FieldWorks' allomorph ad hoc
/// rule reads it: the other forms of both affixes still combine. The rule names forms of two entries, because one
/// entry never occurs twice in a word.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserAllomorphAfterAllomorphProhibitionTests(PristineProjectFixture pristine)
{
    [RealParserFact]
    public async Task OneFormOfALaterAffixIsBlockedAfterOneFormOfAnEarlierAffixAndNoOtherPair()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "k", "a", "m", "e", "o", "l", "i");
        var ws = Anal(cache);
        var verb = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Verb", ws, "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var slots = ApplyOps(cache, [
            .. AuthorAffixSlotComposer.Build(cache, new AuthorAffixSlotIntent(verb, "FIRST", ws, true)),
            .. AuthorAffixSlotComposer.Build(cache, new AuthorAffixSlotIntent(verb, "SECOND", ws, true))])
            .Operations.Where(op => op.Kind == PartOfSpeechAffixSlotsOperationKinds.Create)
            .Select(op => op.EntityId!.Value).ToArray();
        ApplyOps(cache, AuthorAffixTemplateComposer.Build(cache,
            new AuthorAffixTemplateIntent(verb, "Verb template", ws, [], slots, true)));

        var kam = AddStem(cache, "kam", verb);
        var applicative = Suffix(cache, "e", slots[0], verb);
        var applicativeO = AlternateSuffix(cache, applicative.Entry, "o");
        var stative = Suffix(cache, "lik", slots[1], verb);
        var stativeK = AlternateSuffix(cache, stative.Entry, "k");
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);

        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        foreach (var word in new[] { "kamelik", "kamek", "kamolik", "kamok" })
            Assert.Single(await Readings(tracer, path, word));

        ApplyOps(cache, AuthorAdhocProhibitionComposer.Build(cache, new AuthorAdhocProhibitionIntent(
            AdhocProhibitionPayload.Allomorph, stative.Form, [applicative.Form], "adjacent-to-right")));
        new FwDataProjectLoader().Save(cache);

        Assert.Empty(await Readings(tracer, path, "kamelik"));
        Assert.Equal([[kam.Form, applicative.Form, stativeK]], await Readings(tracer, path, "kamek"));
        Assert.Equal([[kam.Form, applicativeO, stative.Form]], await Readings(tracer, path, "kamolik"));
        Assert.Equal([[kam.Form, applicativeO, stativeK]], await Readings(tracer, path, "kamok"));
        Assert.Equal([[kam.Form, applicative.Form]], await Readings(tracer, path, "kame"));
    }

    private static async Task<List<CanonicalId[]>> Readings(PanGlossTracer tracer, string path, string word)
    {
        var outcome = Assert.IsType<PanGlossTraceOutcome.Completed>(
            await tracer.TraceAsync(path, word, CancellationToken.None));
        Assert.True(outcome.Details!.SearchCompleted, word);
        return outcome.Document!.Analyses.Select(analysis => analysis.Morphs
            .Select(morph => CanonicalId.FromGuid(Guid.Parse(morph.FormId!))).ToArray()).ToList();
    }

    private static (CanonicalId Entry, CanonicalId Form) Suffix(LcmCache cache, string form, CanonicalId slot, CanonicalId category)
    {
        var proposal = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent(form,
            Vern(cache), MorphType(MoMorphTypeTags.kguidMorphSuffix), form, Anal(cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, category, Slots: [slot]))));
        return (Created(proposal, LexDbEntriesOperationKinds.Create),
            Created(proposal, LexEntryLexemeFormOperationKinds.CreateLexemeForm));
    }

    private static CanonicalId AlternateSuffix(LcmCache cache, CanonicalId entry, string form) =>
        Created(ApplyOps(cache, AuthorAlternateFormComposer.Build(cache, new AuthorAlternateFormIntent(
            entry, form, Vern(cache), MorphType(MoMorphTypeTags.kguidMorphSuffix)))),
            LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms);
}
