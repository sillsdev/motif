using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Tests.Samples;
using Xunit;

namespace SIL.Motif.Tests.Composers;

public sealed class AuthorAffixProcessIntentTests
{
    [Fact]
    public void PortableVectorsMatchTheClosedParser()
    {
        using var stream = typeof(CanonicalId).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.author-affix-process.vectors.json");
        Assert.NotNull(stream);
        using var vectors = JsonDocument.Parse(stream);
        using var schemaStream = typeof(CanonicalId).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.author-affix-process.schema.json");
        using var schema = JsonDocument.Parse(schemaStream!);
        foreach (var vector in vectors.RootElement.GetProperty("vectors").EnumerateArray())
        {
            var input = vector.GetProperty("intent");
            var exception = Record.Exception(() => AuthorAffixProcessIntentParser.Parse(input));
            if (vector.GetProperty("accept").GetBoolean()) Assert.Null(exception);
            else Assert.IsType<ContractParseException>(exception);
            if (vector.TryGetProperty("schemaAccept", out var schemaAccept))
                Assert.Equal(schemaAccept.GetBoolean(),
                    SampleJsonSchemaValidator.Validate(input, schema.RootElement).Count == 0);
        }
    }

    [Theory]
    [InlineData("gloss", "")]
    [InlineData("gloss", " \t\n")]
    [InlineData("analysisWs", "")]
    [InlineData("analysisWs", " \t\n")]
    [InlineData("definition", "")]
    [InlineData("definition", " \t\n")]
    public void SchemaAndParserRefuseBlankLexicalMetadata(string field, string value)
    {
        using var vectorStream = typeof(CanonicalId).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.author-affix-process.vectors.json");
        using var vectors = JsonDocument.Parse(vectorStream!);
        var authored = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            vectors.RootElement.GetProperty("vectors")[0].GetProperty("intent"))!;
        authored[field] = JsonSerializer.SerializeToElement(value);
        var input = JsonSerializer.SerializeToElement(authored);
        using var schemaStream = typeof(CanonicalId).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.author-affix-process.schema.json");
        using var schema = JsonDocument.Parse(schemaStream!);
        Assert.NotEmpty(SampleJsonSchemaValidator.Validate(input, schema.RootElement));
        Assert.Throws<ContractParseException>(() => AuthorAffixProcessIntentParser.Parse(input));
    }

    [Theory]
    [InlineData("\"gloss\":\"other\",", "")]
    [InlineData("", ",\"remaining\":true")]
    [InlineData("", ",\"phoneme\":\"AAAAAAAAAAAAAAAAAAAAAA\"")]
    public void DuplicateAndAmbiguousSemanticPropertiesAreRefused(string extraRoot, string extraPart)
    {
        using var document = JsonDocument.Parse($$"""
            { {{extraRoot}} "morphType":"AAAAAAAAAAAAAAAAAAAAAA", "gloss":"REDUP", "analysisWs":"en",
              "msa":{"kind":"unclassified","category":"AAAAAAAAAAAAAAAAAAAAAA"},
              "input":[{"name":"stem","remaining":true{{extraPart}}}],
              "output":[{"copy":"stem"},{"copy":"stem"}] }
            """);
        Assert.Throws<ContractParseException>(() => AuthorAffixProcessIntentParser.Parse(document.RootElement));
    }

    [Theory]
    [InlineData(17, 2, 1)]
    [InlineData(1, 65, 1)]
    [InlineData(1, 2, 65)]
    [InlineData(1, 6, 64)]
    public void ParserEnforcesInputLogicalOutputInsertAndExpandedBounds(int inputCount, int outputCount, int insertCount)
    {
        var id = CanonicalId.Mint().Value;
        var authored = JsonSerializer.SerializeToElement(new
        {
            morphType = id, gloss = "x", analysisWs = "en", msa = new { kind = "unclassified", category = id },
            input = Enumerable.Range(0, inputCount).Select(index => new { name = "part" + index, phoneme = id }),
            output = new object[] { new { copy = "part0" } }.Concat(Enumerable.Range(1, outputCount - 1)
                .Select(_ => (object)new { insert = Enumerable.Repeat(id, insertCount) })),
        });
        Assert.Throws<ContractParseException>(() => AuthorAffixProcessIntentParser.Parse(authored));
    }

    [Fact]
    public void RemainingPartMustBeUniqueAndLast()
    {
        var id = CanonicalId.Mint().Value;
        var authored = JsonSerializer.SerializeToElement(new
        {
            morphType = id, gloss = "x", analysisWs = "en", msa = new { kind = "unclassified", category = id },
            input = new[] { new { name = "a", remaining = true }, new { name = "b", remaining = true } },
            output = new[] { new { copy = "a" }, new { copy = "b" }, new { copy = "a" } },
        });
        Assert.Throws<ContractParseException>(() => AuthorAffixProcessIntentParser.Parse(authored));
    }
}
