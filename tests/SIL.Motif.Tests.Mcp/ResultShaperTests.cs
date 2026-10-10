using System.Text.Json.Nodes;
using SIL.Motif.Mcp;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

public sealed class ResultShaperTests
{
    [Fact]
    public void ConciseDropsNullsAndBlanksButKeepsAnEmptyListAndFalse()
    {
        var node = JsonNode.Parse("""{"a":null,"b":"","c":[],"d":false,"e":0,"f":{"g":null}}""");

        var shaped = ResultShaper.Concise(node, 25)!.ToJsonString();

        Assert.Equal("""{"c":[],"d":false,"e":0}""", shaped);
    }

    [Fact]
    public void ConciseTruncatesALongListWithANoteSayingHowToSeeTheRest()
    {
        var node = new JsonObject { ["items"] = new JsonArray(Enumerable.Range(0, 30).Select(n => (JsonNode)n).ToArray()) };

        var items = (JsonArray)ResultShaper.Concise(node, 5)!["items"]!;

        Assert.Equal(6, items.Count);
        Assert.Contains("25 more", items[5]!.GetValue<string>());
        Assert.Contains("detail=detailed", items[5]!.GetValue<string>());
    }
}
