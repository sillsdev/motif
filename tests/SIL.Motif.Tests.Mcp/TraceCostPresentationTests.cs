using System.Text.Json;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Mcp;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Trait("MotifTestLevel", "Unit")]
public sealed class TraceCostPresentationTests
{
    [Fact]
    public void ConciseAndDetailedTraceHaveNoRawCost()
    {
        var trace = Example();
        AssertSafe(TryWordSummary.Concise(trace).ToJsonString());
        AssertSafe(ProjectionJson.Serialize(StandaloneTraceProjection.From(trace)));
    }

    [Fact]
    public void LowercaseBranchCannotLeakRawCost()
    {
        var trace = Example() with { Lowercase = Example() };
        var json = ProjectionJson.Serialize(StandaloneTraceProjection.From(trace));
        Assert.Contains("lowercase", json);
        AssertSafe(json);
        AssertSafe(TryWordSummary.Concise(trace).ToJsonString());
    }

    [Fact]
    public void UninstrumentedBatchCannotSupplyTracePercent()
    {
        var json = ProjectionJson.Serialize(StandaloneTraceProjection.From(Example()));
        Assert.DoesNotContain("percent", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReasonsMorphsAndCompletionSurviveSanitization()
    {
        using var document = JsonDocument.Parse(ProjectionJson.Serialize(StandaloneTraceProjection.From(Example())));
        var root = document.RootElement;
        Assert.False(root.GetProperty("complete").GetBoolean());
        Assert.Equal("incomplete", root.GetProperty("searchStatus").GetString());
        Assert.Equal("no root", root.GetProperty("noParseReasons")[0].GetString());
        Assert.Equal("walk", root.GetProperty("analyses")[0].GetProperty("morphs")[0].GetProperty("gloss").GetString());
        Assert.Equal("NoRoot", root.GetProperty("nodes")[0].GetProperty("reasonCode").GetString());
    }

    private static WordTraceResponse Example()
    {
        using var payload = JsonDocument.Parse("{\"parserWork\":12345,\"requestedShape\":\"sag\"}");
        var step = new TraceStep("failure", "rule", null, null, "NoRoot", [])
        {
            FailureEvidence = new TraceFailureEvidence(null, null, "NoRoot", "available", null,
                null, null, null, null) { Payload = payload.RootElement.Clone() },
        };
        var morph = new TraceMorph(null, "sag", null, "walk", "v", null, null, null, null, null)
            { RawJson = "{\"parserWork\":12345}" };
        var reading = new WordTraceReading("sag", step, [],
            [new TraceAnalysis("a", 0, "sag", "available", [morph])], [], [], [])
            { NoParseReasons = ["no root"] };
        return new WordTraceResponse("sag", false, false, "limit", 200, "rule", 123, reading)
        {
            ParserWork = 12345, ParserElapsedMs = 42, SearchStatus = "incomplete",
            DiagnosticJson = "{\"nested\":{\"parserWork\":12345}}",
            Effort = [new TraceEffort("rule", 1, 2, 3, 4, 5, 6, 7) { Work = 12345 }],
        };
    }

    private static void AssertSafe(string json)
    {
        foreach (var prohibited in new[] { "parserWork", "elapsedMs", "selfMs", "diagnosticJson", "rawJson", "12345" })
            Assert.DoesNotContain(prohibited, json, StringComparison.OrdinalIgnoreCase);
    }
}
