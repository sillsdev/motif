using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Trait("MotifTestLevel", "Unit")]
public sealed class IntentShapeTests
{
    private static readonly IntentShape Placement = IntentShape.Of(
        Field.Id("after", IdRole.Reference), Field.Text("note"));

    private static readonly IntentShape Shape = IntentShape.Of(
        Field.Id("target", IdRole.Target, required: true),
        Field.Ids("members"),
        Field.Id("chosenId", IdRole.Opaque),
        Field.Text("name", required: true),
        Field.Flag("optional"),
        Field.Choice("kind", ["a", "b"]),
        Field.Nested("placement", Placement),
        Field.Nested("steps", Placement, many: true))
        .Together("name", "optional");

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private static string Id(int _) => CanonicalId.Mint().Value;

    [Fact]
    public void RequireClosedAcceptsDeclaredProperties() =>
        Shape.RequireClosed(Json("""{"target":"x","name":"n","optional":true,"placement":{"after":"y"}}"""), "Demo");

    [Fact]
    public void RequireClosedRefusesAnUnknownTopLevelProperty()
    {
        var error = Assert.Throws<ContractParseException>(() =>
            Shape.RequireClosed(Json("""{"target":"x","bogus":1}"""), "Demo"));
        Assert.Equal("'Demo': unknown property 'bogus'.", error.Message);
    }

    [Fact]
    public void RequireClosedRefusesAnUnknownNestedProperty()
    {
        var error = Assert.Throws<ContractParseException>(() =>
            Shape.RequireClosed(Json("""{"placement":{"after":"y","bogus":1}}"""), "Demo"));
        Assert.Equal("'Demo.placement': unknown property 'bogus'.", error.Message);
    }

    [Fact]
    public void RequireClosedRefusesAnUnknownPropertyInsideAnArrayOfNestedObjects()
    {
        var error = Assert.Throws<ContractParseException>(() =>
            Shape.RequireClosed(Json("""{"steps":[{"after":"y"},{"bogus":1}]}"""), "Demo"));
        Assert.Equal("'Demo.steps': unknown property 'bogus'.", error.Message);
    }

    [Fact]
    public void TogetherRefusesAPartlyGivenGroupAndAcceptsAnEmptyOrFullOne()
    {
        var error = Assert.Throws<ContractParseException>(() =>
            Shape.RequireClosed(Json("""{"name":"n"}"""), "Demo"));
        Assert.Equal("'Demo': 'name', 'optional' go together.", error.Message);
        Shape.RequireClosed(Json("""{"target":"x"}"""), "Demo");
        Shape.RequireClosed(Json("""{"name":"n","optional":false}"""), "Demo");
    }

    [Fact]
    public void TogetherNamingAnUnknownFieldIsRefusedAtConstruction() =>
        Assert.Throws<ArgumentException>(() => IntentShape.Of(Field.Text("a")).Together("a", "missing"));

    [Fact]
    public void ReferencesYieldsReferenceAndTargetIdsAtEveryDepthButNotOpaqueOnes()
    {
        var target = Id(1);
        var member = Id(2);
        var chosen = Id(3);
        var after = Id(4);
        var step = Id(5);
        var authored = Json($$"""
            {"target":"{{target}}","members":["{{member}}","not-an-id"],"chosenId":"{{chosen}}",
             "placement":{"after":"{{after}}"},"steps":[{"after":"{{step}}"}]}
            """);
        var found = Shape.References(authored).Select(id => id.Value).ToArray();
        Assert.Equal(new[] { target, member, after, step }, found);
        Assert.DoesNotContain(chosen, found);
    }

    [Fact]
    public void ReferencesSkipsAnUnparsableIdInsteadOfThrowing() =>
        Assert.Empty(Shape.References(Json("""{"target":"nonsense"}""")));

