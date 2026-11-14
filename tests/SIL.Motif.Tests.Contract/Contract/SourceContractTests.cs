using System.Text.Json;
using System.Text.RegularExpressions;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public sealed class SourceContractTests
{
    private static readonly BaselineToken Token = new("project-1", "sha256:" + new string('a', 64), "projection-1",
        "2026-01-01T00:00:00Z", "sha256:" + new string('b', 64), "session-1", 3);

    private static SourceListResponse ListSample() => new([
        new SourceSummary("WEB", "World English Bible", "Paratext", "Active", "English", "GEN-ROM; 1CO 13-15",
            [new SourceContextRange("GEN", "all"), new SourceContextRange("1CO", "13-15")], "/data/WEB", "bad", "InContext"),
        new SourceSummary("OLD", "Old", "Paratext", "Removed", "English", "MAT", [new SourceContextRange("MAT", "all")], null, null),
    ]) { Baseline = Token, IsStale = true };

    private static CurrentGetResponse CurrentSample() =>
        new([new CurrentChapter("WEB", "MAT 5", "Current"), new CurrentChapter("OLD", "MAT 6", null)]) { Baseline = Token };

    private static JsonDocument Schema()
    {
        using var stream = typeof(SourceListResponse).Assembly.GetManifestResourceStream("SIL.Motif.Contract.Schemas.source.schema.json");
        Assert.NotNull(stream);
        return JsonDocument.Parse(stream);
    }

    [Fact]
    public void EveryMemberOfEveryDefinitionMatchesTheWire()
    {
        using var schema = Schema();
        var walk = new SchemaWalk(schema.RootElement.GetProperty("$defs"));

        walk.Check("SourceListResponse", JsonDocument.Parse(ProjectionJson.Serialize(ListSample())).RootElement, "list");
        walk.Check("CurrentGetResponse", JsonDocument.Parse(ProjectionJson.Serialize(CurrentSample())).RootElement, "current");

        foreach (var definition in schema.RootElement.GetProperty("$defs").EnumerateObject())
        {
            Assert.True(walk.Seen.ContainsKey(definition.Name), $"No sample reaches {definition.Name}.");
            if (definition.Value.TryGetProperty("properties", out var properties))
                Assert.Equal(properties.EnumerateObject().Select(item => item.Name).Order(), walk.Seen[definition.Name].Order());
        }
    }

    [Fact]
    public void ListRoundTripsWithItsBaselineAndVersion()
    {
        var json = ProjectionJson.Serialize(ListSample());

        Assert.Equal(SourceWire.SchemaVersion, JsonDocument.Parse(json).RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(json, ProjectionJson.Serialize(ProjectionJson.Deserialize<SourceListResponse>(json)));
    }

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
                case "boolean": Assert.True(value.ValueKind is JsonValueKind.True or JsonValueKind.False, $"{path} is not a boolean."); break;
                case "integer": Assert.True(value.TryGetInt64(out _), $"{path} is not an integer."); break;
                case "array":
                    Assert.Equal(JsonValueKind.Array, value.ValueKind);
                    foreach (var item in value.EnumerateArray()) Shape(shape.GetProperty("items"), item, path + "[]");
                    break;
                case "object":
                    Assert.Equal(JsonValueKind.Object, value.ValueKind);
                    foreach (var required in shape.GetProperty("required").EnumerateArray())
                        Assert.True(value.TryGetProperty(required.GetString()!, out _), $"{path} lacks {required.GetString()}.");
                    var properties = shape.GetProperty("properties");
                    foreach (var member in value.EnumerateObject())
                    {
                        Assert.True(properties.TryGetProperty(member.Name, out var memberShape), $"{path}.{member.Name} is not in the schema.");
                        seen?.Add(member.Name);
                        Shape(memberShape, member.Value, path + "." + member.Name);
                    }
                    break;
            }
        }
    }
}
