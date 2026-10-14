using System.Text.Json;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Mcp;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

public sealed class GrammarReadToolSchemaTests
{
    [Fact]
    public void McpKindChoicesAreTheNamesWrittenByResponseSerialization()
    {
        var tool = AgentTools.All.Single(candidate => candidate.Name == "motif_grammar_read");
        var kindChoices = tool.InputSchema["properties"]!["kind"]!["enum"]!.AsArray()
            .Select(value => value!.GetValue<string>()).ToArray();

        Assert.Equal(Enum.GetNames<GrammarReadKind>(), kindChoices);
        var response = new GrammarReadResponse(GrammarReadKind.natural_class, 1, 0, 25,
        [
            new GrammarNaturalClassRead("id", "Vowels", "V", "PhNCSegments", [], []),
        ]);
        using var json = JsonDocument.Parse(ProjectionJson.Serialize(response));
        Assert.Equal("natural_class", json.RootElement.GetProperty("kind").GetString());
        Assert.Equal("natural_class", json.RootElement.GetProperty("items")[0].GetProperty("kind").GetString());
        Assert.Equal("id", json.RootElement.GetProperty("items")[0].GetProperty("id").GetString());
    }
}
