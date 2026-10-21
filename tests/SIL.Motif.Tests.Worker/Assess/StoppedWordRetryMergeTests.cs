using System.Text.Json;
using SIL.Motif.Host.Assess;
using Xunit;

namespace SIL.Motif.Tests.Assess;

public sealed class StoppedWordRetryMergeTests
{
    private const string Tsv = "0\ta\t5\tok\ta-sig\n1\tb\t900\tTIMEOUT\t-\n2\tc\t5\tok\tc-sig\n3\td\t900\tTIMEOUT\t-\n";
    private const string Analyses =
        "{\"index\":0,\"word\":\"a\"}\n{\"index\":1,\"word\":\"b\",\"timedOut\":true}\n" +
        "{\"index\":2,\"word\":\"c\"}\n{\"index\":3,\"word\":\"d\",\"timedOut\":true}\n";

    [Fact]
    public void RetriedRowsReplaceTheStoppedOnesAtTheirOriginalIndexes()
    {
        var merged = StoppedWordRetryRunner.Merge(Tsv, Analyses, [1, 3],
            "0\tb\t1200\tok\tb-sig\n1\td\t3000\tTIMEOUT\t-\n",
            "{\"index\":0,\"word\":\"b\",\"timedOut\":false}\n{\"index\":1,\"word\":\"d\",\"timedOut\":true}\n");

        Assert.NotNull(merged);
        Assert.Equal("0\ta\t5\tok\ta-sig\n1\tb\t1200\tok\tb-sig\n2\tc\t5\tok\tc-sig\n3\td\t3000\tTIMEOUT\t-\n",
            merged.Value.Tsv);
        var lines = merged.Value.Analyses.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length);
        Assert.Equal(lines.Select((_, i) => i), lines.Select(line => JsonDocument.Parse(line).RootElement.GetProperty("index").GetInt32()));
        Assert.False(JsonDocument.Parse(lines[1]).RootElement.GetProperty("timedOut").GetBoolean());
        Assert.True(JsonDocument.Parse(lines[3]).RootElement.GetProperty("timedOut").GetBoolean());
        Assert.Equal("{\"index\":0,\"word\":\"a\"}", lines[0]);
    }

    [Fact]
    public void AMissingRetryRowRefusesTheMergeRatherThanLeavingAStoppedRowUnmarked()
    {
        Assert.Null(StoppedWordRetryRunner.Merge(Tsv, Analyses, [1, 3], "0\tb\t1\tok\tb-sig\n",
            "{\"index\":0}\n{\"index\":1}\n"));
        Assert.Null(StoppedWordRetryRunner.Merge(Tsv, Analyses, [1, 3], "0\tb\t1\tok\tb-sig\n1\td\t1\tok\td\n",
            "{\"index\":0}\n"));
        Assert.Null(StoppedWordRetryRunner.Merge(Tsv, Analyses, [9], "0\tb\t1\tok\tb-sig\n", "{\"index\":0}\n"));
    }
}
