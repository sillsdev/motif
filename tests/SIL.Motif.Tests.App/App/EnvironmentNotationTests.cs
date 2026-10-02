using SIL.Motif.App.ViewModels;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class EnvironmentNotationTests
{
    [Theory]
    [InlineData("/", "Environment begins")]
    [InlineData("_", "Target position")]
    [InlineData("#", "Word edge")]
    [InlineData("[C]", "Natural class C; membership is project-defined")]
    [InlineData("(", "Optional group begins")]
    [InlineData(")", "Optional group ends")]
    public void OnlyEstablishedEnvironmentTokensHaveAnInterpretation(string raw, string explanation)
    {
        var token = Assert.Single(EnvironmentNotation.Decode(raw));
        Assert.Equal(raw, token.Raw);
        Assert.Equal(explanation, token.Explanation);
    }

    [Fact]
    public void TheEnvironmentKeepsItsOriginalTokensInOrder()
    {
        var tokens = EnvironmentNotation.Decode("/[C]_#");
        Assert.Equal(["/", "[C]", "_", "#"], tokens.Select(token => token.Raw));
        Assert.DoesNotContain(tokens, token => token.IsProjectDefined);
    }

    [Theory]
    [InlineData("(XV?)[ABC]BBWSF")]
    [InlineData("[Polarity:neg]")]
    [InlineData("[C")]
    [InlineData("[C?]")]
    [InlineData("{\"features\":[]}")]
    public void UnprovedNotationStaysRawAndProjectDefined(string raw)
    {
        var tokens = EnvironmentNotation.Decode(raw);
        Assert.Equal(raw, string.Concat(tokens.Select(token => token.Raw)));
        Assert.Contains(tokens, token => token.IsProjectDefined);
        Assert.All(tokens.Where(token => token.Raw.Contains('?')), token => Assert.True(token.IsProjectDefined));
        Assert.All(tokens.Where(token => token.IsProjectDefined), token => Assert.Equal("Project-defined notation; interpretation unavailable", token.Explanation));
    }
}
