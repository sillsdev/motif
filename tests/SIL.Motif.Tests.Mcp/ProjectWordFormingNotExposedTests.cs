using Xunit;

namespace SIL.Motif.Tests.Mcp;

public sealed class ProjectWordFormingNotExposedTests
{
    public static TheoryData<string> Goldens()
    {
        var data = new TheoryData<string>();
        foreach (var profile in ToolsListGolden.Profiles()) data.Add(profile);
        return data;
    }

    [Theory]
    [MemberData(nameof(Goldens))]
    public void NoToolsListGoldenOffersTheHumanOnlyProjectWordFormingCommand(string profile)
    {
        var tools = ToolsListGolden.Read(profile);

        Assert.NotEmpty(tools);
        foreach (var tool in tools)
        {
            var text = tool.ToJsonString();
            Assert.DoesNotContain("word-forming", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("word_forming", text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
