using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Composers;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Trait("MotifTestLevel", "Unit")]
public sealed class MultipleOutcomeRuleIntentTests
{
    [Fact]
    public void EachOutcomeParsesItsOwnFeaturesContextsAndAlphaReferences()
    {
        var feature = CanonicalId.Mint().Value;
        var naturalClass = CanonicalId.Mint().Value;
        var parsed = AuthorPhonologicalRuleIntentParser.Parse(JsonSerializer.SerializeToElement(new
        {
            name = "cases", direction = "simultaneous", input = new[] { new { naturalClass } },
            rightHandSides = new object[]
            {
                new { output = new[] { new { naturalClass } }, left = new[] { new { boundary = "word" } }, requiredFeatures = new[] { feature } },
                new { output = new[] { new { change = Array.Empty<object>(), variables = new[] { new { variable = "alpha", feature, polarity = "-" } } } },
                    right = new[] { new { naturalClass, variables = new[] { new { variable = "alpha", feature } } } }, excludedFeatures = new[] { feature } },
            },
        }));
        Assert.Equal(2, parsed.Outcomes.Count);
        Assert.Equal("word", Assert.Single(parsed.Outcomes[0].Left).Boundary);
        Assert.Equal(feature, Assert.Single(parsed.Outcomes[0].RequiredFeatures!).Value);
        Assert.Empty(parsed.Outcomes[0].ExcludedFeatures!);
        Assert.Equal(feature, Assert.Single(parsed.Outcomes[1].ExcludedFeatures!).Value);
        Assert.Equal("-", Assert.Single(parsed.Outcomes[1].Output.Single().Variables!).Polarity);
        Assert.Equal("alpha", Assert.Single(parsed.Outcomes[1].Right.Single().Variables!).Variable);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("mixed")]
    [InlineData("missing-output")]
    [InlineData("unknown")]
    public void OutcomeArraysAreNonemptyClosedAndSeparateFromTheLegacyShorthand(string scenario)
    {
        var outcome = new JsonObject { ["output"] = new JsonArray() };
        var authored = new JsonObject
        {
            ["name"] = "cases", ["direction"] = "simultaneous", ["input"] = new JsonArray(),
            ["rightHandSides"] = new JsonArray(outcome),
        };
        if (scenario == "empty") authored["rightHandSides"] = new JsonArray();
        if (scenario == "mixed") authored["output"] = new JsonArray();
        if (scenario == "missing-output") outcome.Remove("output");
        if (scenario == "unknown") outcome["unexpected"] = true;
        Assert.Throws<ContractParseException>(() => AuthorPhonologicalRuleIntentParser.Parse(JsonSerializer.SerializeToElement(authored)));
    }
}
