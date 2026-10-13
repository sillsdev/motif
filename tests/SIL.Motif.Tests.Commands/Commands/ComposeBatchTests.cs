using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class ComposeBatchTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";

    [Fact]
    public void ABatchLeavesTheDraftExactlyAsOneCallPerIntentWould()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        cache.Dispose();
        var sequentialPhoneme = StartDraft(path, "one-at-a-time");
        var batchPhoneme = StartDraft(path, "batched");

        foreach (var (verb, intent) in Intents(sequentialPhoneme))
        {
            var single = RunSingle(path, "one-at-a-time", verb, intent);
            Assert.True(single.Succeeded, single.Refusal?.Message);
        }
        var batch = ProposalCommands.ComposeBatch(new(path, Version, "batched", BatchJson(Intents(batchPhoneme))));
        Assert.True(batch.Succeeded, batch.Refusal?.Message);

        Assert.Equal(Normalized(path, "one-at-a-time"), Normalized(path, "batched"));
        Assert.Equal(4, batch.Value!.Intents.Count);
        Assert.Equal(["compose-author-phoneme", "compose-author-natural-class", "compose-author-environment",
            "compose-author-phoneme"], batch.Value.Intents.Select(intent => intent.Verb));
        Assert.Equal(ReadDraft(path, "batched").Operations.Count, batch.Value.OperationCount);
    }

    [Fact]
    public void ALaterIntentSeesAnEarlierOneAndAFailureAddsNothing()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        cache.Dispose();
        var phoneme = StartDraft(path, "all-or-nothing");
        var before = ReadDraft(path, "all-or-nothing");

        var refused = ProposalCommands.ComposeBatch(new(path, Version, "all-or-nothing", BatchJson(
        [
            ("compose-author-natural-class", new { name = "vowels", abbreviation = "V", members = new[] { phoneme } }),
            ("compose-author-natural-class", new { name = "again", abbreviation = "V", members = new[] { phoneme } }),
        ])));

        Assert.False(refused.Succeeded);
        Assert.Equal("draft.invalid", refused.Refusal!.Code);
        Assert.Contains("Intent 1 (compose-author-natural-class)", refused.Refusal.Message);
        Assert.Contains("already exists", refused.Refusal.Message);
        var after = ReadDraft(path, "all-or-nothing");
        Assert.Equal(before.Operations.Select(operation => operation.OperationId),
            after.Operations.Select(operation => operation.OperationId));
        Assert.Equal(before.ComposerProvenance.Count, after.ComposerProvenance.Count);
    }

    [Fact]
    public void ARefusedSingleEntryBatchNamesItsIndexAndVerb()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        cache.Dispose();
        StartDraft(path, "one-refused");
        var before = ReadDraft(path, "one-refused");

        var refused = ProposalCommands.ComposeBatch(new(path, Version, "one-refused", BatchJson(
            [("compose-author-phoneme", new { name = "again", representations = new[] { "a" } })])));

        Assert.Equal("draft.invalid", refused.Refusal?.Code);
        Assert.Contains("Intent 0 (compose-author-phoneme)", refused.Refusal!.Message);
        Assert.Equal("0", refused.Refusal.Facts["intentIndex"]);
        Assert.Equal("compose-author-phoneme", refused.Refusal.Facts["verb"]);
        Assert.Equal(before.Operations.Select(operation => operation.OperationId),
            ReadDraft(path, "one-refused").Operations.Select(operation => operation.OperationId));
    }

    [Fact]
    public void ManyIntentsUnderOneOwnerChainTheirOrderInsteadOfDependingOnEveryEarlierOne()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        cache.Dispose();
        StartDraft(path, "chained");
        var intents = Enumerable.Range(0, 25).Select(index => ("compose-author-phoneme",
            (object)new { name = $"p{index}", representations = new[] { $"x{index}" } })).ToArray();

        var batch = ProposalCommands.ComposeBatch(new(path, Version, "chained", BatchJson(intents)));

        Assert.True(batch.Succeeded, batch.Refusal?.Message);
        var operations = ReadDraft(path, "chained").Operations;
        var byId = operations.ToDictionary(operation => operation.OperationId);
        var creates = operations.Where(operation => operation.Kind.EndsWith("/createPhonemes", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(26, creates.Length);
        Assert.All(operations, operation => Assert.True(operation.DependsOn.Count <= 4, operation.Kind));
        for (var index = 1; index < creates.Length; index++)
            Assert.Contains(creates[index - 1].OperationId, Ancestors(byId, creates[index].OperationId));
    }

    private static HashSet<string> Ancestors(IReadOnlyDictionary<string, DraftOperation> byId, string operationId)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(byId[operationId].DependsOn);
        while (pending.Count > 0)
            if (pending.Pop() is var next && found.Add(next))
                foreach (var parent in byId[next].DependsOn) pending.Push(parent);
        return found;
    }

    [Fact]
    public void ChainedDependenciesKeepCreatorsAndTheFullRemovalClosureAfterReorderedDryRun()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        cache.Dispose();
        var firstPhoneme = StartDraft(path, "closure");
        var intents = Enumerable.Range(0, 8).Select(index => ("compose-author-phoneme",
            (object)new { name = $"p{index}", representations = new[] { $"x{index}" } })).ToArray();
        var batch = ProposalCommands.ComposeBatch(new(path, Version, "closure", BatchJson(intents)));
        Assert.True(batch.Succeeded, batch.Refusal?.Message);
        var independent = ProposalCommands.ComposeAuthorNaturalClass(new(path, Version, "closure",
            JsonSerializer.Serialize(new { name = "first", abbreviation = "F", members = new[] { firstPhoneme } })));
        Assert.True(independent.Succeeded, independent.Refusal?.Message);
        var before = ReadDraft(path, "closure");
        var creators = before.Operations.Where(operation => operation.EntityId is not null)
            .ToDictionary(operation => operation.EntityId!, operation => operation.OperationId);
        foreach (var operation in before.Operations.Where(operation =>
                     operation.Target is { } target && creators.ContainsKey(target)))
            Assert.Contains(creators[operation.Target!], operation.DependsOn);
        ReplayReversed(path, before);

        var pivot = batch.Value!.Intents[3].Operations.Single(operation =>
            operation.Kind.EndsWith("/createPhonemes", StringComparison.Ordinal)).OperationId;
        var expectedRemoved = batch.Value.Intents.Skip(3).SelectMany(intent => intent.Operations)
            .Select(operation => operation.OperationId).ToHashSet(StringComparer.Ordinal);
        var refused = ProposalCommands.RemoveOperations(new(path, Version, "closure", [pivot], Force: false));
        Assert.Equal("operation.invalid-dependency", refused.Refusal?.Code);
        Assert.Contains(batch.Value.Intents[^1].Operations[0].OperationId, refused.Refusal!.Message);
        Assert.Equal(before.Operations.Select(operation => operation.OperationId),
            ReadDraft(path, "closure").Operations.Select(operation => operation.OperationId));

        var removed = ProposalCommands.RemoveOperations(new(path, Version, "closure", [pivot], Force: true));
        Assert.True(removed.Succeeded, removed.Refusal?.Message);
        Assert.Equal(expectedRemoved.Order(StringComparer.Ordinal), removed.Value!.RemovedOperationIds
            .Concat(removed.Value.ForcedDependents.Select(operation => operation.OperationId)).Order(StringComparer.Ordinal));
        var after = ReadDraft(path, "closure");
        Assert.Equal(before.Operations.Where(operation => !expectedRemoved.Contains(operation.OperationId))
            .Select(operation => operation.OperationId), after.Operations.Select(operation => operation.OperationId));
        Assert.All(independent.Value!.Operations, operation =>
            Assert.Contains(after.Operations, retained => retained.OperationId == operation.OperationId));
        ReplayReversed(path, after);
    }

    private static void ReplayReversed(string path, DraftDocument draft)
    {
        var proposal = ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(draft));
        var reversed = new Proposal(proposal.ContractVersions, proposal.ProposalId, proposal.Requires,
            proposal.Operations.Reverse().ToArray(), proposal.Extensions);
        using var scratch = DryRunScratch.Adopt(
            new SIL.Motif.Host.LcmUtils.FwDataProjectLoader().LoadScratchCache(path), "reordered batch dependencies");
        Assert.NotEmpty(ProposalDryRunner.Run(scratch, reversed).ExpectedEffects);
        var expectedPhonemes = proposal.Operations.Where(operation =>
            operation.Kind.EndsWith("/createPhonemes", StringComparison.Ordinal))
            .Select(operation => operation.EntityId!.Value.ToGuid()).Order().ToArray();
        Assert.Equal(expectedPhonemes, scratch.PeekCache().LangProject.PhonologicalDataOA.PhonemeSetsOS
            .SelectMany(set => set.PhonemesOC).Select(phoneme => phoneme.Guid).Order());
    }

    [Theory]
    [InlineData("""{"intents":[]}""", "at least one intent")]
    [InlineData("""{"intents":[{"verb":"add-set-gloss","intent":{}}]}""", "not a verb a batch can run")]
    [InlineData("""{"intents":[{"verb":"compose-author-phoneme","intent":{},"note":"x"}]}""", "exactly 'verb'")]
    [InlineData("""{"intents":[],"draft":"x"}""", "one property, 'intents'")]
    [InlineData("""[{"verb":"compose-author-phoneme","intent":{}}]""", "one property, 'intents'")]
    public void ABatchFileIsClosed(string json, string expected)
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        cache.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, "closed", "Closed batch shape")).Succeeded);

        var refused = ProposalCommands.ComposeBatch(new(path, Version, "closed", json));

        Assert.False(refused.Succeeded);
        Assert.Contains(expected, refused.Refusal!.Message);
        Assert.Empty(ReadDraft(path, "closed").Operations);
    }

    [Fact]
    public void EveryComposingVerbOutsideParsimonyCanRunInABatch()
    {
        var composing = CommandCatalog.All
            .Where(command => command.ResponseType == typeof(ComposedOperationsResponse))
            .Select(command => command.Name)
            .Where(name => !name.StartsWith("parsimony ", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

        Assert.Equal(composing, ProposalCommands.DraftComposers.Keys.Order(StringComparer.Ordinal));
    }

    private static string StartDraft(string path, string draft)
    {
        Assert.True(ProposalCommands.New(new(path, Version, draft, "Batch comparison")).Succeeded);
        var phoneme = ProposalCommands.ComposeAuthorPhoneme(new(path, Version, draft,
            JsonSerializer.Serialize(new { name = "a", representations = new[] { "a" } })));
        Assert.True(phoneme.Succeeded, phoneme.Refusal?.Message);
        return phoneme.Value!.Operations.Single(operation =>
            operation.Kind.EndsWith("/createPhonemes", StringComparison.Ordinal)).EntityId!;
    }

    private static (string Verb, object Intent)[] Intents(string phoneme) =>
    [
        ("compose-author-phoneme", new { name = "i", representations = new[] { "i" } }),
        ("compose-author-natural-class", new { name = "front", abbreviation = "F", members = new[] { phoneme } }),
        ("compose-author-environment", new
        {
            name = "after a", left = new[] { new { phoneme } }, right = Array.Empty<object>(),
        }),
        ("compose-author-phoneme", new { name = "u", representations = new[] { "u" } }),
    ];

    private static string BatchJson((string Verb, object Intent)[] intents) =>
        JsonSerializer.Serialize(new { intents = intents.Select(item => new { verb = item.Verb, intent = item.Intent }) });

    private static CommandOutcome<ComposedOperationsResponse> RunSingle(string path, string draft, string verb,
        object intent)
    {
        var json = JsonSerializer.Serialize(intent);
        return verb switch
        {
            "compose-author-phoneme" => ProposalCommands.ComposeAuthorPhoneme(new(path, Version, draft, json)),
            "compose-author-natural-class" => ProposalCommands.ComposeAuthorNaturalClass(new(path, Version, draft, json)),
            "compose-author-environment" => ProposalCommands.ComposeAuthorEnvironment(new(path, Version, draft, json)),
            _ => throw new ArgumentOutOfRangeException(nameof(verb), verb, null),
        };
    }

    private static DraftDocument ReadDraft(string path, string draft)
    {
        var loaded = ProjectStoreCommand.Run(path, Version, (database, _) =>
            CommandOutcome<DraftDocument>.Success(ProposalCommands.DeserializeDraft(
                new ProposalRepository(database).GetDraft(draft).ProposalJson!)));
        Assert.True(loaded.Succeeded, loaded.Refusal?.Message);
        return loaded.Value!;
    }

    // Minted identities differ between the two Drafts, so each is replaced by its first-appearance order.
    private static string Normalized(string path, string draft)
    {
        var document = ReadDraft(path, draft);
        var text = JsonSerializer.Serialize(new { document.Operations, document.ComposerProvenance });
        var tokens = document.Operations
            .SelectMany(operation => new[] { operation.OperationId, operation.EntityId })
            .OfType<string>().Distinct().Select((id, index) => (id, token: $"#{index}"));
        foreach (var (id, token) in tokens)
            text = text.Replace(id, token, StringComparison.Ordinal);
        return text;
    }
}
