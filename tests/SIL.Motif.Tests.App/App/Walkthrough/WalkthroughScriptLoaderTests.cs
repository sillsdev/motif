using System.Text.Json;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

public sealed class WalkthroughScriptLoaderTests
{
    [Fact]
    public void SchemaNamesEveryStepKindAndRejectsAdditionalFields()
    {
        var schemaPath = Path.Combine(FindRepositoryRoot(), "walkthroughs", "walkthrough.schema.json");
        using var schema = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var root = schema.RootElement;
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", root.GetProperty("$schema").GetString());
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        var stepVariants = root.GetProperty("properties").GetProperty("steps")
            .GetProperty("items").GetProperty("oneOf").EnumerateArray().ToArray();
        var kinds = stepVariants.Select(variant => variant.GetProperty("properties")
            .GetProperty("kind").GetProperty("const").GetString()!).ToArray();
        Assert.Equal(["click", "type", "waitFor", "highlight", "hold", "capture"], kinds);
        Assert.All(stepVariants, variant => Assert.False(variant.GetProperty("additionalProperties").GetBoolean()));
        Assert.Equal(["visible", "hidden", "enabled", "text"],
            stepVariants[2].GetProperty("properties").GetProperty("condition").GetProperty("enum")
                .EnumerateArray().Select(condition => condition.GetString()));
    }

    [Theory]
    [InlineData("{\"id\":\"invalid\",\"steps\":[{\"id\":\"step\",\"kind\":\"fly\"}]}")]
    [InlineData("{\"id\":\"invalid\",\"extra\":true,\"steps\":[{\"id\":\"step\",\"kind\":\"hold\",\"durationMs\":1}]}")]
    [InlineData("{\"id\":\"invalid\",\"steps\":[{\"id\":\"step\",\"kind\":\"hold\",\"durationMs\":1,\"x\":4}]}")]
    [InlineData("{\"id\":\"invalid\",\"steps\":[{\"id\":\"step\",\"kind\":\"highlight\",\"automationId\":\"9bad\"}]}")]
    [InlineData("{\"id\":\"invalid\",\"steps\":[{\"id\":\"step\",\"kind\":\"waitFor\",\"automationId\":\"motif-pages\",\"condition\":\"missing\",\"timeoutMs\":1000}]}")]
    public void LoaderRejectsUnknownKindsAndFields(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"walkthrough-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        try
        {
            Assert.Throws<InvalidDataException>(() => WalkthroughScriptLoader.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Motif.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate Motif.sln.");
    }
}
