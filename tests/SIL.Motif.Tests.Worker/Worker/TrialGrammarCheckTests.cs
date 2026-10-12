using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Jobs;
using Xunit;

namespace SIL.Motif.Tests.Worker;

[Trait("MotifTestLevel", "Unit")]
public sealed class TrialGrammarCheckTests
{
    [Theory]
    [InlineData(StoredAnalysisComparison.Budget)]
    [InlineData(StoredAnalysisComparison.All)]
    [InlineData(StoredAnalysisComparison.Off)]
    public async Task AGrammarCheckForATrialRunsWithTheChoiceItWasGiven(StoredAnalysisComparison mode)
    {
        var invoker = Answering();

        var response = await TrialGrammarCheck.RunAsync(invoker, "trial-grammar:test", mode, "scratch.fwdata",
            "Scratch", CancellationToken.None);

        Assert.NotEmpty(response!.Findings);
        var (request, label) = Assert.Single(invoker.Requests);
        Assert.Equal("trial-grammar:test", label);
        var health = Assert.IsType<PanGlossRequest.GrammarHealth>(request);
        Assert.Equal(mode, health.StoredAnalyses);
        Assert.Equal("scratch.fwdata", health.GrammarPath);
    }

    private static FakeInvoker Answering()
    {
        var report = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestFixtures", "GrammarHealth",
            "schema-v4-producer.json"));
        return new FakeInvoker { Respond = _ => new PanGlossOutcome.Completed(report, string.Empty, TimeSpan.Zero) };
    }
}
