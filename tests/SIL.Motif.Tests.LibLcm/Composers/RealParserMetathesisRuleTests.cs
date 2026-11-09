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

/// <summary>The Cebuano h/C case: a stem dahma surfaces as damha once h and a following consonant trade places.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserMetathesisRuleTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    [RealParserFact]
    public async Task AnHThenConsonantMetathesisRuleMakesDamhaParseAndRejectsDahma()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "d", "h", "m", "a");
        var category = AddCategory(cache, "Synthetic word");
        var form = AddStem(cache, "dahma", category).Form;
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        Assert.Empty(await Readings("damha"));
        Assert.Equal([form.Value], await Readings("dahma"));

        var phonemes = RealParserReadings.Phonemes(cache);
        var glottal = Created(ApplyOps(cache, AuthorNaturalClassComposer.Build(cache,
            new("H", "H", [phonemes["h"]]))), PhPhonDataNaturalClassesOperationKinds.Create);
        var consonants = Created(ApplyOps(cache, AuthorNaturalClassComposer.Build(cache,
            new("C", "C", [phonemes["d"], phonemes["m"]]))), PhPhonDataNaturalClassesOperationKinds.Create);
        ApplyOps(cache, AuthorMetathesisRuleComposer.Build(cache, new("h and C swap", PhonologicalRuleDirection.Simultaneous,
            [new(Phoneme: phonemes["a"])], new(NaturalClass: glottal), null, new(NaturalClass: consonants),
            [new(Phoneme: phonemes["a"])])));
        new FwDataProjectLoader().Save(cache);

        Assert.Equal([form.Value], await Readings("damha"));
        Assert.Empty(await Readings("dahma"));

        async Task<string[]> Readings(string word)
        {
            var outcome = Assert.IsType<PanGlossTraceOutcome.Completed>(
                await tracer.TraceAsync(cache.ProjectId.Path, word, CancellationToken.None));
            Assert.True(outcome.Details!.SearchCompleted, word);
            Assert.False(outcome.Details.Capped || outcome.Details.TimedOut || outcome.Details.InvalidShape, word);
            var readings = outcome.Document!.Analyses.Select(analysis =>
                string.Join("+", analysis.Morphs.Select(morph => CanonicalId.FromGuid(Guid.Parse(morph.FormId!)).Value)))
                .Distinct().Order(StringComparer.Ordinal).ToArray();
            output.WriteLine($"{word}: {string.Join(" | ", readings)}");
            return readings;
        }
    }
}
