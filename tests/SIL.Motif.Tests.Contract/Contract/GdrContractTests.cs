using System.Text.Json;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public sealed class GdrContractTests
{
    private static GdrShowResponse Sample()
    {
        var plain = new GdrTextProperties(new Dictionary<int, GdrIntProperty>(), new Dictionary<int, string>());
        var italic = new GdrTextProperties(
            new Dictionary<int, GdrIntProperty> { [2] = new(0, 1) }, new Dictionary<int, string> { [133] = "Emphasis" });
        var paragraphProperties = new GdrTextProperties(
            new Dictionary<int, GdrIntProperty> { [28] = new(0, 1) }, new Dictionary<int, string> { [133] = "Normal" });
        return new GdrShowResponse(new GdrSummary("id-1", "GDR-1", "title-1", "abstract-1", "id-0"),
            [new GdrParagraph("Normal", true, "ar", paragraphProperties,
                [new GdrRun("abc", "ar", null, plain), new GdrRun("def", "en", "Emphasis", italic, "https://example.invalid/x",
                new GdrLinkTarget(GdrLinkState.Resolved, "Lexical entry", "entry-1", "gloss-1"))], "para-1")])
        {
            DefaultVernacular = "ar", DefaultAnalysis = "en",
            Styles = [new GdrStyle("Emphasis", GdrStyleKind.Character,
                new Dictionary<string, GdrTextProperties> { ["en"] = italic })],
            WritingSystems = [new GdrWritingSystem("ar", "rtl", "Scheherazade"), new GdrWritingSystem("en", "ltr", "Charis SIL")],
        };
    }

    private static JsonElement Definition(JsonDocument schema, string name) =>
        schema.RootElement.GetProperty("$defs").GetProperty(name);

    private static JsonDocument Schema()
    {
        using var stream = typeof(GdrShowResponse).Assembly.GetManifestResourceStream("SIL.Motif.Contract.Schemas.gdr.schema.json");
        Assert.NotNull(stream);
        return JsonDocument.Parse(stream);
    }

    private static IEnumerable<string?> Required(JsonElement shape) =>
        shape.GetProperty("required").EnumerateArray().Select(item => item.GetString()).Order();

    [Fact]
    public void ShowWireShapeMatchesItsSchemaAndRoundTrips()
    {
        var json = ProjectionJson.Serialize(Sample());
        using var document = JsonDocument.Parse(json);
        using var schema = Schema();

        var shape = Definition(schema, "GdrShowResponse");
        Assert.Equal(Required(shape), document.RootElement.EnumerateObject().Select(item => item.Name).Order());
        Assert.Equal(GdrWire.SchemaVersion, document.RootElement.GetProperty("schemaVersion").GetInt32());

        var paragraph = document.RootElement.GetProperty("discussion")[0];
        Assert.Equal(Required(Definition(schema, "GdrParagraph")).Append("style").Append("key").Order(), paragraph.EnumerateObject().Select(item => item.Name).Order());
        var run = paragraph.GetProperty("runs")[1];
        Assert.Equal(Required(Definition(schema, "GdrRun")).Append("style").Append("objData").Append("target").Order(), run.EnumerateObject().Select(item => item.Name).Order());
        var target = run.GetProperty("target");
        Assert.Equal(Required(Definition(schema, "GdrLinkTarget")).Append("kind").Append("name").Order(), target.EnumerateObject().Select(item => item.Name).Order());
        Assert.Equal("resolved", target.GetProperty("state").GetString());
        Assert.Equal("character", document.RootElement.GetProperty("styles")[0].GetProperty("kind").GetString());
        Assert.Equal(1, run.GetProperty("properties").GetProperty("ints").GetProperty("2").GetProperty("value").GetInt32());

        Assert.Equal(json, ProjectionJson.Serialize(ProjectionJson.Deserialize<GdrShowResponse>(json)));
    }

    [Fact]
    public void ListWireShapeMatchesItsSchema()
    {
        var json = ProjectionJson.Serialize(new GdrListResponse(true,
            [new GdrSummary("id-1", "GDR-1", "title-1", "abstract-1", "id-0")]));
        using var document = JsonDocument.Parse(json);
        using var schema = Schema();

        Assert.Equal(Required(Definition(schema, "GdrListResponse")), document.RootElement.EnumerateObject().Select(item => item.Name).Order());
        Assert.Equal(Required(Definition(schema, "GdrSummary")).Append("parentId").Order(),
            document.RootElement.GetProperty("records")[0].EnumerateObject().Select(item => item.Name).Order());
    }

    [Fact]
    public void TheClosedRecordsRefuseAnUnknownMember()
    {
        var json = ProjectionJson.Serialize(Sample()).Replace("\"abbreviation\"", "\"unknown\": true, \"abbreviation\"");

        Assert.Throws<JsonException>(() => ProjectionJson.Deserialize<GdrShowResponse>(json));
    }
}
