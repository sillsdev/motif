using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
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

    private Dictionary<string, string> BindSyntheticInventory(string path, string fixtureRoot)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtureRoot, "bindings.json")));
        var bindings = document.RootElement.EnumerateObject().ToArray();
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var binding in bindings.Where(binding =>
                     binding.Value.GetProperty("kind").GetString() == "phoneme"))
        {
            var sourceId = binding.Name;
            var definition = binding.Value;
            var result = ProposalCommands.ComposeAuthorPhoneme(new(path, Version, DraftName,
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
            var result = ProposalCommands.ComposeAuthorNaturalClass(new(path, Version, DraftName,
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

    private DraftDocument ReadDraft(string path)
    {
        var loaded = ProjectStoreCommand.Run(path, Version, (database, _) =>
            CommandOutcome<DraftDocument>.Success(ProposalCommands.DeserializeDraft(
                new ProposalRepository(database).GetDraft(DraftName).ProposalJson!)));
        Assert.True(loaded.Succeeded, loaded.Refusal?.Message);
        return loaded.Value!;
    }
}
