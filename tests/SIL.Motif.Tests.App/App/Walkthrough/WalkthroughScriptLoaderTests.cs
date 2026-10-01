using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using SIL.Motif.Tests.TestFixtures;
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
        Assert.Equal(["visible", "hidden", "enabled", "text", "assessmentPublished"],
            stepVariants[2].GetProperty("properties").GetProperty("condition").GetProperty("enum")
                .EnumerateArray().Select(condition => condition.GetString()));
    }

    [Theory]
    [InlineData("{\"id\":\"invalid\",\"fixture\":\"fresh-project\",\"steps\":[{\"id\":\"step\",\"kind\":\"fly\"}]}")]
    [InlineData("{\"id\":\"invalid\",\"fixture\":\"fresh-project\",\"extra\":true,\"steps\":[{\"id\":\"step\",\"kind\":\"hold\",\"durationMs\":1}]}")]
    [InlineData("{\"id\":\"invalid\",\"fixture\":\"fresh-project\",\"steps\":[{\"id\":\"step\",\"kind\":\"hold\",\"durationMs\":1,\"x\":4}]}")]
    [InlineData("{\"id\":\"invalid\",\"fixture\":\"fresh-project\",\"steps\":[{\"id\":\"step\",\"kind\":\"highlight\",\"automationId\":\"9bad\"}]}")]
    [InlineData("{\"id\":\"9invalid\",\"fixture\":\"fresh-project\",\"steps\":[{\"id\":\"step\",\"kind\":\"hold\",\"durationMs\":1}]}")]
    [InlineData("{\"id\":\"invalid\",\"fixture\":\"fresh-project\",\"steps\":[{\"id\":\"step\",\"kind\":\"waitFor\",\"automationId\":\"motif-pages\",\"condition\":\"missing\",\"timeoutMs\":1000}]}")]
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

    [Fact]
    public void CommittedWalkthroughScriptsMatchTheirSchemaBranches()
    {
        var root = FindRepositoryRoot();
        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "walkthroughs", "walkthrough.schema.json")));
        var schemaRoot = schema.RootElement;
        Assert.Equal(["id", "fixture", "steps"], schemaRoot.GetProperty("required")
            .EnumerateArray().Select(item => item.GetString()));
        Assert.False(schemaRoot.GetProperty("additionalProperties").GetBoolean());
        var branches = schemaRoot.GetProperty("properties").GetProperty("steps")
            .GetProperty("items").GetProperty("oneOf").EnumerateArray()
            .ToDictionary(item => item.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString()!);
        foreach (var path in WalkthroughScriptLoader.Discover(root))
        {
            using var script = JsonDocument.Parse(File.ReadAllText(path));
            _ = WalkthroughScriptLoader.Load(path);
            AssertSchemaObject(script.RootElement, schemaRoot);
            foreach (var step in script.RootElement.GetProperty("steps").EnumerateArray())
                AssertSchemaObject(step, branches[step.GetProperty("kind").GetString()!]);
        }
    }

    [Fact]
    public void ExplainedWordCardWalkthroughCoversTheSixRoundThreeExamplesAndAStagedDecision()
    {
        var root = FindRepositoryRoot();
        var script = WalkthroughScriptLoader.Load(Path.Combine(root, "walkthroughs",
            "explained-word-card.walkthrough.json"));
        var captures = script.Steps.Where(step => step.Kind == WalkthroughStepKind.Capture).ToArray();
        var targets = captures.SelectMany(step => step.Callouts!).Select(callout => callout.AutomationId)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal("explained-word-card", script.Id);
        Assert.Equal("explained-word-card", script.Fixture);
        Assert.Equal(7, captures.Length);
        Assert.All(captures, step => Assert.True(step.Scale >= 2));
        Assert.Contains(captures.Single(step => step.Id == "approved-agrees").Callouts!, callout =>
            callout.AutomationId.EndsWith("-unread", StringComparison.Ordinal));
        Assert.Contains("motif-word-bc4183b60be45dceb3769d68cb9fcf88-0-67656c6469-opinion", targets);
        Assert.Contains("motif-word-bc4183b60be45dceb3769d68cb9fcf88-0-65766c6572-action", targets);
        Assert.Contains("motif-word-bc4183b60be45dceb3769d68cb9fcf88-0-6b6564697965-opinion", targets);
        Assert.Contains("motif-word-bc4183b60be45dceb3769d68cb9fcf88-0-6b6564697965-disapproved", targets);
        Assert.Contains("motif-word-bc4183b60be45dceb3769d68cb9fcf88-0-6164616d6c6172c4b16e6461-action", targets);
        Assert.Contains("motif-word-bc4183b60be45dceb3769d68cb9fcf88-0-67c3bc6e6c6572-pangloss", targets);
        Assert.Contains("motif-word-bc4183b60be45dceb3769d68cb9fcf88-0-6f6b756c6c6172c4b16e6461-pangloss", targets);
        Assert.Contains("motif-word-bc4183b60be45dceb3769d68cb9fcf88-0-65766c6572-staged", targets);
        Assert.DoesNotContain(script.Steps, step => step.Kind == WalkthroughStepKind.Click &&
            step.AutomationId == "motif-run-assessment");
        Assert.DoesNotContain(script.Steps, step => step.Kind == WalkthroughStepKind.WaitFor &&
            step.AutomationId == "motif-parse-all-words-progress");
    }

    [Theory]
    [InlineData("{\"id\":\"example\",\"fixture\":\"fresh-project\",\"steps\":[{\"id\":\"step\",\"kind\":\"type\",\"automationId\":\"motif-pages\",\"text\":\" \"}]}")]
    [InlineData("{\"id\":\"example\",\"fixture\":\"fresh-project\",\"steps\":[{\"id\":\"step\",\"kind\":\"waitFor\",\"automationId\":\"motif-pages\",\"condition\":\"text\",\"expectedText\":\" \",\"timeoutMs\":1}]}")]
    public void SchemaRejectsWhitespaceOnlyText(string json)
    {
        var root = FindRepositoryRoot();
        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "walkthroughs", "walkthrough.schema.json")));
        using var script = JsonDocument.Parse(json);
        var step = script.RootElement.GetProperty("steps")[0];
        var kindSchema = schema.RootElement.GetProperty("properties").GetProperty("steps")
            .GetProperty("items").GetProperty("oneOf").EnumerateArray()
            .Single(item => item.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString() ==
                step.GetProperty("kind").GetString());
        var propertyName = step.TryGetProperty("text", out _) ? "text" : "expectedText";
        var value = step.GetProperty(propertyName).GetString()!;
        var valueSchema = kindSchema.GetProperty("properties").GetProperty(propertyName);
        Assert.True(value.Length >= valueSchema.GetProperty("minLength").GetInt32());
        Assert.DoesNotMatch(valueSchema.GetProperty("pattern").GetString()!, value);
    }

    private static void AssertSchemaObject(JsonElement instance, JsonElement schema)
    {
        var properties = schema.GetProperty("properties");
        var allowed = properties.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.All(schema.GetProperty("required").EnumerateArray(), required =>
            Assert.True(instance.TryGetProperty(required.GetString()!, out _)));
        Assert.All(instance.EnumerateObject(), property =>
        {
            Assert.Contains(property.Name, allowed);
            AssertSchemaValue(property.Value, properties.GetProperty(property.Name));
        });
    }

    private static void AssertSchemaValue(JsonElement value, JsonElement schema)
    {
        if (schema.TryGetProperty("type", out var type))
        {
            if (type.GetString() == "number")
            {
                Assert.Equal(JsonValueKind.Number, value.ValueKind);
            }
            else Assert.Equal(type.GetString(), value.ValueKind switch
            {
                JsonValueKind.String => "string",
                JsonValueKind.Number when value.TryGetInt32(out _) => "integer",
                JsonValueKind.Array => "array",
                JsonValueKind.Object => "object",
                JsonValueKind.True or JsonValueKind.False => "boolean",
                _ => "null",
            });
        }
        if (schema.TryGetProperty("const", out var expected)) Assert.Equal(expected.GetString(), value.GetString());
        if (schema.TryGetProperty("enum", out var choices))
            Assert.Contains(value.GetString(), choices.EnumerateArray().Select(choice => choice.GetString()));
        if (schema.TryGetProperty("$ref", out var reference))
        {
            var target = reference.GetString()!.Split('/').Last();
            var schemaPath = Path.Combine(FindRepositoryRoot(), "walkthroughs", "walkthrough.schema.json");
            using var root = JsonDocument.Parse(File.ReadAllText(schemaPath));
            AssertSchemaValue(value, root.RootElement.GetProperty("$defs").GetProperty(target));
        }
        if (schema.TryGetProperty("pattern", out var pattern))
            Assert.Matches(pattern.GetString()!, value.GetString()!);
        if (schema.TryGetProperty("minLength", out var minLength))
            Assert.True(value.GetString()!.Length >= minLength.GetInt32());
        if (schema.TryGetProperty("maxLength", out var maxLength))
            Assert.True(value.GetString()!.Length <= maxLength.GetInt32());
        if (schema.TryGetProperty("minimum", out var minimum)) Assert.True(value.GetDouble() >= minimum.GetDouble());
        if (schema.TryGetProperty("maximum", out var maximum)) Assert.True(value.GetDouble() <= maximum.GetDouble());
        if (schema.TryGetProperty("minItems", out var minItems)) Assert.True(value.GetArrayLength() >= minItems.GetInt32());
    }

    [Fact]
    public void LoaderRejectsAutomationNamesAsClickTargets()
    {
        var path = WriteScript("{\"id\":\"invalid\",\"fixture\":\"fresh-project\",\"steps\":[{\"id\":\"step\",\"kind\":\"click\",\"automationName\":\"Texts page\"}]}");
        try
        {
            Assert.Throws<InvalidDataException>(() => WalkthroughScriptLoader.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoaderReadsTheFixtureNameDeclaredByTheScript()
    {
        var path = WriteScript("{\"id\":\"example\",\"fixture\":\"fresh-project\",\"steps\":[{\"id\":\"open-menu\",\"kind\":\"click\",\"automationId\":\"motif-project-menu\"}]}");
        try
        {
            var script = WalkthroughScriptLoader.Load(path);

            var fixture = typeof(WalkthroughScript).GetProperty("Fixture");
            Assert.NotNull(fixture);
            Assert.Equal("fresh-project", fixture.GetValue(script));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void HelpKeysCalloutCaptionsByCaptureStepAndAutomationId()
    {
        var path = Path.Combine(FindRepositoryRoot(), "src", "SIL.Motif.Help", "Content", "en", "walkthroughs", "open-project-overview.json");
        using var help = JsonDocument.Parse(File.ReadAllText(path));

        var caption = help.RootElement.GetProperty("callouts").GetProperty("overview")
            .GetProperty("motif-overview-selection-word-count").GetString();

        Assert.Equal("Words in the Selection", caption);
    }

    [Fact]
    public void CloseUpCaptureDeclaresItsCropPadding()
    {
        var path = Path.Combine(FindRepositoryRoot(), "walkthroughs", "annotate-control.walkthrough.json");
        using var script = JsonDocument.Parse(File.ReadAllText(path));

        Assert.Equal(48, script.RootElement.GetProperty("steps").EnumerateArray().Last()
            .GetProperty("cropPadding").GetInt32());
    }

    [Fact]
    public void EveryDiscoveredScriptHasExactlyOneRunnableReplayWrapper()
    {
        var root = FindRepositoryRoot();
        var discoveredIds = WalkthroughScriptLoader.Discover(root)
            .Select(path =>
            {
                var id = WalkthroughScriptLoader.Load(path).Id;
                Assert.Equal($"{id}.walkthrough.json", Path.GetFileName(path));
                return id;
            })
            .ToArray();
        var registrations = typeof(WalkthroughScriptLoaderTests).Assembly.GetTypes()
            .Select(type => (Type: type,
                Registration: type.GetCustomAttribute<AuthoredWalkthroughIdAttribute>(inherit: false)))
            .Where(item => item.Registration is not null)
            .Select(item => (item.Type, Registration: item.Registration!))
            .ToArray();
        var registeredIds = registrations.Select(item => item.Registration.ScriptId).ToArray();

        Assert.Equal(discoveredIds.Length,
            discoveredIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(registeredIds.Length,
            registeredIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            discoveredIds.OrderBy(id => id, StringComparer.Ordinal),
            registeredIds.OrderBy(id => id, StringComparer.Ordinal));
        Assert.All(registrations, item =>
        {
            Assert.True(item.Type.IsClass && item.Type.IsPublic && !item.Type.IsAbstract &&
                !item.Type.ContainsGenericParameters, $"{item.Type.FullName} is not a runnable wrapper.");
            Assert.True(item.Type.Name.EndsWith("WalkthroughReplayTests", StringComparison.Ordinal),
                $"{item.Type.Name} is not selected by the authored media filter.");
            var collection = item.Type.CustomAttributes
                .Single(attribute => attribute.AttributeType == typeof(CollectionAttribute));
            var collectionName = Assert.IsType<string>(collection.ConstructorArguments.Single().Value);
            Assert.Equal(LcmCacheTestCollection.Name, collectionName);
            var fact = Assert.Single(item.Type.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
                method => method.CustomAttributes.Any(attribute =>
                    attribute.AttributeType == typeof(FactAttribute)));
            Assert.Empty(fact.GetParameters());
            Assert.False(fact.IsGenericMethod);
            Assert.Null(fact.GetCustomAttribute<FactAttribute>(inherit: false)?.Skip);
        });
    }

    [Fact]
    public void ReplayDeadlineAddsAllWaitsAndHoldDurations()
    {
        var path = WriteScript("""{"id":"example","fixture":"fresh-project","steps":[{"id":"wait","kind":"waitFor","automationId":"motif-pages","condition":"visible","timeoutMs":30000},{"id":"hold","kind":"hold","durationMs":10000}]}""");
        try
        {
            var script = WalkthroughScriptLoader.Load(path);
            var method = typeof(WalkthroughReplay).GetMethod("DeadlineBudget",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(method);

            var budget = (TimeSpan)method.Invoke(null, [script])!;

            Assert.True(budget >= TimeSpan.FromSeconds(40), $"Expected at least 40 seconds, got {budget}.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteScript(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"walkthrough-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Motif.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate Motif.sln.");
    }
}
