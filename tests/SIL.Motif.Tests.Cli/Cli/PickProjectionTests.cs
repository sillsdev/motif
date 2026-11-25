using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class PickProjectionTests
{
    private const string Json = """
        {"gate":{"state":"green","unexpectedLosses":2},"summary":{"wordforms":9},
         "rows":[{"label":"a","tokens":1},{"label":"b"},{"label":"c","tokens":3}],"empty":[]}
        """;

    private static JsonElement Pick(string spec)
    {
        var result = PickProjection.Apply(new CommandResult(0, Json), spec);
        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        return document.RootElement.Clone();
    }

    [Fact]
    public void ANamedPathKeepsOnlyThatFieldUnderItsOwnPath()
    {
        var picked = Pick("gate.state,summary.wordforms");
        Assert.Equal("green", picked.GetProperty("gate.state").GetString());
        Assert.Equal(9, picked.GetProperty("summary.wordforms").GetInt32());
        Assert.Equal(2, picked.EnumerateObject().Count());
    }

    [Fact]
    public void AnArrayPathYieldsOneValuePerElementWithNullWhereAnElementLacksIt()
    {
        var picked = Pick("rows[].label,rows[].tokens");
        Assert.Equal(["a", "b", "c"], picked.GetProperty("rows[].label").EnumerateArray().Select(e => e.GetString()));
        var tokens = picked.GetProperty("rows[].tokens").EnumerateArray().ToArray();
        Assert.Equal(JsonValueKind.Null, tokens[1].ValueKind);
        Assert.Equal(3, tokens[2].GetInt32());
    }

    [Fact]
    public void AnIndexedPathTakesOneElementAndAnEmptyArrayProjectsToAnEmptyArray()
    {
        Assert.Equal("c", Pick("rows[2].label").GetProperty("rows[2].label").GetString());
        Assert.Equal(0, Pick("empty[].x").GetProperty("empty[].x").GetArrayLength());
    }

    [Theory]
    [InlineData("gate.nope", "state, unexpectedLosses")]
    [InlineData("nope", "gate, summary, rows")]
    [InlineData("rows[].nope", "label, tokens")]
    [InlineData("rows[7]", "3 elements")]
    [InlineData("gate[]", "not an array")]
    [InlineData("gate..state", "not valid")]
    [InlineData("rows[x]", "not valid")]
    public void ABadPathIsRefusedWithTheRegisteredCodeAndTheNamesThatExist(string spec, string expected)
    {
        var result = PickProjection.Apply(new CommandResult(0, Json), spec);
        Assert.NotEqual(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        Assert.Equal(HostRefusalCodes.CliPickPath, document.RootElement.GetProperty("code").GetString());
        Assert.Contains(expected, document.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.True(RefusalCatalog.Specs.ContainsKey(HostRefusalCodes.CliPickPath));
    }

    [Fact]
    public void TextOutputIsRefusedBecauseThereIsNothingToProject()
    {
        var result = PickProjection.Apply(new CommandResult(0, "plain text\n"), "a");
        Assert.NotEqual(0, result.ExitCode);
    }
}
