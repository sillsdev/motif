using System.Text.Json.Nodes;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.Parser;

/// <summary>
/// A resident <c>pangloss serve</c> against the cold <c>parse</c> on the golden grammar: the same document, exit code
/// and trace outcome, with and without a step cap. It runs only when <c>MOTIF_PANGLOSS_SERVE_EXE</c> names a built parser.
/// </summary>
[Trait("MotifTestLevel", "System")]
public sealed class RealServeParityTests
{
    /// <summary>A built pangloss with serve; the warm-versus-cold check skips without it.</summary>
    private const string ServeExecutableVariable = "MOTIF_PANGLOSS_SERVE_EXE";

    private static readonly string[] WallClockFields = ["elapsedNs", "timedNs", "timingOverrunNs", "unattributedNs"];

    [ServeExecutableTheory]
    [InlineData("sagd", null)]
    [InlineData("sag", null)]
    [InlineData("zzz", null)]
    [InlineData("sagd", 1L)]
    public async Task AResidentServeAnswersLikeTheColdParseIncludingExitCodeAndStepCap(string word, long? cap)
    {
        var executable = Environment.GetEnvironmentVariable(ServeExecutableVariable)!;
        var grammarPath = Path.Combine(Path.GetTempPath(), "motif-real-serve-" + Guid.NewGuid().ToString("N") + ".xml");
        File.WriteAllText(grammarPath, RealParserTraceTests.GoldenGrammar);
        StepCap? stepCap = cap is { } steps ? new StepCap(steps) : null;
        using var pool = new WarmPanGlossPool(executable);
        using var invoker = new PanGlossInvoker(executable);
        try
        {
            var warm = Assert.IsType<WarmParseResult.Completed>(await pool.TryWordAsync(grammarPath, "t1", grammarPath, word,
                traceDetails: true, CancellationToken.None, stepCap));
            var cold = Assert.IsType<PanGlossOutcome.Completed>(await invoker.RunAsync(
                new PanGlossRequest.Trace(grammarPath, word) { StepLimit = stepCap }, "cold", CancellationToken.None,
                TimeSpan.FromSeconds(60)));

            Assert.Equal(0, warm.ExitCode);
            var expected = Normalize(JsonNode.Parse(cold.Output)!);
            var actual = Normalize(JsonNode.Parse(warm.Json)!);
            Assert.True(JsonNode.DeepEquals(expected, actual),
                "warm and cold documents differ.\ncold: " + expected.ToJsonString() + "\nwarm: " + actual.ToJsonString());

            var viaCold = await new PanGlossTracer(invoker, stepCap).TraceAsync(grammarPath, word, CancellationToken.None);
            var viaWarm = await new WarmFirstTracer(pool, new PanGlossTracer(invoker, stepCap), stepLimit: stepCap)
                .TraceAsync(grammarPath, "t1", grammarPath, word, CancellationToken.None);
            Assert.Equal(viaCold.GetType(), viaWarm.GetType());
            if (cap is not null) Assert.IsType<PanGlossTraceOutcome.Incomplete>(viaWarm);
        }
        finally
        {
            File.Delete(grammarPath);
        }
    }

    private sealed class ServeExecutableTheoryAttribute : TheoryAttribute
    {
        public ServeExecutableTheoryAttribute()
        {
            var executable = Environment.GetEnvironmentVariable(ServeExecutableVariable);
            if (string.IsNullOrEmpty(executable) || !File.Exists(executable))
                Skip = $"{ServeExecutableVariable} is not set to a built pangloss with `serve`.";
        }
    }

    private static JsonNode Normalize(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var name in obj.Select(pair => pair.Key).ToList())
                {
                    if (WallClockFields.Contains(name) || name.EndsWith("ElapsedNs", StringComparison.Ordinal)) obj.Remove(name);
                    else if (obj[name] is { } child) Normalize(child);
                }
                break;
            case JsonArray array:
                foreach (var child in array) if (child is not null) Normalize(child);
                break;
        }
        return node;
    }
}
