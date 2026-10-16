using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class ComposeCommandTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";
    private const string CreateRuleKind = "grammar/phPhonData/createPhonRules";

    private static ComposeStepRequest Step(string verb, object intent, string? label = null) =>
        new(verb, JsonSerializer.SerializeToElement(intent), label, null);

    [Fact]
    public void OneStepLeavesTheDraftExactlyAsComposeIntentDoes()
    {
        var setup = PrepareFeatures();
        var structures = new List<string>();
        foreach (var draft in new[] { "by-intent", "by-compose" })
        {
            Assert.True(ProposalCommands.New(new(setup.Path, Version, draft, "one step")).Succeeded);
            var structure = ProposalCommands.ComposeAuthorFeatureStructure(new(setup.Path, Version, draft,
                JsonSerializer.Serialize(new { msa = setup.StemMsa })));
            Assert.True(structure.Succeeded, structure.Refusal?.Message);
            structures.Add(structure.Value!.CreatedObjects.Single().Id);
        }
        var intent = new { featStruc = structures[0], feature = setup.Feature };
        var single = ProposalCommands.ComposeIntent(new(setup.Path, Version, "by-intent", "compose-author-feature-value",
            JsonSerializer.Serialize(intent)));
        var composed = ProposalCommands.Compose(new(setup.Path, Version, "by-compose",
            [Step("compose-author-feature-value", new { featStruc = structures[1], feature = setup.Feature })],
            null));

        Assert.True(single.Succeeded, single.Refusal?.Message);
        Assert.True(composed.Succeeded, composed.Refusal?.Message);
        var only = Assert.Single(composed.Value!.Intents);
        Assert.Equal("compose-author-feature-value", only.Verb);
        Assert.Equal(single.Value!.ComposerName, only.ComposerName);
        Assert.Equal(single.Value.Operations.Select(operation => operation.Kind),
            only.Operations.Select(operation => operation.Kind));
        Assert.Equal(single.Value.OperationCount, composed.Value.OperationCount);
        Assert.Equal(Normalized(setup.Path, "by-intent"), Normalized(setup.Path, "by-compose"));
    }

    [Fact]
    public void ALaterStepNamesAnEarlierRuleByItsLabel()
    {
        var (path, phoneme) = StartDraft("labelled");
        var rule = (string name, object? placement) => new Dictionary<string, object?>
        {
            ["name"] = name,
            ["direction"] = "simultaneous",
            ["input"] = new[] { new { phoneme } },
            ["output"] = new[] { new { phoneme } },
            ["left"] = new[] { new { phoneme } },
            ["right"] = new[] { new { phoneme } },
            ["placement"] = placement,
        }.Where(pair => pair.Value is not null).ToDictionary();

        var composed = ProposalCommands.Compose(new(path, Version, "labelled",
        [
            Step("compose-author-phonological-rule", rule("first", null), "r1"),
            Step("compose-author-phonological-rule", rule("second", new { after = "@r1" }), "r2"),
        ], null));

        Assert.True(composed.Succeeded, composed.Refusal?.Message);
        var creates = composed.Value!.Intents.Select(intent =>
            Assert.Single(intent.Operations, operation => operation.Kind == CreateRuleKind)).ToArray();
        var stored = ReadDraft(path, "labelled").Operations.Where(operation => operation.Kind == CreateRuleKind).ToArray();
        Assert.Contains(creates[0].OperationId, stored[1].DependsOn);
    }

    [Fact]
    public void RemovalAndNewStepsHappenTogetherOrNotAtAll()
    {
        var (path, _) = StartDraft("atomic");
        var extra = ProposalCommands.ComposeAuthorPhoneme(new(path, Version, "atomic",
            JsonSerializer.Serialize(new { name = "z", representations = new[] { "z" } })));
        Assert.True(extra.Succeeded, extra.Refusal?.Message);
        var removable = extra.Value!.Operations.Select(operation => operation.OperationId).ToArray();
        var before = ReadDraft(path, "atomic").Operations.Select(operation => operation.OperationId).ToArray();

        var refused = ProposalCommands.Compose(new(path, Version, "atomic",
        [
            Step("compose-author-phoneme", new { name = "y", representations = new[] { "y" } }),
            Step("compose-author-phoneme", new { name = "a", representations = new[] { "a" } }),
        ], removable));

        Assert.False(refused.Succeeded);
        Assert.Contains("Intent 1 (compose-author-phoneme)", refused.Refusal!.Message);
        Assert.Equal(before, ReadDraft(path, "atomic").Operations.Select(operation => operation.OperationId));

        var done = ProposalCommands.Compose(new(path, Version, "atomic",
            [Step("compose-author-phoneme", new { name = "y", representations = new[] { "y" } })], removable));

        Assert.True(done.Succeeded, done.Refusal?.Message);
        var after = ReadDraft(path, "atomic").Operations.Select(operation => operation.OperationId).ToArray();
        Assert.Empty(after.Intersect(removable));
        Assert.Equal(before.Except(removable).Count() + done.Value!.Intents.Single().Operations.Count, after.Length);
        Assert.Equal(after.Length, done.Value.OperationCount);
    }

    [Fact]
    public void AnUnknownRemovedOperationRefusesTheWholeCall()
    {
        var (path, _) = StartDraft("bad-remove");
        var before = ReadDraft(path, "bad-remove").Operations.Select(operation => operation.OperationId).ToArray();

        var refused = ProposalCommands.Compose(new(path, Version, "bad-remove",
            [Step("compose-author-phoneme", new { name = "y", representations = new[] { "y" } })],
            [CanonicalId.Mint().Value]));

        Assert.Equal("operation.invalid-id", refused.Refusal?.Code);
        Assert.Equal(before, ReadDraft(path, "bad-remove").Operations.Select(operation => operation.OperationId));
    }

    [Theory]
    [InlineData("not-a-verb", "not a verb a batch can run")]
    public void AnUnknownVerbIsRefusedWithItsIndex(string verb, string expected)
    {
        var (path, _) = StartDraft("unknown-verb");

        var refused = ProposalCommands.Compose(new(path, Version, "unknown-verb", [Step(verb, new { })], null));

        Assert.Equal("draft.invalid", refused.Refusal?.Code);
        Assert.Contains(expected, refused.Refusal!.Message);
        Assert.Equal("0", refused.Refusal.Facts["intentIndex"]);
    }

    [Fact]
    public void ACallNeedsAtLeastOneStep()
    {
        var (path, _) = StartDraft("no-steps");

        var refused = ProposalCommands.Compose(new(path, Version, "no-steps", [], null));

        Assert.Equal("draft.invalid", refused.Refusal?.Code);
    }

    [Fact]
    public void StepsParseFromJsonWithLabelsAndCitationsAndRefuseUnknownProperties()
    {
        var steps = ComposeStepRequest.ParseList(
            """[{"verb":"compose-author-phoneme","intent":{"name":"a"},"as":"x","cite":{"source":"Grammar","locator":"3"}}]""");

        var step = Assert.Single(steps);
        Assert.Equal("compose-author-phoneme", step.Verb);
        Assert.Equal("x", step.As);
        Assert.Equal("Grammar", step.Cite!.Source);
        Assert.Throws<InvalidOperationException>(() => ComposeStepRequest.ParseList("""[{"verb":"v","intent":{},"extra":1}]"""));
        Assert.Throws<InvalidOperationException>(() => ComposeStepRequest.ParseList("""{"verb":"v"}"""));
        Assert.Throws<InvalidOperationException>(() => ComposeStepRequest.ParseList("""[{"intent":{}}]"""));
    }

    private (string Path, string Phoneme) StartDraft(string draft)
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, draft, "Compose")).Succeeded);
        var phoneme = ProposalCommands.ComposeAuthorPhoneme(new(path, Version, draft,
            JsonSerializer.Serialize(new { name = "a", representations = new[] { "a" } })));
        Assert.True(phoneme.Succeeded, phoneme.Refusal?.Message);
        return (path, phoneme.Value!.Operations.Single(operation =>
            operation.Kind.EndsWith("/createPhonemes", StringComparison.Ordinal)).EntityId!);
    }

    private sealed record Fixture(string Path, string StemMsa, string Feature);

    private Fixture PrepareFeatures()
    {
        using var cache = pristine.NewScratch();
        var anal = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        var featureOps = AuthorInflectionFeatureComposer.Build(cache, new("synthetic-nc", anal, [new("seven")]));
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            foreach (var operation in featureOps)
                OperationHandlerRegistry.Resolve(operation.Kind, "compose command fixture")
                    .ApplyAndCaptureEffect(cache, operation, []);
        });
        new FwDataProjectLoader().Save(cache);
        return new(cache.ProjectId.Path,
            CanonicalId.FromGuid(cache.ServiceLocator.GetInstance<ILexSenseRepository>()
                .GetObject(pristine.Seed.FirstSenseId).MorphoSyntaxAnalysisRA.Guid).Value,
            featureOps.Single(operation => operation.Kind == FsFeatureSystemFeaturesOperationKinds.Create).EntityId!.Value.Value);
    }

    private static DraftDocument ReadDraft(string path, string name)
    {
        using var database = ProjectMotifDatabase.Open(path);
        return ProposalCommands.DeserializeDraft(new ProposalRepository(database).GetDraft(name).ProposalJson!);
    }

    // Minted identities differ between Drafts, so each is replaced by its first-appearance order.
    private static string Normalized(string path, string draft)
    {
        var document = ReadDraft(path, draft);
        var text = JsonSerializer.Serialize(new { document.Operations, document.ComposerProvenance });
        var tokens = document.Operations
            .SelectMany(operation => new[] { operation.OperationId, operation.EntityId, operation.Target })
            .OfType<string>().Distinct().Select((id, index) => (id, token: $"#{index}"));
        foreach (var (id, token) in tokens)
            text = text.Replace(id, token, StringComparison.Ordinal);
        return text;
    }
}
