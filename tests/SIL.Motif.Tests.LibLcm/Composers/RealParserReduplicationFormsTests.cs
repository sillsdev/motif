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
/// Whether one reduplication entry holding a solid full-copy process and a hyphen-separated full-copy process
/// parses both spellings, and whether the same two forms as two entries read the same. The language is synthetic
/// and Swahili-shaped (whole-stem copying of a verb stem, as in a repeated stem).
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserReduplicationFormsTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    private static readonly string[] Letters = ["p", "l", "e", "o", "m", "a", "k", "u", "n", "i", "-"];

    [RealParserFact]
    public async Task SolidAndHyphenFullCopyFormsInOneEntryBothParseAndNeitherBlocksTheOther()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, Letters);
        var ws = Anal(cache);
        var verb = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Verb", ws, "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var p = Phonemes(cache);
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        foreach (var stem in new[] { "pole", "kaun", "mapo" })
            names[AddStem(cache, stem, verb).Form] = stem;
        var created = ApplyOps(cache, AuthorAffixProcessComposer.Build(cache, new AuthorAffixProcessIntent(
            MorphType(MoMorphTypeTags.kguidMorphPrefix), "red", ws,
            new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, verb, verb),
            [new("rest", Remaining: true)], [new(Copy: "rest"), new(Copy: "rest")])));
        var entry = Created(created, LexDbEntriesOperationKinds.Create);
        names[Created(created, LexEntryLexemeFormOperationKinds.CreateLexemeForm)] = "red";
        var hyphen = ApplyOps(cache, AuthorAffixProcessAllomorphComposer.Build(cache, new AuthorAffixProcessAllomorphIntent(
            entry, [new("rest", Remaining: true)], [new(Copy: "rest"), new(Insert: [p["-"]]), new(Copy: "rest")])));
        names[Created(hyphen, LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms)] = "red";
        Assert.Single(Get<ILexEntry>(cache, entry).AlternateFormsOS);

        output.WriteLine("two forms, one entry");
        var solid = await Parse(cache, names, "polepole");
        var hyphenated = await Parse(cache, names, "pole-pole");
        Assert.Equal(["red+pole"], solid);
        Assert.Equal(["red+pole"], hyphenated);
        Assert.Equal(["red+kaun"], await Parse(cache, names, "kaunkaun"));
        Assert.Equal(["red+kaun"], await Parse(cache, names, "kaun-kaun"));
        Assert.Empty(await Parse(cache, names, "pole-kaun"));
        Assert.Empty(await Parse(cache, names, "polekaun"));
    }

    [RealParserFact]
    public async Task SolidAndHyphenFullCopyFormsAsTwoEntriesReadTheSameAsOneEntry()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, Letters);
        var ws = Anal(cache);
        var verb = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Verb", ws, "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var p = Phonemes(cache);
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        foreach (var stem in new[] { "pole", "kaun" })
            names[AddStem(cache, stem, verb).Form] = stem;
        foreach (var (label, realization) in new (string, AffixProcessOutputStep[])[]
                 {
                     ("red-solid", [new(Copy: "rest"), new(Copy: "rest")]),
                     ("red-hyphen", [new(Copy: "rest"), new(Insert: [p["-"]]), new(Copy: "rest")]),
                 })
        {
            var created = ApplyOps(cache, AuthorAffixProcessComposer.Build(cache, new AuthorAffixProcessIntent(
                MorphType(MoMorphTypeTags.kguidMorphPrefix), label, ws,
                new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, verb, verb),
                [new("rest", Remaining: true)], realization)));
            names[Created(created, LexEntryLexemeFormOperationKinds.CreateLexemeForm)] = label;
        }

        Assert.Equal(["red-solid+pole"], await Parse(cache, names, "polepole"));
        Assert.Equal(["red-hyphen+pole"], await Parse(cache, names, "pole-pole"));
        Assert.Equal(["red-solid+kaun"], await Parse(cache, names, "kaunkaun"));
        Assert.Equal(["red-hyphen+kaun"], await Parse(cache, names, "kaun-kaun"));
    }

    private async Task<string[]> Parse(LcmCache cache, IReadOnlyDictionary<CanonicalId, string> names, string word)
    {
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        return await Readings(new PanGlossTracer(invoker), cache.ProjectId.Path, word, names);
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

    private static Dictionary<string, CanonicalId> Phonemes(LcmCache cache) =>
        cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].PhonemesOC
            .SelectMany(phoneme => phoneme.CodesOS.Select(code =>
                (Text: code.Representation.get_String(cache.DefaultVernWs)?.Text, Phoneme: phoneme)))
            .Where(item => item.Text is not null)
            .ToDictionary(item => item.Text!, item => CanonicalId.FromGuid(item.Phoneme.Guid), StringComparer.Ordinal);
}
