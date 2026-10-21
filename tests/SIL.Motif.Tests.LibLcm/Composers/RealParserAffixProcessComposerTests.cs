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
/// Whether affix processes authored by the composer and applied as a Proposal are the ones the real parser uses:
/// an infix process, natural-class and explicit-segment partial copy, and whole-stem copy with a separator.
/// The stems are synthetic; this is a parser example, not a description of Tagalog.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserAffixProcessComposerTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    private static readonly string[] Letters =
        ["k", "a", "m", "o", "t", "b", "p", "s", "y", "l", "h", "i", "n", "g", "-"];

    [RealParserFact]
    public async Task ComposerAuthoredInfixAndCopyProcessesParseTheApprovedFormsAndRejectTheControls()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, Letters);
        var panel = Author(cache);
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);

        var expected = new Dictionary<string, string>
        {
            ["kinamot"] = "IN+kamot", ["kinakamot"] = "IN+CV+kamot", ["magtatakbo"] = "mag+CV+takbo",
            ["magpasyal-pasyal"] = "mag+FULL+pasyal", ["naghihilik"] = "nag+CV+hilik", ["sinalpok"] = "IN+salpok",
            ["tinakasan"] = "IN+takas+an", ["tataat"] = "CV+taat", ["ayat"] = "ayat",
        };
        foreach (var (word, reading) in expected)
            Assert.Equal([reading], await Readings(tracer, path, word, panel));
        foreach (var word in new[] { "ikinamot", "kainmot", "taataat", "inayat" })
            Assert.Empty(await Readings(tracer, path, word, panel));

        var processForms = panel.Names.Where(item => item.Value is "IN" or "CV" or "FULL").Select(item => item.Key);
        foreach (var form in processForms)
            Assert.IsAssignableFrom<IMoAffixProcess>(
                cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(form.ToGuid()));
        Assert.DoesNotContain(cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances(),
            entry => entry.LexemeFormOA is IMoAffixAllomorph { MorphTypeRA.Guid: var type } &&
                     type == MoMorphTypeTags.kguidMorphInfix);
    }

    [RealParserFact]
    public async Task ExplicitSegmentInputsCopyThePrefixOfAStemThroughTheComposer()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "t", "a");
        var category = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Synthetic word", Anal(cache), "W"))),
            CmPossibilityListPossibilitiesOperationKinds.Create);
        var phonemes = Phonemes(cache);
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid)
        {
            [Form(cache, ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent("taat",
                Vern(cache), CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem), "taat", Anal(cache),
                new LexicalEntryMsa(LexicalEntryMsaKind.Stem, category)))))] = "taat",
            [Process(cache, MoMorphTypeTags.kguidMorphPrefix, category,
                [new("t", Phoneme: phonemes["t"]), new("a", Phoneme: phonemes["a"]), new("rest", Remaining: true)],
                [new(Copy: "t"), new(Copy: "a"), new(Copy: "t"), new(Copy: "a"), new(Copy: "rest")])] = "SEG",
        };
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        var panel = new Panel(names);

        Assert.Equal(["SEG+taat"], await Readings(tracer, path, "tataat", panel));
        Assert.Equal(["taat"], await Readings(tracer, path, "taat", panel));
        Assert.Empty(await Readings(tracer, path, "taataat", panel));
    }

    [RealParserFact]
    public async Task NasalSubstitutionProcessesDeleteTheStemInitialConsonantAndParseTheDerivedForms()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "p", "a", "l", "i", "t", "b", "n", "m", "g");
        var category = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Synthetic word", Anal(cache), "W"))),
            CmPossibilityListPossibilitiesOperationKinds.Create);
        var phonemes = Phonemes(cache);
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        foreach (var stem in new[] { "palit", "tabang" })
            names[Form(cache, ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent(stem,
                Vern(cache), CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem), stem, Anal(cache),
                new LexicalEntryMsa(LexicalEntryMsaKind.Stem, category)))))] = stem;
        names[Process(cache, MoMorphTypeTags.kguidMorphPrefix, category,
            [new("c", Phoneme: phonemes["p"]), new("rest", Remaining: true)],
            [new(Insert: [phonemes["p"], phonemes["a"], phonemes["m"]]), new(Copy: "rest")])] = "paM";
        names[Process(cache, MoMorphTypeTags.kguidMorphPrefix, category,
            [new("c", Phoneme: phonemes["t"]), new("rest", Remaining: true)],
            [new(Insert: [phonemes["p"], phonemes["a"], phonemes["n"]]), new(Copy: "rest")])] = "paN";
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        var panel = new Panel(names);

        Assert.Equal(["paM+palit"], await Readings(tracer, path, "pamalit", panel));
        Assert.Equal(["paN+tabang"], await Readings(tracer, path, "panabang", panel));
        Assert.Equal(["palit"], await Readings(tracer, path, "palit", panel));
        foreach (var control in new[] { "pampalit", "pantabang", "pamabang", "panalit" })
            Assert.Empty(await Readings(tracer, path, control, panel));
    }

    private sealed record Panel(IReadOnlyDictionary<CanonicalId, string> Names);

    private async Task<string[]> Readings(PanGlossTracer tracer, string path, string word, Panel panel)
    {
        var outcome = Assert.IsType<PanGlossTraceOutcome.Completed>(
            await tracer.TraceAsync(path, word, CancellationToken.None));
        var details = outcome.Details!;
        Assert.True(details.SearchCompleted, word);
        Assert.False(details.Capped || details.TimedOut || details.InvalidShape, word);
        var readings = outcome.Document!.Analyses.Select(analysis => string.Join("+", analysis.Morphs.Select(morph =>
            panel.Names.TryGetValue(CanonicalId.FromGuid(Guid.Parse(morph.FormId!)), out var name) ? name : "?" + morph.Form)))
            .Distinct().ToArray();
        output.WriteLine($"{word}: {(readings.Length == 0 ? "no analysis" : string.Join(" | ", readings))}");
        return readings;
    }

    private static Panel Author(LcmCache cache)
    {
        var ws = Anal(cache);
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        var category = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Synthetic word", ws, "W"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var phonemes = Phonemes(cache);
        var consonants = Created(ApplyOps(cache, AuthorNaturalClassComposer.Build(cache, new AuthorNaturalClassIntent(
            "Consonants", "C", "kmtbpsylhng".Select(c => phonemes[c.ToString()]).ToArray()))),
            PhPhonDataNaturalClassesOperationKinds.Create);
        var vowels = Created(ApplyOps(cache, AuthorNaturalClassComposer.Build(cache, new AuthorNaturalClassIntent(
            "Vowels", "V", "aoi".Select(c => phonemes[c.ToString()]).ToArray()))), PhPhonDataNaturalClassesOperationKinds.Create);

        foreach (var stem in new[] { "kamot", "takbo", "pasyal", "hilik", "salpok", "takas", "taat", "ayat" })
            names[Form(cache, ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent(
                stem, Vern(cache), CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem), stem, ws,
                new LexicalEntryMsa(LexicalEntryMsaKind.Stem, category)))))] = stem;
        names[Affix(cache, MoMorphTypeTags.kguidMorphPrefix, "mag", category)] = "mag";
        names[Affix(cache, MoMorphTypeTags.kguidMorphPrefix, "nag", category)] = "nag";
        names[Affix(cache, MoMorphTypeTags.kguidMorphSuffix, "an", category)] = "an";

        names[Process(cache, MoMorphTypeTags.kguidMorphInfix, category,
            [new("c", NaturalClass: consonants), new("rest", Remaining: true)],
            [new(Copy: "c"), new(Insert: [phonemes["i"], phonemes["n"]]), new(Copy: "rest")])] = "IN";
        names[Process(cache, MoMorphTypeTags.kguidMorphPrefix, category,
            [new("c", NaturalClass: consonants), new("v", NaturalClass: vowels), new("rest", Remaining: true)],
            [new(Copy: "c"), new(Copy: "v"), new(Copy: "c"), new(Copy: "v"), new(Copy: "rest")])] = "CV";
        names[Process(cache, MoMorphTypeTags.kguidMorphPrefix, category,
            [new("rest", Remaining: true)],
            [new(Copy: "rest"), new(Insert: [phonemes["-"]]), new(Copy: "rest")])] = "FULL";
        return new Panel(names);
    }

    private static CanonicalId Form(LcmCache cache, Proposal proposal) =>
        Created(proposal, LexEntryLexemeFormOperationKinds.CreateLexemeForm);

    private static CanonicalId Affix(LcmCache cache, Guid morphType, string form, CanonicalId category) =>
        Form(cache, ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent(form,
            Vern(cache), CanonicalId.FromGuid(morphType), form, Anal(cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Unclassified, category)))));

    private static CanonicalId Process(LcmCache cache, Guid morphType, CanonicalId category,
        IReadOnlyList<AffixProcessInputPart> input, IReadOnlyList<AffixProcessOutputStep> output) =>
        Form(cache, ApplyOps(cache, AuthorAffixProcessComposer.Build(cache, new AuthorAffixProcessIntent(
            CanonicalId.FromGuid(morphType), "process", Anal(cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Unclassified, category), input, output))));

    private static Dictionary<string, CanonicalId> Phonemes(LcmCache cache) =>
        cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].PhonemesOC
            .SelectMany(phoneme => phoneme.CodesOS.Select(code =>
                (Text: code.Representation.get_String(cache.DefaultVernWs)?.Text, Phoneme: phoneme)))
            .Where(item => item.Text is not null)
            .ToDictionary(item => item.Text!, item => CanonicalId.FromGuid(item.Phoneme.Guid), StringComparer.Ordinal);
}
