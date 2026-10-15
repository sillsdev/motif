using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Runner.Composers;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public sealed class StemLinkEditingContractTests
{
    [Fact]
    public void SchemasRequireEveryFieldAndCloseEveryIntent()
    {
        using var document = Read("schema");
        foreach (var name in new[] { "EditStemAllomorphStemName", "EditStemInflectionClass" })
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
                    case "EditStemAllomorphStemName": _ = EditStemAllomorphStemNameIntentParser.Parse(input); break;
                    case "EditStemInflectionClass": _ = EditStemInflectionClassIntentParser.Parse(input); break;
                    default: throw new InvalidOperationException("Unknown vector shape.");
                }
            }
            if (vector.GetProperty("valid").GetBoolean()) { Parse(); accepted++; }
            else { Assert.ThrowsAny<Exception>(Parse); refused++; }
        }
        Assert.Equal(6, accepted);
        Assert.Equal(18, refused);
    }

    [Theory]
    [InlineData("EditStemAllomorphStemName", "expectedStemName", "stemName")]
    [InlineData("EditStemInflectionClass", "expectedInflectionClass", "inflectionClass")]
    public void DuplicatePropertiesAreRefused(string shape, string expected, string desired)
    {
        using var document = JsonDocument.Parse($$$"""
            {"target":"AAAAAAAAAAAAAAAAAAAAAQ","{{{expected}}}":null,"{{{expected}}}":null,"{{{desired}}}":null}
            """);
        Assert.ThrowsAny<Exception>(() =>
        {
            if (shape == "EditStemAllomorphStemName") EditStemAllomorphStemNameIntentParser.Parse(document.RootElement);
            else EditStemInflectionClassIntentParser.Parse(document.RootElement);
        });
    }

    private static JsonDocument Read(string suffix)
    {
        using var stream = typeof(CanonicalId).Assembly.GetManifestResourceStream(
            $"SIL.Motif.Contract.Schemas.stem-link-editing.{suffix}.json");
        Assert.NotNull(stream);
        return JsonDocument.Parse(stream!);
    }
}
