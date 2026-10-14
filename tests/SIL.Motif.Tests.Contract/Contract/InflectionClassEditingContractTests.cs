using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public sealed class InflectionClassEditingContractTests
{
    [Fact]
    public void EditingSchemasRequireEveryFieldAndCloseEveryIntent()
    {
        using var document = Read("schema");
        foreach (var name in new[] { "EditPartOfSpeechInflectionClassDefault", "EditAffixInflectionClasses" })
        {
            var shape = document.RootElement.GetProperty("$defs").GetProperty(name);
            Assert.False(shape.GetProperty("additionalProperties").GetBoolean());
            Assert.Equal(3, shape.GetProperty("required").GetArrayLength());
        }
    }

    [Fact]
    public void PortableVectorsAgreeWithBothClosedParsers()
    {
        using var document = Read("vectors");
        var accepted = 0;
        var refused = 0;
        foreach (var vector in document.RootElement.GetProperty("vectors").EnumerateArray())
        {
            void Parse()
            {
                var input = vector.GetProperty("input");
                switch (vector.GetProperty("shape").GetString())
                {
                    case "EditPartOfSpeechInflectionClassDefault": _ = EditPartOfSpeechInflectionClassDefaultIntentParser.Parse(input); break;
                    case "EditAffixInflectionClasses": _ = EditAffixInflectionClassesIntentParser.Parse(input); break;
                    default: throw new InvalidOperationException("Unknown vector shape.");
                }
            }
            if (vector.GetProperty("valid").GetBoolean()) { Parse(); accepted++; }
            else { Assert.ThrowsAny<Exception>(Parse); refused++; }
        }
        Assert.Equal(6, accepted);
        Assert.True(refused >= 20);
    }

    [Theory]
    [InlineData("EditPartOfSpeechInflectionClassDefault", "expectedInflectionClass", "null")]
    [InlineData("EditAffixInflectionClasses", "expectedInflectionClasses", "[]")]
    public void DuplicatePropertiesAreRefused(string shape, string expected, string value)
    {
        var desired = shape == "EditAffixInflectionClasses" ? "inflectionClasses" : "inflectionClass";
        using var document = JsonDocument.Parse($$$"""
            {"target":"AAAAAAAAAAAAAAAAAAAAAQ","{{{expected}}}":{{{value}}},
             "{{{expected}}}":{{{value}}},"{{{desired}}}":{{{value}}}}
            """);
        Assert.ThrowsAny<Exception>(() =>
        {
            if (shape == "EditAffixInflectionClasses") EditAffixInflectionClassesIntentParser.Parse(document.RootElement);
            else EditPartOfSpeechInflectionClassDefaultIntentParser.Parse(document.RootElement);
        });
    }

    [Fact]
    public void DefaultSetRequiresAReferenceAndClearRequiresAnEmptyPayload()
    {
        using var nullReference = JsonDocument.Parse("{\"ref\":null}");
        using var empty = JsonDocument.Parse("{}");
        Assert.ThrowsAny<Exception>(() => PartOfSpeechDefaultInflectionClassSetPayload.Parse(nullReference.RootElement));
        Assert.ThrowsAny<Exception>(() => PartOfSpeechDefaultInflectionClassClearPayload.Parse(nullReference.RootElement));
        PartOfSpeechDefaultInflectionClassClearPayload.Parse(empty.RootElement);
    }

    [Theory]
    [InlineData("PartOfSpeech", "DefaultInflectionClass", "MoInflClass", "Atomic")]
    [InlineData("MoAffixForm", "InflectionClasses", "MoInflClass", "Col")]
    public void EditorsUseClassifiedExistingReferenceFamilies(string owner, string field, string signature, string cardinality)
    {
        var row = SIL.Motif.Generator.MotifModelLoader.Load().Rows.Single(row => row.DeclaringClass == owner && row.FieldName == field);
        Assert.Equal("in", row.Manifest.Scope);
        Assert.Equal(SIL.Motif.Generator.Model.FieldKind.Rel, row.Kind);
        Assert.Equal(signature, row.Sig);
        Assert.Equal(cardinality, row.Card.ToString());
    }

    private static JsonDocument Read(string suffix)
    {
        using var stream = typeof(CanonicalId).Assembly.GetManifestResourceStream(
            $"SIL.Motif.Contract.Schemas.inflection-class-editing.{suffix}.json");
        Assert.NotNull(stream);
        return JsonDocument.Parse(stream!);
    }
}
