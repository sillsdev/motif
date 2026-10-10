using System;
using System.Text.Json;
using SIL.Motif.Contract.Retirement;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public class RetirementConformanceVectorTests
{
    [Fact]
    public void PortableVectorsPinClosedParsingAndSemanticDigest()
    {
        using var stream = typeof(AllomorphRetirementCodec).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.allomorph-retirement.vectors.json");
        Assert.NotNull(stream);
        using var document = JsonDocument.Parse(stream!);
        var valid = 0;
        var refused = 0;
        foreach (var vector in document.RootElement.GetProperty("vectors").EnumerateArray())
        {
            var input = vector.GetProperty("input").GetRawText();
            if (vector.TryGetProperty("error", out _))
            {
                Assert.Throws<FormatException>(() => AllomorphRetirementCodec.Parse(input));
                refused++;
            }
            else
            {
                var value = AllomorphRetirementCodec.Parse(input);
                Assert.Equal(vector.GetProperty("intentDigest").GetString(), AllomorphRetirementCodec.IntentDigest(value));
                valid++;
            }
        }
        Assert.Equal(7, valid);
        Assert.Equal(21, refused);
    }

    [Theory]
    [InlineData("allomorph-retirement", "ReplacementIntent")]
    [InlineData("retire-allomorph", "RetireAllomorphIntent")]
    [InlineData("frozen-expectations", "FrozenExpectationSet")]
    [InlineData("allomorph-retirement-review", "ReviewStatistics")]
    [InlineData("retire-redundant-zero-affix", "RetireRedundantZeroAffixIntent")]
    public void PortableSchemasAreEmbeddedAndClosed(string resource, string type)
    {
        using var stream = typeof(AllomorphRetirementCodec).Assembly.GetManifestResourceStream(
            $"SIL.Motif.Contract.Schemas.{resource}.schema.json");
        Assert.NotNull(stream);
        using var document = JsonDocument.Parse(stream!);
        var definitions = document.RootElement.GetProperty("$defs");
        Assert.False(definitions.GetProperty(type).GetProperty("additionalProperties").GetBoolean());
        foreach (var definition in definitions.EnumerateObject())
            if (definition.Value.TryGetProperty("type", out var kind) && kind.GetString() == "object")
                Assert.False(definition.Value.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public void RedundantZeroAffixVectorsPinClosedParsing()
    {
        using var stream = typeof(RetireRedundantZeroAffixCodec).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.retire-redundant-zero-affix.vectors.json");
        Assert.NotNull(stream);
        using var document = JsonDocument.Parse(stream!);
        var accepted = 0;
        var refused = 0;
        foreach (var vector in document.RootElement.GetProperty("vectors").EnumerateArray())
        {
            var input = vector.GetProperty("input").GetRawText();
            if (vector.TryGetProperty("error", out _))
            {
                Assert.Throws<FormatException>(() => RetireRedundantZeroAffixCodec.Parse(input));
                refused++;
            }
            else
            {
                var value = RetireRedundantZeroAffixCodec.Parse(input);
                Assert.Equal("K8Q1aZqBR5uSoC9c1Qw9Bw", value.Retirement.Entry);
                Assert.Equal(RetireRedundantZeroAffixCodec.ToJson(value),
                    RetireRedundantZeroAffixCodec.ToJson(
                        RetireRedundantZeroAffixCodec.Parse(RetireRedundantZeroAffixCodec.ToJson(value))));
                accepted++;
            }
        }
        Assert.Equal(1, accepted);
        Assert.Equal(4, refused);
    }

    [Fact]
    public void RetirementExpectationTranslationSchemaIsEmbeddedAndClosed()
    {
        using var stream = typeof(AllomorphRetirementCodec).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.retirement-expectation-translation.schema.json");
        Assert.NotNull(stream);
        using var document = JsonDocument.Parse(stream!);
        var schema = document.RootElement;
        Assert.Equal("retirement-expectation-translation/v1", schema.GetProperty("properties")
            .GetProperty("contract").GetProperty("const").GetString());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.False(schema.GetProperty("$defs").GetProperty("Mapping")
            .GetProperty("additionalProperties").GetBoolean());
        Assert.False(schema.GetProperty("$defs").GetProperty("BundleTextEffect")
            .GetProperty("additionalProperties").GetBoolean());
    }
}
