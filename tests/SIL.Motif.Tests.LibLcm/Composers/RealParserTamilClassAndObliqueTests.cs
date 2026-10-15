using SIL.LCModel;
using SIL.Motif.Contract.Ids;
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
/// Whether the existing composers author two Tamil patterns in the real parser: same-morpheme past allomorphs
/// restricted to verb inflection classes, and an alternate noun stem confined to the position before a case suffix.
/// Forms are romanised; the routes are script-agnostic.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserTamilClassAndObliqueTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    [RealParserFact]
    public async Task PastAllomorphsOfOneMorphemeAreRestrictedToTheirVerbClasses()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "c", "e", "y", "n", "a", "t", "k", "i", "r");
        var ws = Anal(cache);
        var verb = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Tamil verb", ws, "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var classes = ApplyOps(cache, [
            .. AuthorInflectionClassComposer.Build(cache, new AuthorInflectionClassIntent(verb, "VI-1", ws, "VI-1")),
            .. AuthorInflectionClassComposer.Build(cache, new AuthorInflectionClassIntent(verb, "VI-12", ws, "VI-12"))])
            .Operations.Where(op => op.Kind == PartOfSpeechInflectionClassesOperationKinds.Create)
            .Select(op => op.EntityId!.Value).ToArray();
        var slots = ApplyOps(cache, [
            .. AuthorAffixSlotComposer.Build(cache, new AuthorAffixSlotIntent(verb, "TENSE", ws, false)),
            .. AuthorAffixSlotComposer.Build(cache, new AuthorAffixSlotIntent(verb, "PNG", ws, false))])
            .Operations.Where(op => op.Kind == PartOfSpeechAffixSlotsOperationKinds.Create)
            .Select(op => op.EntityId!.Value).ToArray();
        ApplyOps(cache, AuthorAffixTemplateComposer.Build(cache,
            new AuthorAffixTemplateIntent(verb, "Tamil verb template", ws, [], slots, true)));

        var ceyEntry = Stem(cache, "cey", verb, classes[0]);
        var nataEntry = Stem(cache, "nata", verb, classes[1]);
        var past = Affix(cache, "t", slots[0], verb, [classes[0]]);
        _ = Affix(cache, "kir", slots[0], verb, null);
        var png = Affix(cache, "aan", slots[1], verb, null);
        var alternate = ApplyOps(cache, AuthorAlternateFormComposer.Build(cache, new AuthorAlternateFormIntent(
            past.Entry, "nt", Vern(cache), CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphSuffix), null, [classes[1]])));
        var nt = Created(alternate, LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms);

        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);

        Assert.Equal([[ceyEntry, past.Form, png.Form]], await Readings(tracer, path, "ceytaan"));
        Assert.Equal([[nataEntry, nt, png.Form]], await Readings(tracer, path, "natantaan"));
        Assert.Empty(await Readings(tracer, path, "ceyntaan"));
        Assert.Empty(await Readings(tracer, path, "natataan"));
        Assert.Single(await Readings(tracer, path, "ceykiraan"));
        Assert.Single(await Readings(tracer, path, "natakiraan"));
    }

    [RealParserFact]
    public async Task AnAlternateStemFormBeforeAVowelBlocksTheCitationStemFromTakingCaseSuffixes()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "m", "a", "r", "t", "k", "l", "u", "i");
        var ws = Anal(cache);
        var noun = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Tamil noun", ws, "N"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var slot = Created(ApplyOps(cache, AuthorAffixSlotComposer.Build(cache,
            new AuthorAffixSlotIntent(noun, "CASE", ws, true))), PartOfSpeechAffixSlotsOperationKinds.Create);
        ApplyOps(cache, AuthorAffixTemplateComposer.Build(cache,
            new AuthorAffixTemplateIntent(noun, "Tamil noun template", ws, [], [slot], true)));

        var maram = StemEntry(cache, "maram", noun);
        foreach (var form in new[] { "ai", "aal", "ukku", "kal" }) _ = Affix(cache, form, slot, noun, null);
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);

        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        Assert.NotEmpty(await Readings(tracer, path, "maramai"));
        Assert.Empty(await Readings(tracer, path, "marattai"));
        var phonemes = SoundPhonemes(cache);
        var vowels = Created(ApplyOps(cache, AuthorNaturalClassComposer.Build(cache,
            new AuthorNaturalClassIntent("Vowels", "V", [phonemes["a"], phonemes["u"], phonemes["i"]]))),
            PhPhonDataNaturalClassesOperationKinds.Create);
        var beforeVowel = EnvironmentOf(cache, "Before vowel", new EnvironmentContext(NaturalClass: vowels));

        var oblique = Created(ApplyOps(cache, AuthorAlternateFormComposer.Build(cache, new AuthorAlternateFormIntent(
            maram.Entry, "maratt", Vern(cache), CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem), [beforeVowel]))),
            LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms);
        new FwDataProjectLoader().Save(cache);

        Assert.Equal([[maram.Form]], await Readings(tracer, path, "maram"));
        Assert.Equal([[maram.Form, Suffix(cache, "kal")]], await Readings(tracer, path, "maramkal"));
        foreach (var word in new[] { "marattai", "marattaal", "marattukku" })
            Assert.Equal(oblique, Assert.Single(await Readings(tracer, path, word))[0]);
        Assert.Empty(await Readings(tracer, path, "maramai"));
        Assert.Empty(await Readings(tracer, path, "maramaal"));
    }

    private async Task<List<CanonicalId[]>> Readings(PanGlossTracer tracer, string path, string word)
    {
        var outcome = Assert.IsType<PanGlossTraceOutcome.Completed>(
            await tracer.TraceAsync(path, word, CancellationToken.None));
        Assert.True(outcome.Details!.SearchCompleted, word);
        var readings = outcome.Document!.Analyses.Select(analysis => analysis.Morphs
            .Select(morph => CanonicalId.FromGuid(Guid.Parse(morph.FormId!))).ToArray()).ToList();
        output.WriteLine($"{word}: {(readings.Count == 0 ? "no reading" : string.Join(" | ", readings.Select(r => string.Join("+", r.Select(id => id.Value)))))}");
        return readings;
    }

    private static CanonicalId Stem(LcmCache cache, string form, CanonicalId category, CanonicalId inflectionClass) =>
        Created(ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent(form, Vern(cache),
            CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem), form, Anal(cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Stem, category, InflectionClass: inflectionClass)))),
            LexEntryLexemeFormOperationKinds.CreateLexemeForm);

    private static (CanonicalId Entry, CanonicalId Form) StemEntry(LcmCache cache, string form, CanonicalId category)
    {
        var proposal = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent(form,
            Vern(cache), CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem), form, Anal(cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Stem, category))));
        return (Created(proposal, LexDbEntriesOperationKinds.Create),
            Created(proposal, LexEntryLexemeFormOperationKinds.CreateLexemeForm));
    }

    private static (CanonicalId Entry, CanonicalId Form) Affix(LcmCache cache, string form, CanonicalId slot,
        CanonicalId category, IReadOnlyList<CanonicalId>? classes)
    {
        var proposal = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent(form,
            Vern(cache), CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphSuffix), form, Anal(cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, category, Slots: [slot]), null, classes)));
        return (Created(proposal, LexDbEntriesOperationKinds.Create),
            Created(proposal, LexEntryLexemeFormOperationKinds.CreateLexemeForm));
    }

    private static CanonicalId Suffix(LcmCache cache, string form) =>
        CanonicalId.FromGuid(cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances()
            .Select(entry => entry.LexemeFormOA)
            .First(lexeme => lexeme.Form.get_String(cache.DefaultVernWs).Text == form).Guid);

    private static CanonicalId EnvironmentOf(LcmCache cache, string name, EnvironmentContext right) =>
        Created(ApplyOps(cache, AuthorEnvironmentComposer.Build(cache, new AuthorEnvironmentIntent(name, [], [right]))),
            PhPhonDataEnvironmentsOperationKinds.Create);

    private static Dictionary<string, CanonicalId> SoundPhonemes(LcmCache cache) =>
        cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].PhonemesOC
            .SelectMany(phoneme => phoneme.CodesOS.Select(code =>
                (Text: code.Representation.get_String(cache.DefaultVernWs)?.Text, Phoneme: phoneme)))
            .Where(item => item.Text is not null)
            .ToDictionary(item => item.Text!, item => CanonicalId.FromGuid(item.Phoneme.Guid), StringComparer.Ordinal);
}
