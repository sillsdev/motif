using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Contract.Contract;

public sealed class ProposalDifferenceSummaryContractTests
{
    internal static ProposalDifferenceSummary Summary() => new("difference/1", "proposal/1", "job/1", "assessment/b",
        "assessment/a", "text/1", "Title", "baseline/1", new ProposalDifferencePolicy(), 2, 3, 0, 1, 0, 0, [], [],
        "parsimony", null, Pin("1.0.0"), Pin("1.0.1"), ["Parsers differ."], 1,
        [new(HumanAgreementLimitation.Capped, 1, 2)], "2026-10-31T00:00:00Z");

    private static ProposalDifferenceSidePin Pin(string version) =>
        new(version, "sha256:p", "sha256:g", "selection", "sha256:s", "tokeniser", "1");

    [Fact]
    public void TheSummaryRoundTripsWithItsPinsWarningsAndLimitationsByName()
    {
        var options = MotifJson.CreateOptions();
        var json = JsonSerializer.Serialize(Summary(), options);

        var restored = JsonSerializer.Deserialize<ProposalDifferenceSummary>(json, options)!;

        Assert.Equal(json, JsonSerializer.Serialize(restored, options));
        Assert.Contains("\"Limitation\":\"capped\"", json);
        Assert.Equal("1.0.1", restored.After.ParserVersion);
        Assert.Equal(["Parsers differ."], restored.Warnings);
    }

    [Fact]
    public void ASummaryMissingAPinIsRefused()
    {
        var options = MotifJson.CreateOptions();
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Summary(), options))!.AsObject();
        node.Remove("Before");

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ProposalDifferenceSummary>(node.ToJsonString(), options));
    }

    [Fact]
    public void TheResponseNamesWhetherWordDetailIsKept()
    {
        var options = MotifJson.CreateOptions();
        var response = new ProposalDifferenceResponse(Summary(), [], 0, null, false);

        var json = JsonSerializer.Serialize(response, options);

        Assert.Contains("\"WordDetailKept\":false", json);
    }
}
