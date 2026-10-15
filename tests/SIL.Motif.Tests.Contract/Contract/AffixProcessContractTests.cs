using System.Text.Json;
using System.Text.RegularExpressions;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Generator;
using SIL.Motif.Generator.Emit;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public sealed class AffixProcessContractTests
{
    [Fact]
    public void IntentAndLayerZeroSchemasAreEmbeddedClosedAndBounded()
    {
        using var stream = typeof(CanonicalId).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.author-affix-process.schema.json");
        Assert.NotNull(stream);
        using var schema = JsonDocument.Parse(stream);
        Assert.False(schema.RootElement.GetProperty("additionalProperties").GetBoolean());
        var properties = schema.RootElement.GetProperty("properties");
        Assert.False(properties.TryGetProperty("form", out _));
        Assert.False(properties.TryGetProperty("formWs", out _));
        Assert.Equal(16, properties.GetProperty("input").GetProperty("maxItems").GetInt32());
        Assert.Equal(64, properties.GetProperty("output").GetProperty("maxItems").GetInt32());
        foreach (var part in properties.GetProperty("input").GetProperty("items").GetProperty("oneOf").EnumerateArray())
            Assert.False(part.GetProperty("additionalProperties").GetBoolean());
        using var formStream = typeof(CanonicalId).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.create-lexeme-form.schema.json");
        Assert.NotNull(formStream);
        using var forms = JsonDocument.Parse(formStream);
        var branches = forms.RootElement.GetProperty("oneOf").EnumerateArray().ToArray();
        Assert.Equal(2, branches.Length);
        Assert.All(branches, branch => Assert.False(branch.GetProperty("additionalProperties").GetBoolean()));
        var process = branches.Single(branch => branch.GetProperty("properties").TryGetProperty("class", out _));
        Assert.Equal("MoAffixProcess", process.GetProperty("properties").GetProperty("class").GetProperty("const").GetString());
        Assert.False(process.GetProperty("properties").TryGetProperty("ws", out _));
        Assert.False(process.GetProperty("properties").TryGetProperty("text", out _));
    }

    [Theory]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA", true)]
    [InlineData("ph:AAAAAAAAAAAAAAAAAAAAAQ", true)]
    [InlineData("前/\n:AAAAAAAAAAAAAAAAAAAAAQ", true)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAR", true)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAA", false)]
    [InlineData("ph:AAAAAAAAAAAAAAAAAAAAA:", false)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA\n", false)]
    public void SchemaIdentityPatternsAgreeWithCanonicalIdSuffixContract(string value, bool accept)
    {
        Assert.Equal(accept, CanonicalId.TryParse(value, out _));
        using var stream = typeof(CanonicalId).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.author-affix-process.schema.json");
        using var intent = JsonDocument.Parse(stream!);
        Check(intent.RootElement.GetProperty("$defs").GetProperty("CanonicalId"));
        using var formStream = typeof(CanonicalId).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.create-lexeme-form.schema.json");
        using var forms = JsonDocument.Parse(formStream!);
        foreach (var branch in forms.RootElement.GetProperty("oneOf").EnumerateArray())
            Check(branch.GetProperty("properties").GetProperty("morphType"));

        void Check(JsonElement schema) => Assert.Equal(accept,
            value.Length >= schema.GetProperty("minLength").GetInt32() &&
            Regex.IsMatch(value, schema.GetProperty("pattern").GetString()!));
    }

    [Fact]
    public void GeneratorEmitsOnlyFlatInputAndCopySingletonInsertFactories()
    {
        var temporary = Path.Combine(Path.GetTempPath(), "motif-affix-emission-" + Guid.NewGuid().ToString("N"));
        try
        {
            var written = OwningCreateCatalogWriter.WriteAll(MotifModelLoader.Load(), temporary);
            foreach (var name in new[] { "MoAffixProcessInput", "MoAffixProcessOutput" })
                Assert.Contains(written, file => file.RelativePath.EndsWith(name + ".g.cs", StringComparison.Ordinal));
            var input = File.ReadAllText(Path.Combine(temporary, "src/SIL.Motif.Runner/Operations/Generated5/MoAffixProcessInput.g.cs"));
            Assert.Contains("IPhVariableFactory", input);
            Assert.Contains("IPhSimpleContextSegFactory", input);
            Assert.Contains("IPhSimpleContextNCFactory", input);
            Assert.DoesNotContain("IPhSequenceContextFactory", input);
            Assert.DoesNotContain("IPhIterationContextFactory", input);
            Assert.DoesNotContain("Delete", input);
            Assert.DoesNotContain("Reparent", input);
            var output = File.ReadAllText(Path.Combine(temporary, "src/SIL.Motif.Runner/Operations/Generated5/MoAffixProcessOutput.g.cs"));
            Assert.Contains("IMoCopyFromInputFactory", output);
            Assert.Contains("IMoInsertPhonesFactory", output);
            Assert.DoesNotContain("IMoInsertNCFactory", output);
            Assert.DoesNotContain("IMoModifyFromInputFactory", output);
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true); }
    }
}
