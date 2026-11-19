using System.Text.Json;
using Json.Schema;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Contract.Review;

public sealed class ProposalComparisonContractTests
{
    private static string Digest(char c) => "sha256:" + new string(c, 64);

    private static ProposalComparison Sample(int columns, bool withPage)
    {
        var all = Enumerable.Range(0, columns).Select(index => new CompareColumn(CanonicalId.Mint("proposal/").Value,
            99999, 99999, 99999, 99999, 99999)).ToArray();
        var summary = new ProposalComparison(1, ProposalComparison.TrialBeforeAfter, "0123456789ab", all, 99999, 99999, 99999, 1000, false,
            [new(CompareLimitation.NotMeasured, 99999), new(CompareLimitation.IncompleteTrial, 8)]);
        if (!withPage) return summary;
        var cells = all.Select((_, index) => new CompareCell(true, true, [Digest('1')], index == 0 ? [Digest('2')] : [])).ToArray();
        return summary with { Page = new CompareRowPage(1, null, [new CompareRow(null, "form", "key", true, cells)]) };
    }

    [Fact]
    public void SummaryAndRowsMatchTheSchemaAndNameTheirBasis()
    {
        var schema = JsonSchema.FromText(new StreamReader(typeof(ProposalComparison).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.proposal-compare.schema.json")!).ReadToEnd());
        var json = ProjectionJson.Serialize(Sample(3, withPage: true));
        Assert.True(schema.Evaluate(JsonDocument.Parse(json).RootElement).IsValid, json);
        Assert.Contains("\"basis\": \"trial-before-after\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("approved", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ProposalComparison.TrialBeforeAfter,
            ProjectionJson.Deserialize<ProposalComparison>(json)!.Basis);
        var wrong = json.Replace("trial-before-after", "approved-analyses", StringComparison.Ordinal);
        Assert.False(schema.Evaluate(JsonDocument.Parse(wrong).RootElement).IsValid);
    }

    [Fact]
    public void EightColumnSummaryStaysWithinTwoKilobytes()
    {
        var bytes = ProjectionJson.ToUtf8Bytes(Sample(ProposalComparison.MaxProposals, withPage: false)).Length;
        Assert.InRange(bytes, 1, ProposalComparison.SummaryByteLimit);
    }
}
