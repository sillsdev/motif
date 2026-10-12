using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Mcp;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Trait("MotifTestLevel", "Unit")]
public sealed class ParsimonyViewByteCapTests
{
    private static CommandOutcome<ParsimonyNamedViewResponse> Page(int total, int offset, int limit, int rowBytes)
    {
        var count = Math.Min(limit, total - offset);
        var rows = Enumerable.Range(offset, count).Select(i => (ParsimonyViewRow)new ParsimonyStatementUsageViewRow(
            "environment", $"guid-{i}", new string('x', rowBytes), 1, 1, true, null, "used")).ToArray();
        var next = offset + count;
        return CommandOutcome<ParsimonyNamedViewResponse>.Success(new ParsimonyNamedViewResponse("bundle", "statement-usage",
            1, ParsimonyMeasureStatus.Computed, [], total, count, next < total ? $"cursor-{next}" : null,
            next < total, rows, "digest", null));
    }

    [Fact]
    public void AViewOverTheByteCapReturnsFewerRowsAndTheCursorForTheRest()
    {
        var calls = new List<int>();
        var outcome = ParsimonyTools.FitToBytes(200, 100_000, limit =>
        {
            calls.Add(limit);
            return Page(1700, 0, limit, 1500);
        });

        var response = outcome.Value!;
        Assert.True(response.Returned is > 0 and < 200, $"returned {response.Returned}");
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(ProjectionJson.Serialize(response)) <= 100_000);
        Assert.True(response.Truncated);
        Assert.Equal($"cursor-{response.Returned}", response.NextCursor);
        Assert.Equal(1700, response.Total);
    }

    [Fact]
    public void AViewUnderTheByteCapIsReadOnce()
    {
        var calls = 0;
        var outcome = ParsimonyTools.FitToBytes(50, 100_000, limit => { calls++; return Page(50, 0, limit, 100); });

        Assert.Equal(1, calls);
        Assert.Equal(50, outcome.Value!.Returned);
    }

    [Fact]
    public void OneRowOverTheCapIsStillReturnedAlone()
    {
        var outcome = ParsimonyTools.FitToBytes(10, 1_000, limit => Page(10, 0, limit, 5_000));

        Assert.Equal(1, outcome.Value!.Returned);
        Assert.NotNull(outcome.Value.NextCursor);
    }
}