    [Fact]
    public void ToJsonSchemaIsClosedAtEveryDepthAndListsRequiredFields()
    {
        var schema = Shape.ToJsonSchema();
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(new[] { "target", "name" }, schema["required"]!.AsArray().Select(n => n!.GetValue<string>()));
        var properties = schema["properties"]!.AsObject();
        Assert.False(properties["placement"]!["additionalProperties"]!.GetValue<bool>());
        Assert.Equal("array", properties["steps"]!["type"]!.GetValue<string>());
        Assert.False(properties["steps"]!["items"]!["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(new[] { "a", "b" }, properties["kind"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal("array", properties["members"]!["type"]!.GetValue<string>());
        Assert.Equal("boolean", properties["optional"]!["type"]!.GetValue<string>());
        Assert.Equal(new[] { "optional" },
            schema["dependentRequired"]!["name"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Theory]
    [InlineData("Target")]
    [InlineData("snake_case")]
    [InlineData("kebab-case")]
    [InlineData("")]
    [InlineData("1abc")]
    public void FieldNamesMustBeCamelCase(string name) =>
        Assert.Throws<ArgumentException>(() => IntentShape.Of(Field.Text(name)));

    [Fact]
    public void FieldNamesMustBeUnique() =>
        Assert.Throws<ArgumentException>(() => IntentShape.Of(Field.Text("a"), Field.Flag("a")));
}

[Trait("MotifTestLevel", "Unit")]
public sealed class ReplacementRuleTests
{
    private static OperationEnvelope Op(CanonicalId target, string kind) =>
        new(CanonicalId.Mint(), kind, target: target);

    [Fact]
    public void SameTargetMatchesOnlyTheListedKindsOnTheIntentsTarget()
    {
        var target = CanonicalId.Mint();
        var other = CanonicalId.Mint();
        var rule = ReplacementRule.SameTarget(
            MoInflAffMsaSlotsOperationKinds.AddRefSlots, MoInflAffMsaSlotsOperationKinds.RemoveRefSlots);
        var authored = JsonDocument.Parse($$"""{"target":"{{target}}"}""").RootElement;
        Assert.True(rule.Matches(authored, Op(target, MoInflAffMsaSlotsOperationKinds.AddRefSlots)));
        Assert.True(rule.Matches(authored, Op(target, MoInflAffMsaSlotsOperationKinds.RemoveRefSlots)));
        Assert.False(rule.Matches(authored, Op(other, MoInflAffMsaSlotsOperationKinds.AddRefSlots)));
        Assert.False(rule.Matches(authored, Op(target, LexEntryAlternateFormsOperationKinds.MoveAlternateForms)));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"target":5}""")]
    [InlineData("""{"target":"nonsense"}""")]
    public void SameTargetNeverMatchesWithoutAWellFormedTarget(string json) =>
        Assert.False(ReplacementRule.SameTarget(MoInflAffMsaSlotsOperationKinds.AddRefSlots).Matches(
            JsonDocument.Parse(json).RootElement,
            Op(CanonicalId.Mint(), MoInflAffMsaSlotsOperationKinds.AddRefSlots)));
}

[Trait("MotifTestLevel", "Unit")]
public sealed class LegacyComposerAdapterTests
{
    [Fact]
    public void TheAdapterDelegatesToTheRecordAndDeclaresNoShape()
    {
        var calls = new List<string>();
        var legacy = new DraftComposer("verb", "Name", ["sound-inventory"],
            (_, _, _) => { calls.Add("build"); return []; },
            ValidateAppend: (_, _, _) => calls.Add("validate"),
            ReplacePrevious: (_, _) => true,
            RelatedObjects: (_, _) => [new("Kind", "id", "Name")]);
        IComposerDefinition adapter = new LegacyComposerAdapter(legacy);
        var authored = JsonDocument.Parse("{}").RootElement;
        Assert.Equal("verb", adapter.Verb);
        Assert.Equal("Name", adapter.Composer);
        Assert.Null(adapter.Shape);
        Assert.Null(adapter.Exposure);
        Assert.True(adapter.CanReplacePrevious);
        Assert.Empty(adapter.Compose(null!, authored, CanonicalId.Mint(), () => CanonicalId.Mint()));
        adapter.ValidateAppend([], authored, []);
        Assert.Equal(new[] { "build", "validate" }, calls);
        Assert.True(adapter.ReplacePrevious(authored, new OperationEnvelope(CanonicalId.Mint(), "group/x")));
        Assert.Single(adapter.RelatedObjects(null!, authored)!);
    }

    [Fact]
    public void ARecordWithoutOptionalDelegatesReplacesNothingAndReportsNothing()
    {
        IComposerDefinition adapter = new LegacyComposerAdapter(new DraftComposer("v", "N", ["sound-inventory"], (_, _, _) => []));
        var authored = JsonDocument.Parse("{}").RootElement;
        Assert.False(adapter.CanReplacePrevious);
        Assert.False(adapter.ReplacePrevious(authored, new OperationEnvelope(CanonicalId.Mint(), "group/x")));
        Assert.Null(adapter.RelatedObjects(null!, authored));
        adapter.ValidateAppend([], authored, []);
    }

    [Fact]
    public void TheRegistryHoldsEveryVerbAsATypedDefinitionOrThroughTheAdapter()
    {
        Assert.Equal(49, ProposalCommands.DraftComposers.Count);
        Assert.All(ProposalCommands.DraftComposers, pair =>
        {
            Assert.Equal(pair.Key, pair.Value.Verb);
            Assert.Equal(ComposerDefinitions.All.Contains(pair.Value), pair.Value is not LegacyComposerAdapter);
        });
    }
}
