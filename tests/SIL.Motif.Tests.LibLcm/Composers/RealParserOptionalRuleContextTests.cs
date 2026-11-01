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
/// Whether a regular rule authored with no left context, no right context, or neither (LibLCM and FieldWorks' HCLoader
/// both leave the absent side as no constraint) is the rule the real parser runs. The sounds are synthetic: n becomes m.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserOptionalRuleContextTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    [RealParserFact]
    public async Task ARuleWithOnlyARightContextChangesNBeforeAAndNothingElse()
    {
        var (path, names, tracer) = await Setup(["na", "nt"], phonemes =>
            ([new(Phoneme: phonemes["n"])], [new(Phoneme: phonemes["m"])], [], [new(Phoneme: phonemes["a"])]));
        using var invoker = tracer.Invoker;
        Assert.Equal(["na"], await Readings(tracer.Tracer, path, "ma", names));
        Assert.Equal(["nt"], await Readings(tracer.Tracer, path, "nt", names));
        foreach (var control in new[] { "na", "mt" })
            Assert.Empty(await Readings(tracer.Tracer, path, control, names));
    }

    [RealParserFact]
    public async Task ARuleWithOnlyALeftContextChangesNAfterTAndNothingElse()
    {
        var (path, names, tracer) = await Setup(["tn", "an"], phonemes =>
            ([new(Phoneme: phonemes["n"])], [new(Phoneme: phonemes["m"])], [new(Phoneme: phonemes["t"])], []));
        using var invoker = tracer.Invoker;
        Assert.Equal(["tn"], await Readings(tracer.Tracer, path, "tm", names));
        Assert.Equal(["an"], await Readings(tracer.Tracer, path, "an", names));
        foreach (var control in new[] { "tn", "am" })
            Assert.Empty(await Readings(tracer.Tracer, path, control, names));
    }

    [RealParserFact]
    public async Task ARuleWithNoContextAtAllChangesNEverywhere()
    {
        var (path, names, tracer) = await Setup(["tn", "na"], phonemes =>
            ([new(Phoneme: phonemes["n"])], [new(Phoneme: phonemes["m"])], [], []));
        using var invoker = tracer.Invoker;
        Assert.Equal(["tn"], await Readings(tracer.Tracer, path, "tm", names));
        Assert.Equal(["na"], await Readings(tracer.Tracer, path, "ma", names));
        foreach (var control in new[] { "tn", "na" })
            Assert.Empty(await Readings(tracer.Tracer, path, control, names));
    }

    private async Task<(string Path, Dictionary<CanonicalId, string> Names, (PanGlossInvoker Invoker, PanGlossTracer Tracer) Tracer)>
        Setup(string[] stems, Func<Dictionary<string, CanonicalId>, (PhonologicalRuleItem[] Input,
            PhonologicalRuleItem[] Output, EnvironmentContext[] Left, EnvironmentContext[] Right)> rule)
    {
        var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache);
        var phonemes = new Dictionary<string, CanonicalId>();
        foreach (var letter in new[] { "n", "m", "t", "a" })
            phonemes.Add(letter, Created(ApplyOps(cache, AuthorPhonemeComposer.Build(cache, new(letter, [letter], []))),
                PhPhonemeSetPhonemesOperationKinds.Create));
        var category = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Synthetic word", Anal(cache), "W"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        foreach (var stem in stems)
            names[Created(ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent(stem, Vern(cache),
                CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem), stem, Anal(cache),
                new LexicalEntryMsa(LexicalEntryMsaKind.Stem, category)))), LexEntryLexemeFormOperationKinds.CreateLexemeForm)] = stem;
        var (input, output, left, right) = rule(phonemes);
        var operations = AuthorPhonologicalRuleComposer.Build(cache, new AuthorPhonologicalRuleIntent("n becomes m",
            PhonologicalRuleDirection.Simultaneous, input, output, left, right));
        ApplyOps(cache, operations);
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        cache.Dispose();
        var invoker = new PanGlossInvoker();
        return (path, names, (invoker, new PanGlossTracer(invoker)));
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
            .Distinct().Order().ToArray();
        output.WriteLine($"{word}: {(readings.Length == 0 ? "no analysis" : string.Join(" | ", readings))}");
        return readings;
    }
}
