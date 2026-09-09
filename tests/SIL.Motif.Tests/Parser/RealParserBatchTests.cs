using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Parser;

[Collection(LcmCacheTestCollection.Name)]
public sealed class RealParserBatchTests(PristineProjectFixture pristine)
{
    [RealParserFact]
    public async Task RuntimeSeededStemsParseAndASegmentableUnknownWordHasNoAnalysis()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "m", "o", "t", "i", "f", "a", "b");

        string[] words = [SeededProject.FirstForm, SeededProject.SecondForm, "mofita"];
        using var invoker = new PanGlossInvoker();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Batch(cache.ProjectId.Path, words, TimeSpan.FromSeconds(1)),
            "test:runtime-seeded-stems", cancellation.Token, wallClockCap: TimeSpan.FromSeconds(30));

        Assert.True(outcome is PanGlossOutcome.Completed, $"Expected a completed batch, received {outcome}.");
        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
        var results = BatchTsvParser.Parse(completed.Output);

        Assert.Equal(words, results.Select(result => result.Word));
        Assert.Collection(results,
            first => Assert.Equal(WordOutcome.Analysed, first.Outcome),
            second => Assert.Equal(WordOutcome.Analysed, second.Outcome),
            unknown => Assert.Equal(WordOutcome.NoAnalysis, unknown.Outcome));
    }
}
