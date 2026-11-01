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
/// FieldWorks' affix template has no stem-class field the parser reads, so a template for one stem class is made by
/// restricting every affix in its required slot to that class: a stem of the other class finds no complete template.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserTemplatePerStemClassTests(PristineProjectFixture pristine)
{
    [RealParserFact]
    public async Task ATemplateWhoseAffixesAreRestrictedToOneClassOnlyCompletesForStemsOfThatClass()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "k", "a", "t", "m", "o", "r", "s", "u");
        var ws = Anal(cache);
        var verb = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Verb", ws, "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var classA = AddInflectionClass(cache, verb, "Class A");
        var classB = AddInflectionClass(cache, verb, "Class B");
        var slots = ApplyOps(cache, [
            .. AuthorAffixSlotComposer.Build(cache, new AuthorAffixSlotIntent(verb, "SLOT-A", ws, false)),
            .. AuthorAffixSlotComposer.Build(cache, new AuthorAffixSlotIntent(verb, "SLOT-B", ws, false))])
            .Operations.Where(op => op.Kind == PartOfSpeechAffixSlotsOperationKinds.Create)
            .Select(op => op.EntityId!.Value).ToArray();
        ApplyOps(cache, AuthorAffixTemplateComposer.Build(cache,
            new AuthorAffixTemplateIntent(verb, "Class A template", ws, [], [slots[0]], true)));
        ApplyOps(cache, AuthorAffixTemplateComposer.Build(cache,
            new AuthorAffixTemplateIntent(verb, "Class B template", ws, [], [slots[1]], true)));

        var kat = AddStem(cache, "kat", verb, classA);
        var mot = AddStem(cache, "mot", verb, classB);
        var ra = Suffix(cache, "ra", slots[0], verb, [classA]);
        var su = Suffix(cache, "su", slots[1], verb, [classB]);
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);

        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        Assert.Equal([[kat.Form, ra]], await Readings(tracer, path, "katra"));
        Assert.Equal([[mot.Form, su]], await Readings(tracer, path, "motsu"));
        Assert.Empty(await Readings(tracer, path, "katsu"));
        Assert.Empty(await Readings(tracer, path, "motra"));
    }

    private static async Task<List<CanonicalId[]>> Readings(PanGlossTracer tracer, string path, string word)
    {
        var outcome = Assert.IsType<PanGlossTraceOutcome.Completed>(
            await tracer.TraceAsync(path, word, CancellationToken.None));
        Assert.True(outcome.Details!.SearchCompleted, word);
        return outcome.Document!.Analyses.Select(analysis => analysis.Morphs
            .Select(morph => CanonicalId.FromGuid(Guid.Parse(morph.FormId!))).ToArray()).ToList();
    }

    private static CanonicalId Suffix(LcmCache cache, string form, CanonicalId slot, CanonicalId category,
        IReadOnlyList<CanonicalId> classes) =>
        Created(ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent(form,
            Vern(cache), MorphType(MoMorphTypeTags.kguidMorphSuffix), form, Anal(cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, category, Slots: [slot]), null, classes))),
            LexEntryLexemeFormOperationKinds.CreateLexemeForm);
}
