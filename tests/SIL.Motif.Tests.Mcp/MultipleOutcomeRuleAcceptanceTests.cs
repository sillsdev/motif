using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Mcp;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class MultipleOutcomeRuleAcceptanceTests(PristineProjectFixture pristine)
{
    [Theory]
    [InlineData(2)]
    [InlineData(9)]
    public async Task McpAuthorsAndReordersOutcomesInADraftAndAStaleGuardChangesNothing(int count)
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var phones = new Dictionary<string, string>();
        foreach (var letter in new[] { "n", "m", "t" })
        {
            var ops = AuthorPhonemeComposer.Build(cache, new(letter, [letter], []));
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                foreach (var op in ops) OperationHandlerRegistry.Resolve(op.Kind, "outcome fixture").ApplyAndCaptureEffect(cache, op, []);
            });
            phones[letter] = ops.Single(o => o.Kind == PhPhonemeSetPhonemesOperationKinds.Create).EntityId!.Value.Value;
        }
        var loader = new FwDataProjectLoader();
        loader.Save(cache);
        cache.Dispose();
        using var privateRoot = PrivateManagedRoot.Use(Path.GetDirectoryName(path)!);
        KnownProjectTestSetup.AssignUniqueTag(RunnerOptions.ResolveRoot(), path, "outcome-rule");
        var hash = SHA256.HashData(File.ReadAllBytes(path));
        Assert.True(ProposalCommands.New(new(path, "1.0", "outcomes", "Context-dependent outcomes")).Succeeded);
        var context = new ServerContext(path, "1.0", new NoRunnerLauncher(new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), null)),
            new ActivityLog(null), ToolProfile.Builtin, TextWriter.Null, advancedAiModeEnabled: true);
        var tools = MotifMcpServer.Expose(ToolProfile.Builtin);
        var outcomes = JsonSerializer.SerializeToNode(new[]
        {
            new { output = new[] { new { phoneme = phones["m"] } }, right = new[] { new { phoneme = phones["m"] } } },
            new { output = new[] { new { phoneme = phones["t"] } }, right = new[] { new { phoneme = phones["t"] } } },
        })!.AsArray();
        while (outcomes.Count < count) outcomes.Add(outcomes[0]!.DeepClone());
        var author = new JsonObject
        {
            ["project"] = path, ["draft"] = "outcomes", ["name"] = "ordered cases", ["direction"] = "simultaneous",
            ["input"] = JsonSerializer.SerializeToNode(new[] { new { phoneme = phones["n"] } }),
            ["rightHandSides"] = outcomes.DeepClone(),
        };
        var added = await MotifMcpServer.CallAsync(context, tools, "motif_add_phonological_rule", Arguments(author), CancellationToken.None);
        Assert.False(added.IsError == true, added.StructuredContent?.ToString());
        var staged = ProposalCommands.DeserializeDraft(ReadDraft());
        var rule = staged.Operations.Single(o => o.Kind == PhPhonDataPhonRulesOperationKinds.Create).EntityId!;
        Assert.Equal(count, staged.Operations.Count(o => o.Kind == PhRegularRuleRightHandSidesOperationKinds.Create));
        using (var authored = DryRunScratch.Adopt(loader.LoadScratchCache(path), "authored outcomes proof"))
        {
            var proposal = SIL.Motif.Contract.Parsing.ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(staged));
            ProposalDryRunner.Run(authored, proposal);
            Assert.Equal(count, ((IPhRegularRule)Assert.Single(authored.PeekCache().LangProject.PhonologicalDataOA.PhonRulesOS)).RightHandSidesOS.Count);
        }
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(path)));
        using (var baseline = loader.LoadScratchCache(path))
        {
            Assert.Empty(baseline.LangProject.PhonologicalDataOA.PhonRulesOS);
            var proposal = SIL.Motif.Contract.Parsing.ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(staged));
            NonUndoableUnitOfWorkHelper.Do(baseline.ActionHandlerAccessor, () =>
            {
                foreach (var op in proposal.Operations)
                    OperationHandlerRegistry.Resolve(op.Kind, "persisted outcome fixture").ApplyAndCaptureEffect(baseline, op, []);
            });
            loader.Save(baseline);
        }
        hash = SHA256.HashData(File.ReadAllBytes(path));
        Assert.True(ProposalCommands.New(new(path, "1.0", "edit-outcomes", "Reorder persisted outcomes")).Succeeded);
        var reordered = new JsonArray(outcomes[1]!.DeepClone(), outcomes[0]!.DeepClone());
        foreach (var outcome in outcomes.Skip(2)) reordered.Add(outcome!.DeepClone());
        var edit = new JsonObject
        {
            ["project"] = path, ["draft"] = "edit-outcomes", ["intents"] = new JsonArray(new JsonObject
            {
                ["verb"] = "compose-edit-phonological-rule", ["intent"] = new JsonObject
                {
                    ["rule"] = rule, ["rightHandSides"] = reordered,
                    ["expected"] = new JsonObject { ["rightHandSides"] = outcomes.DeepClone() },
                },
            }),
        };
        var edited = await MotifMcpServer.CallAsync(context, tools, "motif_compose_batch", Arguments(edit), CancellationToken.None);
        Assert.False(edited.IsError == true, edited.StructuredContent?.ToString());
        staged = ProposalCommands.DeserializeDraft(ReadDraft("edit-outcomes"));
        Assert.Equal(count, staged.Operations.Count(o => o.Kind == PhonologicalRuleDeleteOperationKinds.DeleteRightHandSides));
        using (var scratch = DryRunScratch.Adopt(loader.LoadScratchCache(path), "outcome sequence proof"))
        {
            var proposal = SIL.Motif.Contract.Parsing.ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(staged));
            ProposalDryRunner.Run(scratch, proposal);
            var read = PhonologicalRuleReader.Read(Assert.Single(scratch.PeekCache().LangProject.PhonologicalDataOA.PhonRulesOS))!;
            Assert.Equal(count, read.Outcomes.Count);
            Assert.Equal(phones["t"], read.Outcomes[0].Output.Single().Phoneme!.Value.Value);
            Assert.Equal(phones["m"], read.Outcomes[1].Output.Single().Phoneme!.Value.Value);
        }
        var beforeStale = ReadDraft("edit-outcomes");
        var refused = await MotifMcpServer.CallAsync(context, tools, "motif_compose_batch", Arguments(edit), CancellationToken.None);
        Assert.True(refused.IsError);
        Assert.Equal(beforeStale, ReadDraft("edit-outcomes"));
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(path)));
        using var reopened = loader.LoadScratchCache(path);
        var saved = PhonologicalRuleReader.Read(Assert.Single(reopened.LangProject.PhonologicalDataOA.PhonRulesOS))!;
        Assert.Equal(phones["m"], saved.Outcomes[0].Output.Single().Phoneme!.Value.Value);
        Assert.Equal(phones["t"], saved.Outcomes[1].Output.Single().Phoneme!.Value.Value);

        string ReadDraft(string name = "outcomes")
        {
            using var database = ProjectMotifDatabase.Open(path);
            return new ProposalRepository(database).GetDraft(name).ProposalJson!;
        }
    }

    private static Dictionary<string, JsonElement> Arguments(JsonObject node) =>
        node.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value));
}
