using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Generator;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public sealed class ProgressContractTests
{
    private static readonly JsonSchema Schema = CreateValidator();
    [Theory]
    [InlineData("summary")]
    [InlineData("passages")]
    [InlineData("request")]
    [InlineData("refusal")]
    public void GoldenConformsToTheClosedSchema(string name)
    {
        var golden = ReadGolden(name);
        Assert.True(Validator().Evaluate(JsonSerializer.SerializeToElement(golden)).IsValid);
        if (name is "summary" or "passages")
        {
            var response = ProjectionJson.Deserialize<ProgressResponse>(golden.ToJsonString());
            Assert.True(JsonNode.DeepEquals(golden, JsonNode.Parse(ProjectionJson.Serialize(response))));
        }
    }

    [Fact]
    public void UnknownMembersAndInventedParserCountsAreRejected()
    {
        var response = ReadGolden("summary");
        response["totals"]!["tokens"]!["parserGuess"] = 0;
        Assert.False(Validator().Evaluate(JsonSerializer.SerializeToElement(response)).IsValid);
        var request = ReadGolden("request");
        request["scope"]!["unknown"] = true;
        Assert.False(Validator().Evaluate(JsonSerializer.SerializeToElement(request)).IsValid);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ProgressScope>(request["scope"]!.ToJsonString(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public void InvalidLimitAndUnknownScopeAreRejected()
    {
        var request = ReadGolden("request");
        request["limit"] = 201;
        Assert.False(Validator().Evaluate(JsonSerializer.SerializeToElement(request)).IsValid);
        request["limit"] = 50;
        request["scope"]!["kind"] = "sources";
        Assert.False(Validator().Evaluate(JsonSerializer.SerializeToElement(request)).IsValid);
    }

    private static JsonNode ReadGolden(string name) => JsonNode.Parse(File.ReadAllText(Path.Combine(
        RepoPaths.FindRepoRoot(), "tests", "SIL.Motif.Tests.Contract", "Golden", "Progress", name + ".json")))!;

    private static JsonSchema Validator() => Schema;

    private static JsonSchema CreateValidator()
    {
        using var stream = typeof(ProgressResponse).Assembly.GetManifestResourceStream("SIL.Motif.Contract.Schemas.progress.schema.json")!;
        var schema = JsonNode.Parse(stream)!;
        schema["$id"] = "https://example.invalid/motif/progress.schema.json";
        return JsonSchema.FromText(schema.ToJsonString());
    }
}
