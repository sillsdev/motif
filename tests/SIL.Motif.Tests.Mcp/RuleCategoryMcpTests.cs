using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Mcp;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

/// <summary>The single-outcome <c>inputCategories</c> shorthand of motif_add_phonological_rule (motif#468).</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RuleCategoryMcpTests(PristineProjectFixture pristine)
{
    [Fact]
    public void TheAddRuleSchemaListsTheShorthandAndTheMetathesisSchemaDoesNot()
    {
        var add = AgentTools.All.Single(tool => tool.Name == "motif_add_phonological_rule");
        Assert.Contains("inputCategories", add.ArgumentNames);
        var metathesis = AgentTools.All.Single(tool => tool.Name == "motif_add_metathesis_rule");
        Assert.DoesNotContain("inputCategories", metathesis.ArgumentNames);
    }

    [Fact]
    public async Task McpAcceptsTheShorthandAndRefusesMixingItWithRightHandSides()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var ids = new Dictionary<string, string>();
        foreach (var letter in new[] { "u", "i" })
            ids[letter] = Apply(cache, AuthorPhonemeComposer.Build(cache, new(letter, [letter], [])), PhPhonemeSetPhonemesOperationKinds.Create);
        var analysis = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        var verb = Apply(cache, AuthorPartOfSpeechComposer.Build(cache, new AuthorPartOfSpeechIntent("Verb", analysis, "V")),
            CmPossibilityListPossibilitiesOperationKinds.Create);
        var loader = new FwDataProjectLoader();
        loader.Save(cache);
        cache.Dispose();
        using var privateRoot = PrivateManagedRoot.Use(Path.GetDirectoryName(path)!);
        KnownProjectTestSetup.AssignUniqueTag(RunnerOptions.ResolveRoot(), path, "rule-category");
        Assert.True(ProposalCommands.New(new(path, "1.0", "category", "Category rule")).Succeeded);
        var context = new ServerContext(path, "1.0", new NoRunnerLauncher(new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), null)),
            new ActivityLog(), ToolProfile.Builtin, TextWriter.Null, advancedAiModeEnabled: true);
        var tools = MotifMcpServer.Expose(ToolProfile.Builtin);
        JsonObject Rule() => new()
        {
            ["project"] = path, ["draft"] = "category", ["name"] = "u becomes i on verbs", ["direction"] = "simultaneous",
            ["input"] = JsonSerializer.SerializeToNode(new[] { new { phoneme = ids["u"] } }),
            ["inputCategories"] = new JsonArray(verb),
        };

        var mixed = Rule();
        mixed["rightHandSides"] = JsonSerializer.SerializeToNode(new[] { new { output = new[] { new { phoneme = ids["i"] } } } });
        var refused = await MotifMcpServer.CallAsync(context, tools, "motif_add_phonological_rule", Arguments(mixed), CancellationToken.None);
        Assert.True(refused.IsError);
        Assert.Equal(RefusalCodes.DraftInvalid, refused.StructuredContent!.Value.GetProperty("code").GetString());

        var shorthand = Rule();
        shorthand["output"] = JsonSerializer.SerializeToNode(new[] { new { phoneme = ids["i"] } });
        var added = await MotifMcpServer.CallAsync(context, tools, "motif_add_phonological_rule", Arguments(shorthand), CancellationToken.None);
        Assert.False(added.IsError == true, added.StructuredContent?.ToString());
        using var database = ProjectMotifDatabase.Open(path);
        var staged = ProposalCommands.DeserializeDraft(new ProposalRepository(database).GetDraft("category").ProposalJson!);
        Assert.Single(staged.Operations, op => op.Kind == PhPhonDataPhonRulesOperationKinds.Create);
        Assert.Contains(staged.Operations, op => op.Kind.Contains("InputPOS", StringComparison.OrdinalIgnoreCase));
    }

    private static string Apply(LcmCache cache, IReadOnlyList<SIL.Motif.Contract.Model.OperationEnvelope> operations, string createKind)
    {
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            foreach (var op in operations) OperationHandlerRegistry.Resolve(op.Kind, "category fixture").ApplyAndCaptureEffect(cache, op, []);
        });
        return operations.Single(o => o.Kind == createKind).EntityId!.Value.Value;
    }

    private static Dictionary<string, JsonElement> Arguments(JsonObject node) =>
        node.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value));
}
