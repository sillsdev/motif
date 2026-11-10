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
/// A rule whose Output is a multi-member segment class applies only the features the members share, so the input
/// segment keeps its own value for every feature the members differ on (fieldworks-parser-conformance#38). With d
/// [-lat] and the Output {r,l}, ara parses and ala does not.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserMultiMemberOutputRuleTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    [RealParserFact]
    public async Task AFullySpecifiedInputKeepsItsOwnValueUnderAMultiMemberOutputClass()
    {
        Assert.Equal(["ara"], await Parsing(dLateral: false));
        Assert.Equal(["ala"], await Parsing(dLateral: true));
    }

    private async Task<string[]> Parsing(bool? dLateral)
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "d", "r", "l", "a");
        var category = AddCategory(cache, "Synthetic word");
        AddStem(cache, "ada", category);
        CanonicalId Phoneme(string letter) => Id(cache.LangProject.PhonologicalDataOA
            .PhonemeSetsOS[0].PhonemesOC.Single(phoneme =>
                phoneme.CodesOS.Any(code => code.Representation.get_String(cache.DefaultVernWs).Text == letter)));
        var operations = AuthorPhonologicalFeatureComposer.Build(cache, new("lat", [new("minus"), new("plus")]));
        SoundSystemComposerTests.Execute(cache, operations);
        var lat = operations.First().EntityId!.Value;
        var values = operations.Where(o => o.Kind == FsClosedFeatureValuesOperationKinds.Create)
            .Select(o => new PhonologicalFeatureValue(lat, o.EntityId!.Value)).ToArray();
        foreach (var (letter, value) in new (string, PhonologicalFeatureValue?)[]
                 { ("r", values[0]), ("l", values[1]), ("d", dLateral is null ? null : values[dLateral.Value ? 1 : 0]) })
            if (value is not null)
                SoundSystemComposerTests.Execute(cache, EditPhonemeFeaturesComposer.Build(cache, new(Phoneme(letter), [value], [])));
        var liquids = Created(ApplyOps(cache, AuthorNaturalClassComposer.Build(cache,
            new("Liquids", "LQ", [Phoneme("r"), Phoneme("l")]))), PhPhonDataNaturalClassesOperationKinds.Create);
        ApplyOps(cache, AuthorPhonologicalRuleComposer.Build(cache, new("d to liquid",
            PhonologicalRuleDirection.Simultaneous, [new(Phoneme: Phoneme("d"))], [new(NaturalClass: liquids)],
            [new EnvironmentContext(Phoneme: Phoneme("a"))], [new EnvironmentContext(Phoneme: Phoneme("a"))])));
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);

        var parsed = new List<string>();
        foreach (var word in new[] { "ala", "ara" })
        {
            var outcome = Assert.IsType<PanGlossTraceOutcome.Completed>(
                await tracer.TraceAsync(cache.ProjectId.Path, word, CancellationToken.None));
            Assert.True(outcome.Details!.SearchCompleted, word);
            output.WriteLine($"{word}: {outcome.Document!.Analyses.Count} analyses");
            if (outcome.Document.Analyses.Count > 0) parsed.Add(word);
        }
        return [.. parsed];
    }
}
