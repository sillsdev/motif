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

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserMultiItemRuleTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    [RealParserFact]
    public Task ASinglePhonemeRuleChangesTheSurface() => Prove("na", ["n"], ["m"], "ma", ["na"]);

    [RealParserFact]
    public Task ATwoPhonemeInputBecomesATwoPhonemeOutput() =>
        Prove("nta", ["n", "t"], ["m", "d"], "mda", ["nta", "nda", "mta"]);

    [RealParserFact]
    public Task ATwoPhonemeInputBecomesOnePhoneme() => Prove("nka", ["n", "k"], ["n"], "na", ["nka"]);

    [RealParserFact]
    public Task AOnePhonemeInputBecomesTwoPhonemes() => Prove("ta", ["t"], ["n", "k"], "nka", ["ta"]);

    [RealParserFact]
    public async Task ANaturalClassInTheInputSequenceMatchesEveryMember()
    {
        await Prove("nat", ["n", "V"], ["m"], "mt", ["nat", "mat"]);
        await Prove("nit", ["n", "V"], ["m"], "mt", ["nit", "mit"]);
    }

    [RealParserFact]
    public Task ANaturalClassOutputProducesAnyMemberOfTheClass() =>
        Prove("nat", ["n", "V"], ["m", "V"], "mat", ["nat", "mt"], alsoAccepted: ["mit"]);

    [RealParserFact]
    public Task ASequenceRuleChecksBothEnvironments() =>
        Prove("anta", ["n", "t"], ["m", "d"], "amda", ["anta", "amta", "anda", "imda", "amdi"],
            left: ["a"], right: ["a"], unchanged: ["inta", "anti"]);

    [RealParserFact]
    public Task ALeftToRightSequenceRuleStartsAtTheLeftmostOverlap() =>
        Prove("ttt", ["t", "t"], ["t", "d"], "tdt", ["ttt", "ttd", "tdd"],
            direction: PhonologicalRuleDirection.LeftToRightIterative);

    [RealParserFact]
    public Task ARightToLeftSequenceRuleStartsAtTheRightmostOverlap() =>
        Prove("ttt", ["t", "t"], ["t", "d"], "ttd", ["ttt", "tdt", "tdd"],
            direction: PhonologicalRuleDirection.RightToLeftIterative);

    [RealParserFact]
    public Task ASimultaneousSequenceRuleHandlesOverlappingMatches() =>
        Prove("ttt", ["t", "t"], ["t", "d"], "ttd", ["ttt", "tdt", "tdd"]);

    private async Task Prove(string stem, string[] input, string[] replacement, string surface, string[] rejected,
        string[]? alsoAccepted = null, string[]? left = null, string[]? right = null,
        PhonologicalRuleDirection direction = PhonologicalRuleDirection.Simultaneous, string[]? unchanged = null)
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "n", "m", "t", "d", "k", "a", "i");
        var category = AddCategory(cache, "Synthetic word");
        var form = AddStem(cache, stem, category).Form;
        var unmodified = (unchanged ?? []).Select(word => (Word: word, Form: AddStem(cache, word, category).Form)).ToArray();
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        Assert.Empty(await Readings(surface));
        Assert.Equal([form.Value], await Readings(stem));

        CanonicalId Phoneme(string letter) => Id(cache.LangProject.PhonologicalDataOA
            .PhonemeSetsOS[0].PhonemesOC.Single(phoneme =>
                phoneme.CodesOS.Any(code => code.Representation.get_String(cache.DefaultVernWs).Text == letter)));
        var vowels = input.Concat(replacement).Contains("V")
            ? Created(ApplyOps(cache, AuthorNaturalClassComposer.Build(cache,
                new("Vowels", "V", [Phoneme("a"), Phoneme("i")]))), PhPhonDataNaturalClassesOperationKinds.Create)
            : (CanonicalId?)null;
        PhonologicalRuleItem Item(string letter) => letter == "V" ? new(NaturalClass: vowels) : new(Phoneme: Phoneme(letter));
        ApplyOps(cache, AuthorPhonologicalRuleComposer.Build(cache, new("Sequence rewrite",
            direction, input.Select(Item).ToArray(), replacement.Select(Item).ToArray(),
            (left ?? []).Select(letter => new EnvironmentContext(Phoneme: Phoneme(letter))).ToArray(),
            (right ?? []).Select(letter => new EnvironmentContext(Phoneme: Phoneme(letter))).ToArray())));
        new FwDataProjectLoader().Save(cache);

        Assert.Equal([form.Value], await Readings(surface));
        foreach (var word in alsoAccepted ?? []) Assert.Equal([form.Value], await Readings(word));
        foreach (var control in unmodified) Assert.Equal([control.Form.Value], await Readings(control.Word));
        foreach (var word in rejected) Assert.Empty(await Readings(word));

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
