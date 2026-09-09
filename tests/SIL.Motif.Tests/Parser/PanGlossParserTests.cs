using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Parser;

/// <summary>The batch pass is now a reading of one invocation's outcome; nothing here starts a process.</summary>
public sealed class PanGlossParserTests
{
    private static readonly string Project = Path.GetTempFileName();

    [Fact]
    public async Task ACompletedBatchBecomesAnAnalysis_WithWarningsFromStandardError()
    {
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(
                "0\tmotifa\t12\tok\tsig\n1\tzzz\t7\tnone\t-\n", "warning: one thing\nnoise\n", TimeSpan.FromSeconds(1)),
        };
        var parser = new PanGlossParser(invoker);

        var result = await parser.AnalyseBatchAsync(Project, ["motifa", "zzz"], ParserEngine.FstPrunedByHermitCrab,
            TimeSpan.FromMilliseconds(1500), "test", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Analysis!.Words.Count);
        Assert.Equal(1500, result.Analysis.PerWordTimeoutMs);
        Assert.Equal(["warning: one thing"], result.Analysis.Warnings);
        var request = Assert.IsType<PanGlossRequest.Batch>(Assert.Single(invoker.Requests).Request);
        Assert.Null(request.StatsCachePath);
    }

    [Fact]
    public async Task ARefusedRunWithARecognisedFstRefusal_ReturnsTheRefusal()
    {
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Refused(1,
                "error: foma compile failed for this grammar", string.Empty, "pangloss batch exited 1"),
        };

        var result = await new PanGlossParser(invoker).AnalyseBatchAsync(Project, ["x"],
            ParserEngine.FstPrunedByHermitCrab, TimeSpan.FromSeconds(1), "test", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Refusal);
    }

    [Fact]
    public async Task AnyOtherOutcome_IsReturnedAsIs_NeverThrown()
    {
        var invoker = new FakeInvoker { Respond = _ => new PanGlossOutcome.Unavailable("no parser") };

        var result = await new PanGlossParser(invoker).AnalyseBatchAsync(Project, ["x"],
            ParserEngine.FstPrunedByHermitCrab, TimeSpan.FromSeconds(1), "test", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Refusal);
        Assert.IsType<PanGlossOutcome.Unavailable>(result.Outcome);
    }
}
