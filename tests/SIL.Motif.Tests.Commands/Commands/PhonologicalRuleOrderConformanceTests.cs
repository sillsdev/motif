using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Generator;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class PhonologicalRuleOrderConformanceTests(PristineProjectFixture pristine)
{
    private const string Version = "1.0";
    private const string DraftName = "research-rule-order";
    private const string CreateRuleKind = "grammar/phPhonData/createPhonRules";
    private const string PhonologicalRuleVerb = "compose-author-phonological-rule";

    [Fact]
    public void BatchLabelsResolveRulePlacementAndDeclareDependencies()
    {
        using var project = pristine.NewScratch();
        var path = project.ProjectId.Path;
        project.Dispose();
        const string sequentialDraftName = "research-rule-order-sequential";
        Assert.True(ProposalCommands.New(new(path, Version, DraftName, "Stage labeled sound rules")).Succeeded);
        Assert.True(ProposalCommands.New(new(path, Version, sequentialDraftName, "Stage the same sound rules singly")).Succeeded);
        var fixtureRoot = Path.Combine(RepoPaths.FindRepoRoot(), "tests", "conformance", "phonological-rule-order");
        var researchIds = BindSyntheticInventory(path, fixtureRoot);
        var sequentialIds = BindSyntheticInventory(path, fixtureRoot, sequentialDraftName);
        var sourceIntents = File.ReadAllLines(Path.Combine(fixtureRoot, "intents.jsonl"));
        Assert.Equal(6, sourceIntents.Length);

        var entries = new JsonArray();
        for (var index = 0; index < sourceIntents.Length; index++)
        {
            var intent = JsonNode.Parse(ReplaceIds(sourceIntents[index], researchIds))!.AsObject();
            if (index == 0) intent["name"] = "R1 literal @r1 name";
            if (index > 0)
                intent["placement"] = new JsonObject { ["after"] = $"@r{index}" };
            entries.Add(new JsonObject
            {
                ["verb"] = "compose-author-phonological-rule",
                ["as"] = $"r{index + 1}",
                ["intent"] = intent,
            });
        }
        var json = new JsonObject { ["intents"] = entries }.ToJsonString();

        var batch = ProposalCommands.ComposeBatch(new(path, Version, DraftName, json));

        Assert.True(batch.Succeeded, batch.Refusal?.Message);
        var ruleCreates = batch.Value!.Intents.Select(result => Assert.Single(result.Operations,
            operation => operation.Kind == CreateRuleKind)).ToArray();
        Assert.Equal(6, ruleCreates.Length);
        var draft = ReadDraft(path);
        var storedRules = draft.Operations.Where(operation => operation.Kind == CreateRuleKind).ToArray();
        Assert.Equal(ruleCreates.Select(operation => operation.OperationId),
            storedRules.Select(operation => operation.OperationId));
        for (var index = 1; index < ruleCreates.Length; index++)
            Assert.Contains(storedRules[index].DependsOn,
                dependency => dependency == ruleCreates[index - 1].OperationId);

        var ruleProvenance = draft.ComposerProvenance.Where(provenance =>
            provenance.GetProperty("composer").GetString() == "AuthorPhonologicalRule").ToArray();
        Assert.Equal(6, ruleProvenance.Length);
        Assert.All(ruleProvenance, provenance => Assert.False(provenance.GetProperty("input").TryGetProperty("as", out _)));
        for (var index = 1; index < ruleProvenance.Length; index++)
            Assert.Equal(ruleCreates[index - 1].EntityId,
                ruleProvenance[index].GetProperty("input").GetProperty("placement").GetProperty("after").GetString());

        var proposal = ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(draft));
        var reversed = new Proposal(proposal.ContractVersions, proposal.ProposalId, proposal.Requires,
            proposal.Operations.Reverse().ToArray(), proposal.Extensions);
        using var dryRunScratch = DryRunScratch.Adopt(
            new FwDataProjectLoader().LoadScratchCache(path), "labeled research-shaped rule order");
        var dryRun = ProposalDryRunner.Run(dryRunScratch, reversed);
        Assert.NotEmpty(dryRun.ExpectedEffects);
        Assert.Equal(ruleCreates.Select(operation => CanonicalId.Parse(operation.EntityId!).ToGuid()),
            dryRunScratch.PeekCache().LangProject.PhonologicalDataOA.PhonRulesOS.Select(rule => rule.Guid));
        Assert.Equal("R1 literal @r1 name", ruleProvenance[0].GetProperty("input").GetProperty("name").GetString());

        var sequentialRuleIds = new List<string>();
        foreach (var (sourceIntent, index) in sourceIntents.Select((intent, index) => (intent, index)))
        {
            var intent = JsonNode.Parse(ReplaceIds(sourceIntent, sequentialIds))!.AsObject();
            if (index == 0) intent["name"] = "R1 literal @r1 name";
            if (index > 0)
                intent["placement"] = new JsonObject { ["after"] = sequentialRuleIds[^1] };
            var composed = ProposalCommands.ComposeAuthorPhonologicalRule(new(path, Version, sequentialDraftName,
                intent.ToJsonString()));
            Assert.True(composed.Succeeded, composed.Refusal?.Message);
            sequentialRuleIds.Add(Assert.Single(composed.Value!.Operations,
                operation => operation.Kind == CreateRuleKind).EntityId!);
        }
        Assert.Equal(Normalized(path, sequentialDraftName), Normalized(path, DraftName));
    }

    [Fact]
    public void TwentyOneCharacterLabelsResolveAndPrefixedDraftAnchorsKeepTheirDependencies()
    {
        using var project = pristine.NewScratch();
        var path = project.ProjectId.Path;
        project.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, DraftName, "Keep long labels and prefixed anchors")).Succeeded);
        var fixtureRoot = Path.Combine(RepoPaths.FindRepoRoot(), "tests", "conformance", "phonological-rule-order");
        var ids = BindSyntheticInventory(path, fixtureRoot);
        var sourceIntents = File.ReadAllLines(Path.Combine(fixtureRoot, "intents.jsonl"));
        const string label = "abcdefghijklmnopqrstu";
        Assert.Equal(21, label.Length);

        var labeled = ProposalCommands.ComposeBatch(new(path, Version, DraftName, BatchJson(
            RuleBatchEntry(sourceIntents[0], ids, label),
            RuleBatchEntry(sourceIntents[1], ids, null, "@" + label))));

        Assert.True(labeled.Succeeded, labeled.Refusal?.Message);
        var firstTwoCreates = labeled.Value!.Intents.Select(result => Assert.Single(result.Operations,
            operation => operation.Kind == CreateRuleKind)).ToArray();
        var prefixedId = CanonicalId.FromGuid(CanonicalId.Parse(firstTwoCreates[1].EntityId!).ToGuid(),
            "@canonical/");
        var appended = ProposalCommands.ComposeBatch(new(path, Version, DraftName,
            BatchJson(RuleBatchEntry(sourceIntents[2], ids, null, prefixedId.Value))));

        Assert.True(appended.Succeeded, appended.Refusal?.Message);
        var thirdCreate = Assert.Single(appended.Value!.Intents[0].Operations,
            operation => operation.Kind == CreateRuleKind);
        var draft = ReadDraft(path);
        var storedThird = Assert.Single(draft.Operations,
            operation => operation.OperationId == thirdCreate.OperationId);
        Assert.Equal(prefixedId.Value, storedThird.Placement?.After);
        Assert.Contains(storedThird.DependsOn,
            dependency => dependency == firstTwoCreates[1].OperationId);
        using var thirdSourceIntent = JsonDocument.Parse(sourceIntents[2]);
        var thirdRuleName = thirdSourceIntent.RootElement.GetProperty("name").GetString();
        var thirdProvenance = Assert.Single(draft.ComposerProvenance, provenance =>
            provenance.GetProperty("composer").GetString() == "AuthorPhonologicalRule" &&
            provenance.GetProperty("input").GetProperty("name").GetString() == thirdRuleName);
        Assert.Equal(prefixedId.Value,
            thirdProvenance.GetProperty("input").GetProperty("placement").GetProperty("after").GetString());

        var allCreates = firstTwoCreates.Append(thirdCreate).ToArray();
        var proposal = ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(draft));
        var reversed = new Proposal(proposal.ContractVersions, proposal.ProposalId, proposal.Requires,
            proposal.Operations.Reverse().ToArray(), proposal.Extensions);
        using var scratch = DryRunScratch.Adopt(new FwDataProjectLoader().LoadScratchCache(path),
            "prefixed Draft rule anchor replay");
        _ = ProposalDryRunner.Run(scratch, reversed);
        Assert.Equal(allCreates.Select(operation => CanonicalId.Parse(operation.EntityId!).ToGuid()),
            scratch.PeekCache().LangProject.PhonologicalDataOA.PhonRulesOS.Select(rule => rule.Guid));
    }

    [Theory]
    [InlineData("unknown", 1, "Placement after label '@missing' is not declared in this batch")]
    [InlineData("forward", 0, "Placement after label '@r2' is a forward reference")]
    [InlineData("duplicate", 1, "label 'r1' is already declared")]
    [InlineData("unsupported", 0, "labels are supported only")]
    [InlineData("empty", 0, "'as' must be a label")]
    [InlineData("malformed", 0, "'as' must be a label")]
    [InlineData("long-label", 0, "'as' must be a label")]
    [InlineData("nonstring-as", 0, "'as' must be a label")]
    [InlineData("duplicate-as", 0, "an entry repeats a property")]
    [InlineData("case-sensitive", 1, "Placement after label '@R1' is not declared")]
    [InlineData("malformed-placement", 1, "label '@bad!' is malformed")]
    [InlineData("duplicate-placement", 1, "duplicate property 'after'")]
    [InlineData("duplicate-placement-object", 1, "duplicate property 'placement'")]
    [InlineData("phoneme-alias", 1, "is not a valid canonical id")]
    [InlineData("missing-placement", 1, "requires declared placement")]
    public void BatchLabelsRejectInvalidShapesAndReferencesWithoutChangingTheDraftOrProject(
        string scenario, int failingIndex, string expectedMessage)
    {
        using var project = pristine.NewScratch();
        var path = project.ProjectId.Path;
        project.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, DraftName, "Reject invalid rule labels")).Succeeded);
        var fixtureRoot = Path.Combine(RepoPaths.FindRepoRoot(), "tests", "conformance", "phonological-rule-order");
        var ids = BindSyntheticInventory(path, fixtureRoot);
        var intents = File.ReadAllLines(Path.Combine(fixtureRoot, "intents.jsonl"));
        var first = RuleBatchEntry(intents[0], ids, "r1");
        var second = RuleBatchEntry(intents[1], ids, "r2");
        string json;
        string failingVerb = PhonologicalRuleVerb;
        switch (scenario)
        {
            case "unknown":
                json = BatchJson(first, RuleBatchEntry(intents[1], ids, null, "@missing"));
                break;
            case "forward":
                json = BatchJson(RuleBatchEntry(intents[0], ids, "r1", "@r2"), second);
                break;
            case "duplicate":
                json = BatchJson(first, RuleBatchEntry(intents[1], ids, "r1"));
                break;
            case "unsupported":
                failingVerb = "compose-author-phoneme";
                json = new JsonObject
                {
                    ["intents"] = new JsonArray(new JsonObject
                    {
                        ["verb"] = failingVerb,
                        ["as"] = "r1",
                        ["intent"] = new JsonObject
                        {
                            ["name"] = "extra",
                            ["representations"] = new JsonArray("x"),
                        },
                    }),
                }.ToJsonString();
                break;
            case "empty":
                json = BatchJson(RuleBatchEntry(intents[0], ids, ""));
                break;
            case "malformed":
                json = BatchJson(RuleBatchEntry(intents[0], ids, "r1!"));
                break;
            case "long-label":
                json = BatchJson(RuleBatchEntry(intents[0], ids, "abcdefghijklmnopqrstuv"));
                break;
            case "nonstring-as":
                var nonstringIntent = ReplaceIds(intents[0], ids);
                json = "{\"intents\":[{\"verb\":\"" + PhonologicalRuleVerb +
                       "\",\"as\":1,\"intent\":" + nonstringIntent + "}]}" ;
                break;
            case "duplicate-as":
                var duplicateAsIntent = ReplaceIds(intents[0], ids);
                json = "{\"intents\":[{\"verb\":\"" + PhonologicalRuleVerb +
                       "\",\"as\":\"r1\",\"as\":\"r1\",\"intent\":" + duplicateAsIntent + "}]}" ;
                break;
            case "case-sensitive":
                json = BatchJson(first, RuleBatchEntry(intents[1], ids, null, "@R1"));
                break;
            case "malformed-placement":
                json = BatchJson(first, RuleBatchEntry(intents[1], ids, null, "@bad!"));
                break;
            case "duplicate-placement":
                var rawIntent = ReplaceIds(intents[1], ids);
                var end = rawIntent.LastIndexOf('}');
                rawIntent = rawIntent[..end] + ",\"placement\":{\"after\":\"@r1\",\"after\":\"@r1\"}" +
                            rawIntent[end..];
                json = "{\"intents\":[" + first.ToJsonString() +
                       ",{\"verb\":\"compose-author-phonological-rule\",\"as\":\"r2\",\"intent\":" +
                       rawIntent + "}]}";
                break;
            case "duplicate-placement-object":
                var duplicatePlacementIntent = ReplaceIds(intents[1], ids);
                var placementInsertAt = duplicatePlacementIntent.LastIndexOf('}');
                duplicatePlacementIntent = duplicatePlacementIntent[..placementInsertAt] +
                    ",\"placement\":{\"after\":\"@r1\"},\"placement\":{\"after\":\"@r1\"}" +
                    duplicatePlacementIntent[placementInsertAt..];
                json = "{\"intents\":[" + first.ToJsonString() +
                       ",{\"verb\":\"" + PhonologicalRuleVerb +
                       "\",\"as\":\"r2\",\"intent\":" + duplicatePlacementIntent + "}]}" ;
                break;
            case "phoneme-alias":
                var phonemeAliasIntent = JsonNode.Parse(ReplaceIds(intents[1], ids))!.AsObject();
                phonemeAliasIntent["input"]![0]!["phoneme"] = "@r1";
                phonemeAliasIntent["placement"] = new JsonObject { ["after"] = "@r1" };
                json = BatchJson(first, new JsonObject
                {
                    ["verb"] = PhonologicalRuleVerb,
                    ["as"] = "r2",
                    ["intent"] = phonemeAliasIntent,
                });
                break;
            case "missing-placement":
                json = BatchJson(first, RuleBatchEntry(intents[1], ids, null));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }
        var before = ReadDraft(path);

        var refused = ProposalCommands.ComposeBatch(new(path, Version, DraftName, json));

        Assert.False(refused.Succeeded);
        Assert.Equal("draft.invalid", refused.Refusal!.Code);
        Assert.Contains(expectedMessage, refused.Refusal.Message, StringComparison.Ordinal);
        Assert.Contains($"Intent {failingIndex} ({failingVerb})", refused.Refusal.Message, StringComparison.Ordinal);
        Assert.Equal(failingIndex.ToString(), refused.Refusal.Facts["intentIndex"]);
        Assert.Equal(failingVerb, refused.Refusal.Facts["verb"]);
        var after = ReadDraft(path);
        Assert.Equal(before.Operations.Select(operation => operation.OperationId),
            after.Operations.Select(operation => operation.OperationId));
        Assert.Equal(before.ComposerProvenance.Count, after.ComposerProvenance.Count);
        using var live = new FwDataProjectLoader().LoadScratchCache(path);
        Assert.Empty(live.LangProject.PhonologicalDataOA.PhonRulesOS);
    }

    [Fact]
    public void LabelsAreLocalToOneBatchAndCanBeReusedInAnother()
    {
        using var project = pristine.NewScratch();
        var path = project.ProjectId.Path;
        project.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, DraftName, "Keep labels file-local")).Succeeded);
        var fixtureRoot = Path.Combine(RepoPaths.FindRepoRoot(), "tests", "conformance", "phonological-rule-order");
        var ids = BindSyntheticInventory(path, fixtureRoot);
        var intents = File.ReadAllLines(Path.Combine(fixtureRoot, "intents.jsonl"));

        var first = ProposalCommands.ComposeBatch(new(path, Version, DraftName,
            BatchJson(RuleBatchEntry(intents[0], ids, "r1"))));
        Assert.True(first.Succeeded, first.Refusal?.Message);
        var firstRule = Assert.Single(first.Value!.Intents[0].Operations,
            operation => operation.Kind == CreateRuleKind);
        var second = ProposalCommands.ComposeBatch(new(path, Version, DraftName,
            BatchJson(RuleBatchEntry(intents[1], ids, "r1", firstRule.EntityId))));
        Assert.True(second.Succeeded, second.Refusal?.Message);
        Assert.Single(second.Value!.Intents[0].Operations, operation => operation.Kind == CreateRuleKind);
        var beforeCrossFileReference = ReadDraft(path);

        var crossFile = ProposalCommands.ComposeBatch(new(path, Version, DraftName,
            BatchJson(RuleBatchEntry(intents[2], ids, "r3", "@r1"))));

        Assert.False(crossFile.Succeeded);
        Assert.Equal("draft.invalid", crossFile.Refusal!.Code);
        Assert.Contains("Intent 0 (compose-author-phonological-rule)", crossFile.Refusal.Message);
        Assert.Contains("not declared in this batch", crossFile.Refusal.Message);
        Assert.Equal("0", crossFile.Refusal.Facts["intentIndex"]);
        Assert.Equal(PhonologicalRuleVerb, crossFile.Refusal.Facts["verb"]);
        Assert.Equal(beforeCrossFileReference.Operations.Select(operation => operation.OperationId),
            ReadDraft(path).Operations.Select(operation => operation.OperationId));
        using var live = new FwDataProjectLoader().LoadScratchCache(path);
        Assert.Empty(live.LangProject.PhonologicalDataOA.PhonRulesOS);
    }

    [Fact]
    public void InteriorRulePlacementUsesBothLabelsAndRemovalFollowsItsDeclaredDependencies()
    {
        using var project = pristine.NewScratch();
        var path = project.ProjectId.Path;
        project.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, DraftName, "Place a rule between two rules")).Succeeded);
        var fixtureRoot = Path.Combine(RepoPaths.FindRepoRoot(), "tests", "conformance", "phonological-rule-order");
        var ids = BindSyntheticInventory(path, fixtureRoot);
        var intents = File.ReadAllLines(Path.Combine(fixtureRoot, "intents.jsonl"));
        var batch = ProposalCommands.ComposeBatch(new(path, Version, DraftName, BatchJson(
            RuleBatchEntry(intents[0], ids, "left"),
            RuleBatchEntry(intents[1], ids, "right", "@left"),
            RuleBatchEntry(intents[2], ids, "middle", "@left", "@right"),
            RuleBatchEntry(intents[3], ids, "tail", "@middle", "@right"))));
        Assert.True(batch.Succeeded, batch.Refusal?.Message);
        var ruleCreates = batch.Value!.Intents.SelectMany(result => result.Operations)
            .Where(operation => operation.Kind == CreateRuleKind).ToArray();
        Assert.Equal(4, ruleCreates.Length);
        var draft = ReadDraft(path);
        var storedRules = draft.Operations.Where(operation => operation.Kind == CreateRuleKind).ToArray();
        Assert.Contains(storedRules[2].DependsOn, dependency => dependency == ruleCreates[0].OperationId);
        Assert.Contains(storedRules[2].DependsOn, dependency => dependency == ruleCreates[1].OperationId);
        Assert.Contains(storedRules[3].DependsOn, dependency => dependency == ruleCreates[2].OperationId);

        var proposal = ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(draft));
        var reversed = new Proposal(proposal.ContractVersions, proposal.ProposalId, proposal.Requires,
            proposal.Operations.Reverse().ToArray(), proposal.Extensions);
        using (var scratch = DryRunScratch.Adopt(new FwDataProjectLoader().LoadScratchCache(path),
                   "interior labeled rule placement"))
        {
            _ = ProposalDryRunner.Run(scratch, reversed);
            Assert.Equal([0, 2, 3, 1], scratch.PeekCache().LangProject.PhonologicalDataOA.PhonRulesOS
                .Select(rule => Array.IndexOf(ruleCreates.Select(operation =>
                    CanonicalId.Parse(operation.EntityId!).ToGuid()).ToArray(), rule.Guid)));
        }

        var removed = ProposalCommands.RemoveOperations(new(path, Version, DraftName,
            [ruleCreates[2].OperationId], Force: true));
        Assert.True(removed.Succeeded, removed.Refusal?.Message);
        Assert.Contains(ruleCreates[2].OperationId, removed.Value!.RemovedOperationIds);
        Assert.Contains(ruleCreates[3].OperationId,
            removed.Value.ForcedDependents.Select(operation => operation.OperationId));
        using var afterRemoval = DryRunScratch.Adopt(
            new FwDataProjectLoader().LoadScratchCache(path), "remaining labeled rules after removal");
        var retainedProposal = ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(ReadDraft(path)));
        var retainedReversed = new Proposal(retainedProposal.ContractVersions, retainedProposal.ProposalId,
            retainedProposal.Requires, retainedProposal.Operations.Reverse().ToArray(), retainedProposal.Extensions);
        _ = ProposalDryRunner.Run(afterRemoval, retainedReversed);
        Assert.Equal([0, 1], afterRemoval.PeekCache().LangProject.PhonologicalDataOA.PhonRulesOS
            .Select(rule => Array.IndexOf(ruleCreates.Select(operation =>
                CanonicalId.Parse(operation.EntityId!).ToGuid()).ToArray(), rule.Guid)));
    }

    [Fact]
    public void LiteralAtTextAndCitationsAreNotPlacementLabels()
    {
        using var project = pristine.NewScratch();
        var path = project.ProjectId.Path;
        project.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, DraftName, "Keep ordinary at text")).Succeeded);
        var fixtureRoot = Path.Combine(RepoPaths.FindRepoRoot(), "tests", "conformance", "phonological-rule-order");
        var ids = BindSyntheticInventory(path, fixtureRoot);
        var intents = File.ReadAllLines(Path.Combine(fixtureRoot, "intents.jsonl"));
        var first = RuleBatchEntry(intents[0], ids, "r1");
        first["intent"]!["name"] = "Rule named @r1";
        first["citation"] = new JsonObject { ["source"] = "Grammar @r1", ["locator"] = "@r1" };
        var batch = ProposalCommands.ComposeBatch(new(path, Version, DraftName,
            BatchJson(first, RuleBatchEntry(intents[1], ids, null, "@r1"))));

        Assert.True(batch.Succeeded, batch.Refusal?.Message);
        var draft = ReadDraft(path);
        var firstRuleProvenance = Assert.Single(draft.ComposerProvenance, provenance =>
            provenance.GetProperty("composer").GetString() == "AuthorPhonologicalRule" &&
            provenance.GetProperty("input").GetProperty("name").GetString() == "Rule named @r1");
        Assert.Equal("Rule named @r1",
            firstRuleProvenance.GetProperty("input").GetProperty("name").GetString());
        var firstRuleId = Assert.Single(batch.Value!.Intents[0].Operations,
            operation => operation.Kind == CreateRuleKind).OperationId;
        var created = draft.Operations.Single(operation => operation.OperationId == firstRuleId);
        var citation = OperationCitation.FromProvenance(created.Provenance);
        Assert.Equal(("Grammar @r1", "@r1"), (citation!.Source, citation.Locator));
    }

    [Fact]
    public void CanonicalWrongTypeStaleNonadjacentAndMissingAnchorsRemainRefused()
    {
        using var project = pristine.NewScratch();
        var path = project.ProjectId.Path;
        project.Dispose();
        Assert.True(ProposalCommands.New(new(path, Version, DraftName, "Keep canonical anchors strict")).Succeeded);
        var fixtureRoot = Path.Combine(RepoPaths.FindRepoRoot(), "tests", "conformance", "phonological-rule-order");
        var ids = BindSyntheticInventory(path, fixtureRoot);
        var intents = File.ReadAllLines(Path.Combine(fixtureRoot, "intents.jsonl"));
        var initial = ProposalCommands.ComposeBatch(new(path, Version, DraftName, BatchJson(
            RuleBatchEntry(intents[0], ids, "r1"),
            RuleBatchEntry(intents[1], ids, "r2", "@r1"),
            RuleBatchEntry(intents[2], ids, "r3", "@r2"))));
        Assert.True(initial.Succeeded, initial.Refusal?.Message);
        var rules = initial.Value!.Intents.SelectMany(result => result.Operations)
            .Where(operation => operation.Kind == CreateRuleKind).ToArray();

        AssertCanonicalPlacementRefused(path, ids["AaE28rpEzrUmGYqFyaKXnQ"], null, intents[3], ids);
        AssertCanonicalPlacementRefused(path, CanonicalId.Mint().Value, null, intents[3], ids);
        AssertCanonicalPlacementRefused(path, rules[0].EntityId!, rules[2].EntityId!, intents[3], ids);
        var beforeMissing = ReadDraft(path);
        var missing = ProposalCommands.ComposeBatch(new(path, Version, DraftName,
            BatchJson(RuleBatchEntry(intents[3], ids, null))));
        AssertBatchRefusedAt(missing, 0, PhonologicalRuleVerb, "requires declared placement");
        Assert.Equal(beforeMissing.Operations.Select(operation => operation.OperationId),
            ReadDraft(path).Operations.Select(operation => operation.OperationId));
    }

    [Fact]
    public void SavedLiveRulePlacementRejectsStaleWrongTypeNonadjacentAndRemovedAnchorsAtomically()
    {
        using var project = pristine.NewScratch();
        var path = project.ProjectId.Path;
        project.Dispose();
        const string seedDraft = "saved-live-rule-anchor-seed";
        Assert.True(ProposalCommands.New(new(path, Version, seedDraft, "Save three ordered rules")).Succeeded);
        var fixtureRoot = Path.Combine(RepoPaths.FindRepoRoot(), "tests", "conformance", "phonological-rule-order");
        var ids = BindSyntheticInventory(path, fixtureRoot, seedDraft);
        var sourceIntents = File.ReadAllLines(Path.Combine(fixtureRoot, "intents.jsonl"));
        var seedBatch = ProposalCommands.ComposeBatch(new(path, Version, seedDraft, BatchJson(
            RuleBatchEntry(sourceIntents[0], ids, "r1"),
            RuleBatchEntry(sourceIntents[1], ids, "r2", "@r1"),
            RuleBatchEntry(sourceIntents[2], ids, "r3", "@r2"))));
        Assert.True(seedBatch.Succeeded, seedBatch.Refusal?.Message);
        var seedComment = ProposalCommands.Comment(new(path, Version, seedDraft,
            "Save the three rules for live placement checks."));
        Assert.True(seedComment.Succeeded, seedComment.Refusal?.Message);
        var finalized = ProposalCommands.Finalize(new(path, Version, seedDraft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var dryRun = DryRunJobRunner.Run(path, Version, finalized.Value!.ProposalId);
        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);
        var applied = ProposalCommands.Apply(new ApplyRequest(path, Version, finalized.Value.ProposalId,
            Force: true));
        Assert.True(applied.Succeeded, applied.Refusal?.Message);
        var ruleCreates = seedBatch.Value!.Intents.Select(result => Assert.Single(result.Operations,
            operation => operation.Kind == CreateRuleKind)).ToArray();
        var ruleIds = ruleCreates.Select(operation => operation.EntityId!).ToArray();
        using (var saved = new FwDataProjectLoader().LoadScratchCache(path))
            Assert.Equal(ruleIds.Select(id => CanonicalId.Parse(id).ToGuid()),
                saved.LangProject.PhonologicalDataOA.PhonRulesOS.Select(rule => rule.Guid));

        const string liveDraft = "saved-live-rule-anchor-refusals";
        Assert.True(ProposalCommands.New(new(path, Version, liveDraft, "Check saved rule anchors")).Succeeded);
        JsonObject IntentWithPlacement(string after, string? before = null)
        {
            var intent = JsonNode.Parse(ReplaceIds(sourceIntents[3], ids))!.AsObject();
            var placement = new JsonObject { ["after"] = after };
            if (before is not null) placement["before"] = before;
            intent["placement"] = placement;
            return intent;
        }

        AssertLiveRuleCompositionRefusedWithoutChanges(path, liveDraft,
            IntentWithPlacement(CanonicalId.Mint().Value).ToJsonString(),
            "anchors are stale or are not adjacent");
        AssertLiveRuleCompositionRefusedWithoutChanges(path, liveDraft,
            IntentWithPlacement(ids["AaE28rpEzrUmGYqFyaKXnQ"]).ToJsonString(),
            "anchors are stale or are not adjacent");
        AssertLiveRuleCompositionRefusedWithoutChanges(path, liveDraft,
            IntentWithPlacement(ruleIds[0], ruleIds[2]).ToJsonString(),
            "anchors are stale or are not adjacent");

        var previouslyValid = IntentWithPlacement(ruleIds[1], ruleIds[2]);
        var composed = ProposalCommands.ComposeAuthorPhonologicalRule(new(path, Version, liveDraft,
            previouslyValid.ToJsonString()));
        Assert.True(composed.Succeeded, composed.Refusal?.Message);
        Assert.Single(composed.Value!.Operations, operation => operation.Kind == CreateRuleKind);
        using (var live = new FwDataProjectLoader().LoadCache(path))
        {
            var removedAnchor = live.LangProject.PhonologicalDataOA.PhonRulesOS.Single(rule =>
                rule.Guid == CanonicalId.Parse(ruleIds[1]).ToGuid());
            NonUndoableUnitOfWorkHelper.Do(live.ActionHandlerAccessor,
                () => live.LangProject.PhonologicalDataOA.PhonRulesOS.Remove(removedAnchor));
            new FwDataProjectLoader().Save(live);
        }
        using (var reopened = new FwDataProjectLoader().LoadScratchCache(path))
            Assert.Equal(new[] { CanonicalId.Parse(ruleIds[0]).ToGuid(), CanonicalId.Parse(ruleIds[2]).ToGuid() },
                reopened.LangProject.PhonologicalDataOA.PhonRulesOS.Select(rule => rule.Guid));
        AssertLiveRuleCompositionRefusedWithoutChanges(path, liveDraft,
            previouslyValid.ToJsonString(), "placement neighbours are missing or no longer adjacent; recompute a Dry Run.");
    }

    [Fact]
    public void R1ThroughR6CanBeComposedInOneDraftAndAppliedInDeclaredOrder()
    {
        using var project = pristine.NewScratch();
        var path = project.ProjectId.Path;
        project.Dispose();

        Assert.True(ProposalCommands.New(new(path, Version, DraftName, "Stage six ordered sound rules")).Succeeded);
        var root = RepoPaths.FindRepoRoot();
        var fixtureRoot = Path.Combine(root, "tests", "conformance", "phonological-rule-order");
        var researchIds = BindSyntheticInventory(path, fixtureRoot);
        var sourceIntents = File.ReadAllLines(Path.Combine(fixtureRoot, "intents.jsonl"));
        Assert.Equal(6, sourceIntents.Length);

        var ruleCreates = new List<(string EntityId, string OperationId)>();
        foreach (var (sourceIntent, index) in sourceIntents.Select((intent, index) => (intent, index)))
        {
            var boundIntent = ReplaceIds(sourceIntent, researchIds);
            var intent = JsonNode.Parse(boundIntent)!.AsObject();
            if (index == 1)
            {
                var before = ReadDraft(path).Operations.Count;
                var missingPlacement = ProposalCommands.ComposeAuthorPhonologicalRule(new(path, Version,
                    DraftName, intent.ToJsonString()));
                Assert.False(missingPlacement.Succeeded);
                Assert.Contains("nonempty rule sequence requires declared placement",
                    missingPlacement.Refusal!.Message, StringComparison.Ordinal);
                Assert.Equal(before, ReadDraft(path).Operations.Count);
            }

            if (index > 0)
                intent["placement"] = new JsonObject { ["after"] = ruleCreates[^1].EntityId };

            var composed = ProposalCommands.ComposeAuthorPhonologicalRule(new(path, Version,
                DraftName, intent.ToJsonString()));
            Assert.True(composed.Succeeded, composed.Refusal?.Message);
            var create = Assert.Single(composed.Value!.Operations,
                operation => operation.Kind == CreateRuleKind);
            Assert.NotNull(create.EntityId);
            ruleCreates.Add((create.EntityId!, create.OperationId));
        }

        Assert.Equal(6, ruleCreates.Count);
        var comment = ProposalCommands.Comment(new(path, Version, DraftName,
            "Stage these six rules with their declared sequence."));
        Assert.True(comment.Succeeded, comment.Refusal?.Message);
        using (var source = new FwDataProjectLoader().LoadScratchCache(path))
        {
            Assert.Empty(source.LangProject.PhonologicalDataOA.PhonemeSetsOS);
            Assert.Empty(source.LangProject.PhonologicalDataOA.PhonRulesOS);
        }

        var finalized = ProposalCommands.Finalize(new(path, Version, DraftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var read = ProjectStoreCommand.Run<Proposal>(path, Version, (database, _) =>
            CommandOutcome<Proposal>.Success(ProposalJsonParser.Parse(new ProposalRepository(database)
                .Get(CanonicalId.Parse(finalized.Value!.ProposalId)).ProposalJson!)));
        Assert.True(read.Succeeded, read.Refusal?.Message);
        var proposal = read.Value!;
        var creates = proposal.Operations.Where(operation => operation.Kind == CreateRuleKind).ToArray();
        Assert.Equal(ruleCreates.Select(rule => rule.EntityId), creates.Select(operation => operation.EntityId!.Value.Value));
        for (var index = 1; index < ruleCreates.Count; index++)
            Assert.Contains(creates[index].DependsOn,
                dependency => dependency.OperationId.Value == ruleCreates[index - 1].OperationId);

        var reversed = new Proposal(proposal.ContractVersions, proposal.ProposalId, proposal.Requires,
            proposal.Operations.Reverse().ToArray(), proposal.Extensions);
        using (var dryRunScratch = DryRunScratch.Adopt(
                   new FwDataProjectLoader().LoadScratchCache(path), "research-shaped rule order"))
        {
            var dryRun = ProposalDryRunner.Run(dryRunScratch, reversed);
            Assert.NotEmpty(dryRun.ExpectedEffects);
            Assert.Equal(ruleCreates.Select(rule => CanonicalId.Parse(rule.EntityId).ToGuid()),
                dryRunScratch.PeekCache().LangProject.PhonologicalDataOA.PhonRulesOS.Select(rule => rule.Guid));
        }

        var storedDryRun = DryRunJobRunner.Run(path, Version, finalized.Value!.ProposalId);
        Assert.True(storedDryRun.Succeeded, storedDryRun.Refusal?.Message);
        var applied = ProposalCommands.Apply(new ApplyRequest(path, Version, finalized.Value.ProposalId, Force: true));
        Assert.True(applied.Succeeded, applied.Refusal?.Message);
        Assert.Equal(finalized.Value.ProposalId, applied.Value!.ProposalId);
        Assert.NotEmpty(applied.Value.Effects);

        using var saved = new FwDataProjectLoader().LoadScratchCache(path);
        Assert.Equal(ruleCreates.Select(rule => CanonicalId.Parse(rule.EntityId).ToGuid()),
            saved.LangProject.PhonologicalDataOA.PhonRulesOS.Select(rule => rule.Guid));
    }

    private Dictionary<string, string> BindSyntheticInventory(string path, string fixtureRoot,
        string draftName = DraftName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtureRoot, "bindings.json")));
        var bindings = document.RootElement.EnumerateObject().ToArray();
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var binding in bindings.Where(binding =>
                     binding.Value.GetProperty("kind").GetString() == "phoneme"))
        {
            var sourceId = binding.Name;
            var definition = binding.Value;
            var result = ProposalCommands.ComposeAuthorPhoneme(new(path, Version, draftName,
                JsonSerializer.Serialize(new
                {
                    name = definition.GetProperty("name").GetString(),
                    representations = definition.GetProperty("representations").EnumerateArray()
                        .Select(value => value.GetString()).ToArray(),
                })));
            Assert.True(result.Succeeded, result.Refusal?.Message);
            ids.Add(sourceId, Assert.Single(result.Value!.Operations,
                operation => operation.Kind.EndsWith("/createPhonemes", StringComparison.Ordinal)).EntityId!);
        }

        foreach (var binding in bindings.Where(binding =>
                     binding.Value.GetProperty("kind").GetString() == "naturalClass"))
        {
            var sourceId = binding.Name;
            var definition = binding.Value;
            var members = definition.GetProperty("members").EnumerateArray()
                .Select(member => ids[member.GetString()!]).ToArray();
            var result = ProposalCommands.ComposeAuthorNaturalClass(new(path, Version, draftName,
                JsonSerializer.Serialize(new
                {
                    name = definition.GetProperty("name").GetString(),
                    abbreviation = definition.GetProperty("abbreviation").GetString(),
                    members,
                })));
            Assert.True(result.Succeeded, result.Refusal?.Message);
            ids.Add(sourceId, Assert.Single(result.Value!.Operations,
                operation => operation.Kind.EndsWith("/createNaturalClasses", StringComparison.Ordinal)).EntityId!);
        }

        return ids;
    }

    private static string ReplaceIds(string intent, IReadOnlyDictionary<string, string> ids)
    {
        foreach (var (sourceId, entityId) in ids)
            intent = intent.Replace(sourceId, entityId, StringComparison.Ordinal);
        return intent;
    }

    private static JsonObject RuleBatchEntry(string sourceIntent, IReadOnlyDictionary<string, string> ids,
        string? label, string? after = null, string? before = null)
    {
        var intent = JsonNode.Parse(ReplaceIds(sourceIntent, ids))!.AsObject();
        if (after is not null || before is not null)
        {
            var placement = new JsonObject();
            if (after is not null) placement["after"] = after;
            if (before is not null) placement["before"] = before;
            intent["placement"] = placement;
        }
        var entry = new JsonObject
        {
            ["verb"] = PhonologicalRuleVerb,
            ["intent"] = intent,
        };
        if (label is not null) entry["as"] = label;
        return entry;
    }

    private static string BatchJson(params JsonObject[] entries) =>
        new JsonObject { ["intents"] = new JsonArray(entries.Select(entry => (JsonNode?)entry).ToArray()) }
            .ToJsonString();

    private void AssertCanonicalPlacementRefused(string path, string after, string? before,
        string sourceIntent, IReadOnlyDictionary<string, string> ids)
    {
        var beforeDraft = ReadDraft(path);
        var refused = ProposalCommands.ComposeBatch(new(path, Version, DraftName,
            BatchJson(RuleBatchEntry(sourceIntent, ids, null, after, before))));
        AssertBatchRefusedAt(refused, 0, PhonologicalRuleVerb, "anchors are stale or are not adjacent");
        Assert.Equal(beforeDraft.Operations.Select(operation => operation.OperationId),
            ReadDraft(path).Operations.Select(operation => operation.OperationId));
        using var live = new FwDataProjectLoader().LoadScratchCache(path);
        Assert.Empty(live.LangProject.PhonologicalDataOA.PhonRulesOS);
    }

    private void AssertLiveRuleCompositionRefusedWithoutChanges(string path, string draftName,
        string intentJson, string expectedMessage)
    {
        var beforeDraft = JsonSerializer.Serialize(ReadDraft(path, draftName));
        var beforeProject = SHA256.HashData(File.ReadAllBytes(path));
        var refused = ProposalCommands.ComposeAuthorPhonologicalRule(new(path, Version, draftName, intentJson));
        Assert.False(refused.Succeeded);
        Assert.Equal("draft.invalid", refused.Refusal!.Code);
        Assert.Contains(expectedMessage, refused.Refusal.Message, StringComparison.Ordinal);
        Assert.Equal(beforeDraft, JsonSerializer.Serialize(ReadDraft(path, draftName)));
        Assert.Equal(beforeProject, SHA256.HashData(File.ReadAllBytes(path)));
    }

    private static void AssertBatchRefusedAt(CommandOutcome<ComposedBatchResponse> outcome, int index,
        string verb, string expectedMessage)
    {
        Assert.False(outcome.Succeeded);
        Assert.Equal("draft.invalid", outcome.Refusal!.Code);
        Assert.Contains(expectedMessage, outcome.Refusal.Message, StringComparison.Ordinal);
        Assert.Contains($"Intent {index} ({verb})", outcome.Refusal.Message, StringComparison.Ordinal);
        Assert.Equal(index.ToString(), outcome.Refusal.Facts["intentIndex"]);
        Assert.Equal(verb, outcome.Refusal.Facts["verb"]);
    }

    private DraftDocument ReadDraft(string path, string draftName = DraftName)
    {
        var loaded = ProjectStoreCommand.Run(path, Version, (database, _) =>
            CommandOutcome<DraftDocument>.Success(ProposalCommands.DeserializeDraft(
                new ProposalRepository(database).GetDraft(draftName).ProposalJson!)));
        Assert.True(loaded.Succeeded, loaded.Refusal?.Message);
        return loaded.Value!;
    }

    private string Normalized(string path, string draftName)
    {
        var draft = ReadDraft(path, draftName);
        var json = JsonSerializer.Serialize(new { draft.Operations, draft.ComposerProvenance });
        var ids = draft.Operations.SelectMany(operation => new[] { operation.OperationId, operation.EntityId })
            .OfType<string>().Distinct().Select((id, index) => (id, token: $"#{index}"));
        foreach (var (id, token) in ids) json = json.Replace(id, token, StringComparison.Ordinal);
        return json;
    }
}
