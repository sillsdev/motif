using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public sealed class AffixAuthoringContractTests
{
    [Fact]
    public void AuthoringSchemasAreEmbeddedAndEveryObjectIsClosed()
    {
        using var stream = typeof(CanonicalId).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.affix-authoring.schema.json");
        Assert.NotNull(stream);
        using var document = JsonDocument.Parse(stream!);
        foreach (var definition in document.RootElement.GetProperty("$defs").EnumerateObject())
            if (definition.Value.TryGetProperty("type", out var type) && type.GetString() == "object")
                Assert.False(definition.Value.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public void PortableAuthoringVectorsAgreeWithTheClosedParsers()
    {
        using var stream = typeof(CanonicalId).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.affix-authoring.vectors.json");
        Assert.NotNull(stream);
        using var document = JsonDocument.Parse(stream!);
        var accepted = 0;
        var refused = 0;
        foreach (var vector in document.RootElement.GetProperty("vectors").EnumerateArray())
        {
            var input = vector.GetProperty("input");
            void Parse()
            {
                switch (vector.GetProperty("shape").GetString())
                {
                    case "AuthorLexicalEntry": _ = AuthorLexicalEntryIntentParser.Parse(input); break;
                    case "AuthorAlternateForm": _ = AuthorAlternateFormIntentParser.Parse(input); break;
                    case "AlternateFormsPayload": _ = LexEntryAlternateFormsCreatePayload.Parse(input); break;
                    default: throw new InvalidOperationException("Unknown vector shape.");
                }
            }
            if (vector.GetProperty("valid").GetBoolean()) { Parse(); accepted++; }
            else { Assert.ThrowsAny<Exception>(Parse); refused++; }
        }
        Assert.True(accepted >= 4);
        Assert.True(refused >= 8);
    }

    [Theory]
    [InlineData("inflectionClasses")]
    [InlineData("environments")]
    public void ParserRefusesRepeatedDecodedIdentities(string field)
    {
        using var document = JsonDocument.Parse($$$"""
            {"entry":"AAAAAAAAAAAAAAAAAAAAAQ","form":"a","formWs":"en","morphType":"AAAAAAAAAAAAAAAAAAAAAg",
             "{{{field}}}":["AAAAAAAAAAAAAAAAAAAAAw","form:AAAAAAAAAAAAAAAAAAAAAx"]}
            """);
        Assert.ThrowsAny<Exception>(() => AuthorAlternateFormIntentParser.Parse(document.RootElement));
    }

    [Theory]
    [InlineData("LexEntry", "AlternateForms", "MoForm", "Seq", "feeding", "lexical/lexEntry/createAlternateForms")]
    public void OwningFamiliesMatchTheGeneratedModelClassification(string owner, string field, string signature,
        string cardinality, string comparison, string kind)
    {
        var row = SIL.Motif.Generator.MotifModelLoader.Load().Rows.Single(row => row.DeclaringClass == owner && row.FieldName == field);
        Assert.Equal("in", row.Manifest.Scope);
        Assert.Equal(SIL.Motif.Generator.Model.FieldKind.Owning, row.Kind);
        Assert.Equal(signature, row.Sig);
        Assert.Equal(cardinality, row.Card.ToString());
        Assert.Equal(comparison, row.Manifest.ComparisonClass);
        Assert.Contains(kind, SIL.Motif.Generator.Derivation.KindNameDerivation.DeriveAll(
            SIL.Motif.Generator.Derivation.GroupDerivation.Derive(owner),
            SIL.Motif.Generator.Derivation.ConstructDerivation.Derive(owner), row.Manifest.Verbs, field));
    }

    [Fact]
    public void AlternatePayloadRefusesDuplicateProperties()
    {
        using var document = JsonDocument.Parse("""
            {"morphType":"AAAAAAAAAAAAAAAAAAAAAQ","ws":"sw","text":"m","text":"mw"}
            """);
        Assert.ThrowsAny<Exception>(() => LexEntryAlternateFormsCreatePayload.Parse(document.RootElement));
    }

}
