using System.Text.Json;
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
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>
/// The template and slot editor against the pinned PanGloss: each edit changes the parse the way a linguist expects.
/// The forms are synthetic and stand for no language.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserTemplateSlotEditorTests(PristineProjectFixture pristine)
{
    [RealParserFact]
    public async Task AnInfixInItsOwnSlotParsesOnlyOnceItsAffixIsMovedIntoTheTemplateSlot()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "k", "a", "m", "o", "t", "i", "n");
        var ws = Anal(cache);
        var verb = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Verb", ws, "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var phonemes = Phonemes(cache);
        var consonants = Created(ApplyOps(cache, AuthorNaturalClassComposer.Build(cache, new AuthorNaturalClassIntent(
            "Consonants", "C", "kmt".Select(c => phonemes[c.ToString()]).ToArray()))),
            PhPhonDataNaturalClassesOperationKinds.Create);
        var slot = SlotId(cache, verb, "INFIX-SLOT", optional: true);
        var spare = SlotId(cache, verb, "SPARE", optional: true);
        ApplyOps(cache, AuthorAffixTemplateComposer.Build(cache,
            new AuthorAffixTemplateIntent(verb, "Verb template", ws, [], [slot], true)));
        var stem = AddStem(cache, "kamot", verb);
        var process = ApplyOps(cache, AuthorAffixProcessComposer.Build(cache, new AuthorAffixProcessIntent(
            CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphInfix), "infix", ws,
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, verb, Slots: [spare]),
            [new("c", NaturalClass: consonants), new("rest", Remaining: true)],
            [new(Copy: "c"), new(Insert: [phonemes["i"], phonemes["n"]]), new(Copy: "rest")])));
        var infixForm = Created(process, LexEntryLexemeFormOperationKinds.CreateLexemeForm);
        var infixMsa = Created(process, LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
        var path = Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);

        // The infix is in a slot that no template lists, so it does not complete a word.
        Assert.Equal([[stem.Form]], await Readings(tracer, path, "kamot"));
        Assert.Empty(await Readings(tracer, path, "kinamot"));

        ApplyOps(cache, EditInflectionalAffixComposer.Build(cache,
            new EditInflectionalAffixIntent(infixMsa, [spare], [slot])));
        path = Save(cache);
        Assert.Equal([[infixForm, stem.Form]], await Readings(tracer, path, "kinamot"));
        Assert.Equal([[stem.Form]], await Readings(tracer, path, "kamot"));
        Assert.Empty(await Readings(tracer, path, "kinamo"));
    }

    [RealParserFact]
    public async Task KaInsideANasalPrefixParsesInThatOrderAfterTheMoveAndTheReorder()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "k", "a", "t", "u", "b", "n", "m");
        var ws = Anal(cache);
        var verb = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Verb", ws, "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var nasalSlot = SlotId(cache, verb, "NASAL", optional: true);
        var kaSlot = SlotId(cache, verb, "KA", optional: true);
        var parkedSlot = SlotId(cache, verb, "PARKED", optional: true);
        var unusedSlot = SlotId(cache, verb, "UNUSED", optional: true);
        var template = Created(ApplyOps(cache, AuthorAffixTemplateComposer.Build(cache,
            new AuthorAffixTemplateIntent(verb, "Verb template", ws, [kaSlot, nasalSlot], [], true))),
            PartOfSpeechAffixTemplatesOperationKinds.Create);
        var stem = AddStem(cache, "tub", verb);
        var nan = Prefix(cache, "nan", verb, nasalSlot);
        var ka = Prefix(cache, "ka", verb, parkedSlot);
        var path = Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);

        // Start: ka sits in a slot no template lists, so only the nasal prefix parses.
        Assert.Equal([[nan.Form, stem.Form]], await Readings(tracer, path, "nantub"));
        Assert.Empty(await Readings(tracer, path, "kantub"));
        Assert.Empty(await Readings(tracer, path, "nankatub"));

        // Move: ka's analysis leaves the parked slot for the KA slot, which the template lists before the nasal one.
        ApplyOps(cache, EditInflectionalAffixComposer.Build(cache,
            new EditInflectionalAffixIntent(ka.Msa, [parkedSlot], [kaSlot])));
        path = Save(cache);
        // The template lists prefix slots outermost first, so the ka prefix is now outside the nasal one.
        Assert.Equal([[ka.Form, nan.Form, stem.Form]], await Readings(tracer, path, "kanantub"));
        Assert.Empty(await Readings(tracer, path, "nankatub"));

        // Reorder: the template lists the two slots the other way round, which puts ka inside the nasal prefix.
        ApplyOps(cache, EditAffixTemplateComposer.Build(cache, new EditAffixTemplateIntent(template,
            [kaSlot, nasalSlot], [nasalSlot, kaSlot], [], [])));
        path = Save(cache);
        Assert.Equal([[nan.Form, ka.Form, stem.Form]], await Readings(tracer, path, "nankatub"));
        Assert.Empty(await Readings(tracer, path, "kanantub"));
        Assert.Equal([[nan.Form, stem.Form]], await Readings(tracer, path, "nantub"));
        Assert.Equal([[ka.Form, stem.Form]], await Readings(tracer, path, "katub"));
        Assert.Empty(await Readings(tracer, path, "nanmtub"));

        // Rename: the label changes and the parse does not.
        var rename = EditAffixSlotComposer.Build(cache, new EditAffixSlotIntent(kaSlot, null, null,
            new EditAffixSlotRename(ws, "KA", "Applicative")));
        ApplyOps(cache, rename);
        Assert.Equal("Applicative", Get<IMoInflAffixSlot>(cache, kaSlot).Name.get_String(cache.DefaultAnalWs).Text);
        path = Save(cache);
        Assert.Equal([[nan.Form, ka.Form, stem.Form]], await Readings(tracer, path, "nankatub"));

        // Retire: the unused slot goes at once; the KA slot is refused until its users are moved off.
        ApplyOps(cache, RetireAffixSlotComposer.Build(cache, new RetireAffixSlotIntent(unusedSlot)));
        Assert.False(Exists(cache, unusedSlot));
        var refusal = Assert.Throws<InvalidOperationException>(() =>
            RetireAffixSlotComposer.Build(cache, new RetireAffixSlotIntent(kaSlot)));
        Assert.Contains("1 inflectional affix analysis", refusal.Message);
        Assert.Contains("1 affix template", refusal.Message);
        ApplyOps(cache, EditInflectionalAffixComposer.Build(cache,
            new EditInflectionalAffixIntent(ka.Msa, [kaSlot], [parkedSlot])));
        ApplyOps(cache, EditAffixTemplateComposer.Build(cache, new EditAffixTemplateIntent(template,
            [nasalSlot, kaSlot], [nasalSlot], [], [])));
        ApplyOps(cache, RetireAffixSlotComposer.Build(cache, new RetireAffixSlotIntent(kaSlot)));
        Assert.False(Exists(cache, kaSlot));
        path = Save(cache);
        Assert.Empty(await Readings(tracer, path, "nankatub"));
        Assert.Equal([[nan.Form, stem.Form]], await Readings(tracer, path, "nantub"));
    }

    private static string Save(LcmCache cache)
    {
        new FwDataProjectLoader().Save(cache);
        return cache.ProjectId.Path;
    }

    private static CanonicalId SlotId(LcmCache cache, CanonicalId category, string name, bool optional) =>
        Created(ApplyOps(cache, AuthorAffixSlotComposer.Build(cache,
            new AuthorAffixSlotIntent(category, name, Anal(cache), optional))),
            PartOfSpeechAffixSlotsOperationKinds.Create);

    private static (CanonicalId Entry, CanonicalId Msa, CanonicalId Form) Prefix(
        LcmCache cache, string form, CanonicalId category, CanonicalId slot)
    {
        var proposal = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent(form,
            Vern(cache), MorphType(MoMorphTypeTags.kguidMorphPrefix), form, Anal(cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, category, Slots: [slot]))));
        return (Created(proposal, LexDbEntriesOperationKinds.Create),
            Created(proposal, LexEntryMorphoSyntaxAnalysesOperationKinds.Create),
            Created(proposal, LexEntryLexemeFormOperationKinds.CreateLexemeForm));
    }

    private static async Task<List<CanonicalId[]>> Readings(PanGlossTracer tracer, string path, string word)
    {
        var outcome = Assert.IsType<PanGlossTraceOutcome.Completed>(
            await tracer.TraceAsync(path, word, CancellationToken.None));
        Assert.True(outcome.Details!.SearchCompleted, word);
        return outcome.Document!.Analyses.Select(analysis => analysis.Morphs
            .Select(morph => CanonicalId.FromGuid(Guid.Parse(morph.FormId!))).ToArray()).ToList();
    }

    private static Dictionary<string, CanonicalId> Phonemes(LcmCache cache) =>
        cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].PhonemesOC
            .SelectMany(phoneme => phoneme.CodesOS.Select(code =>
                (Text: code.Representation.get_String(cache.DefaultVernWs)?.Text, Phoneme: phoneme)))
            .Where(item => item.Text is not null)
            .ToDictionary(item => item.Text!, item => CanonicalId.FromGuid(item.Phoneme.Guid), StringComparer.Ordinal);
}
