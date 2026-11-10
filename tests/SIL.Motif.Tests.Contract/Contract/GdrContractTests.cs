using System.Text.Json;
using System.Text.RegularExpressions;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public sealed class GdrContractTests
{
    private static readonly BaselineToken Token = new("project-1", "sha256:" + new string('a', 64), "projection-1",
        "2026-01-01T00:00:00Z", "sha256:" + new string('b', 64), "session-1", 3);

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
            Baseline = Token, IsStale = true,
            DefaultVernacular = "ar", DefaultAnalysis = "en",
            Styles = [new GdrStyle("Emphasis", GdrStyleKind.Character,
                new Dictionary<string, GdrTextProperties> { ["en"] = italic })],
            WritingSystems = [new GdrWritingSystem("ar", "rtl", "Scheherazade"), new GdrWritingSystem("en", "ltr", "Charis SIL")],
        };
    }

    private static GdrListResponse ListSample() =>
        new(true, [new GdrSummary("id-1", "GDR-1", "title-1", "abstract-1", "id-0")]) { Baseline = Token, IsStale = true };

    private static JsonDocument Schema()
    {
        using var stream = typeof(GdrShowResponse).Assembly.GetManifestResourceStream("SIL.Motif.Contract.Schemas.gdr.schema.json");
        Assert.NotNull(stream);
        return JsonDocument.Parse(stream);
    }

    [Fact]
    public void EveryMemberOfEveryDefinitionMatchesTheWire()
    {
        using var schema = Schema();
        var walk = new SchemaWalk(schema.RootElement.GetProperty("$defs"));

        walk.Check("GdrShowResponse", JsonDocument.Parse(ProjectionJson.Serialize(Sample())).RootElement, "show");
        walk.Check("GdrListResponse", JsonDocument.Parse(ProjectionJson.Serialize(ListSample())).RootElement, "list");

        foreach (var definition in schema.RootElement.GetProperty("$defs").EnumerateObject())
        {
            Assert.True(walk.Seen.ContainsKey(definition.Name), $"No sample reaches {definition.Name}.");
            if (definition.Value.TryGetProperty("properties", out var properties))
                Assert.Equal(properties.EnumerateObject().Select(item => item.Name).Order(), walk.Seen[definition.Name].Order());
        }
    }

    [Fact]
    public void ShowRoundTripsWithItsBaselineAndVersion()
    {
        var json = ProjectionJson.Serialize(Sample());
        using var document = JsonDocument.Parse(json);

        Assert.Equal(GdrWire.SchemaVersion, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(Token, ProjectionJson.Deserialize<GdrShowResponse>(json)!.Baseline);
        Assert.Equal(json, ProjectionJson.Serialize(ProjectionJson.Deserialize<GdrShowResponse>(json)));
    }

    [Fact]
    public void ListRoundTripsWithItsBaseline()
    {
        var json = ProjectionJson.Serialize(ListSample());

        Assert.Equal(json, ProjectionJson.Serialize(ProjectionJson.Deserialize<GdrListResponse>(json)));
    }

    [Fact]
    public void TheClosedRecordsRefuseAnUnknownMember()
    {
        var json = ProjectionJson.Serialize(Sample()).Replace("\"abbreviation\"", "\"unknown\": true, \"abbreviation\"");

        Assert.Throws<JsonException>(() => ProjectionJson.Deserialize<GdrShowResponse>(json));
    }

    // Checks a wire value against a definition, and records which members of each definition were present.
    private sealed class SchemaWalk(JsonElement definitions)
    {
        public Dictionary<string, HashSet<string>> Seen { get; } = [];

        public void Check(string name, JsonElement value, string path)
        {
            var seen = Seen.TryGetValue(name, out var existing) ? existing : Seen[name] = [];
            Shape(definitions.GetProperty(name), value, path, seen);
        }

        private void Shape(JsonElement shape, JsonElement value, string path, HashSet<string>? seen = null)
        {
            if (shape.TryGetProperty("$ref", out var reference))
            {
                Check(reference.GetString()!["#/$defs/".Length..], value, path);
                return;
            }
            if (shape.TryGetProperty("const", out var constant))
                Assert.Equal(constant.GetRawText(), value.GetRawText());
            if (shape.TryGetProperty("enum", out var choices))
                Assert.Contains(value.GetString(), choices.EnumerateArray().Select(item => item.GetString()));
            if (shape.TryGetProperty("pattern", out var pattern))
                Assert.Matches(new Regex(pattern.GetString()!), value.GetString()!);
            if (!shape.TryGetProperty("type", out var type)) return;
            switch (type.GetString())
            {
                case "string": Assert.Equal(JsonValueKind.String, value.ValueKind); break;
                case "integer": Assert.True(value.TryGetInt64(out _), $"{path} is not an integer."); break;
                case "boolean": Assert.True(value.ValueKind is JsonValueKind.True or JsonValueKind.False, $"{path} is not a boolean."); break;
                case "array":
                    Assert.Equal(JsonValueKind.Array, value.ValueKind);
                    Assert.NotEqual(0, value.GetArrayLength());
                    foreach (var item in value.EnumerateArray()) Shape(shape.GetProperty("items"), item, path + "[]");
                    break;
                case "object":
                    Assert.Equal(JsonValueKind.Object, value.ValueKind);
                    Object(shape, value, path, seen);
                    break;
            }
        }

        private void Object(JsonElement shape, JsonElement value, string path, HashSet<string>? seen)
        {
            if (shape.TryGetProperty("properties", out var properties))
            {
                foreach (var required in shape.GetProperty("required").EnumerateArray())
                    Assert.True(value.TryGetProperty(required.GetString()!, out _), $"{path} lacks {required.GetString()}.");
                foreach (var member in value.EnumerateObject())
                {
                    Assert.True(properties.TryGetProperty(member.Name, out var memberShape), $"{path}.{member.Name} is not in the schema.");
                    seen?.Add(member.Name);
                    Shape(memberShape, member.Value, path + "." + member.Name);
                }
            }
            else if (shape.TryGetProperty("additionalProperties", out var entries) && entries.ValueKind == JsonValueKind.Object)
            {
                foreach (var member in value.EnumerateObject()) Shape(entries, member.Value, path + "." + member.Name);
            }
        }
    }
}
