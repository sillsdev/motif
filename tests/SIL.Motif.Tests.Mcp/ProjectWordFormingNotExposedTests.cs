using System.Text.Json.Nodes;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

public sealed class ProjectWordFormingNotExposedTests
{
    public static TheoryData<string> Goldens()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.GetFiles(GoldenDirectory(), "tools-list.*.json").Order())
            data.Add(Path.GetFileName(path));
        return data;
    }

    private static string GoldenDirectory([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "Golden");

    [Theory]
    [MemberData(nameof(Goldens))]
    public void NoToolsListGoldenOffersTheHumanOnlyProjectWordFormingCommand(string golden)
    {
        var tools = JsonNode.Parse(File.ReadAllText(Path.Combine(GoldenDirectory(), golden)))!.AsArray();

        Assert.NotEmpty(tools);
        foreach (var tool in tools)
        {
            var text = tool!.ToJsonString();
            Assert.DoesNotContain("word-forming", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("word_forming", text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
