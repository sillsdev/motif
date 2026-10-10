using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Composers;
using Xunit;

namespace SIL.Motif.Tests.Composers;

public sealed class EditAdhocProhibitionIntentParserTests
{
    [Fact]
    public void ParsesTargetExpectedStateAndExplicitDisabledValue()
    {
        using var document = JsonDocument.Parse("""
            {
              "target":"moadhocprohib_0000000000000000000001",
              "expectedDisabled":false,
              "disabled":true
            }
            """);

        var intent = EditAdhocProhibitionIntentParser.Parse(document.RootElement);

        Assert.Equal(CanonicalId.Parse("moadhocprohib_0000000000000000000001"), intent.Target);
        Assert.False(intent.ExpectedDisabled);
        Assert.True(intent.Disabled);
    }

    [Theory]
    [InlineData("{\"target\":\"moadhocprohib_0000000000000000000001\",\"expectedDisabled\":false}")]
    [InlineData("{\"target\":\"moadhocprohib_0000000000000000000001\",\"expectedDisabled\":false,\"disabled\":\"true\"}")]
    [InlineData("{\"target\":\"moadhocprohib_0000000000000000000001\",\"expectedDisabled\":false,\"disabled\":true,\"other\":1}")]
    [InlineData("{\"target\":\"moadhocprohib_0000000000000000000001\",\"expectedDisabled\":false,\"disabled\":true,\"disabled\":false}")]
    public void RejectsMissingInvalidUnknownAndDuplicateProperties(string json)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Throws<ContractParseException>(() => EditAdhocProhibitionIntentParser.Parse(document.RootElement));
    }
}
