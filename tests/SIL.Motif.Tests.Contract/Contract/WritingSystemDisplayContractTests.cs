using System.Text.Json;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public sealed class WritingSystemDisplayContractTests
{
    [Fact]
    public void DisplayWireShapeMatchesItsClosedSchemaAndRoundTripsInPoints()
    {
        var sizes = new Dictionary<string, double>
        {
            ["Normal"] = 10, ["Paragraph"] = 12.5, ["Dictionary-Headword"] = 14,
            ["Dictionary-Vernacular"] = 10, ["Dictionary-POS"] = 10, ["Title_Main"] = 20,
        };
        var display = new WritingSystemDisplay("ar", "Arabic", "Ar", WritingSystemKind.Vernacular, 2, false,
            "Missing font", "1051=1", true, sizes)
        { StyleFonts = sizes.ToDictionary(pair => pair.Key, _ => new WritingSystemStyleFont("Missing font", "1051=1")) };
        var json = ProjectionJson.Serialize(display);
        using var document = JsonDocument.Parse(json);
        using var stream = typeof(WritingSystemDisplay).Assembly.GetManifestResourceStream(
            "SIL.Motif.Contract.Schemas.writing-system-display.schema.json");
        Assert.NotNull(stream);
        using var schema = JsonDocument.Parse(stream);
        var shape = schema.RootElement.GetProperty("$defs").GetProperty("WritingSystemDisplay");
        Assert.False(shape.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(shape.GetProperty("required").EnumerateArray().Select(item => item.GetString()).Order(),
            document.RootElement.EnumerateObject().Select(item => item.Name).Order());
        Assert.Equal("vernacular", document.RootElement.GetProperty("kind").GetString());
        Assert.False(document.RootElement.TryGetProperty("fontEngine", out _));
        Assert.Equal(12.5, document.RootElement.GetProperty("styleSizes").GetProperty("Paragraph").GetDouble());
        Assert.Equal(json, ProjectionJson.Serialize(ProjectionJson.Deserialize<WritingSystemDisplay>(json)));
        var reversed = display with { StyleSizes = sizes.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value) };
        Assert.Equal(CanonicalJson.Canonicalize(json), CanonicalJson.Canonicalize(ProjectionJson.Serialize(reversed)));
        Assert.Throws<JsonException>(() => ProjectionJson.Deserialize<WritingSystemDisplay>(json.Replace(
            "\"id\": \"ar\"", "\"unknown\": true, \"id\": \"ar\"")));
    }

    [Fact]
    public void ComposedAndUnknownTextExplicitlySerializeNullTags()
    {
        using var document = JsonDocument.Parse(ProjectionJson.Serialize(new WordRow("word", WordRowOutcome.NotParsed,
            "Not parsed", WordRowTone.Neutral) { WordWritingSystem = "fr", Gloss = "mixed glosses" }));
        Assert.Equal("fr", document.RootElement.GetProperty("wordWritingSystem").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("glossWritingSystem").ValueKind);
        using var tagged = JsonDocument.Parse(ProjectionJson.Serialize(new WritingSystemText("composed", null)));
        Assert.Equal(JsonValueKind.Null, tagged.RootElement.GetProperty("writingSystem").ValueKind);
    }
}
