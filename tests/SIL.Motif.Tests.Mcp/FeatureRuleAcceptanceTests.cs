using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Preferences;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
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

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class FeatureRuleAcceptanceTests(PristineProjectFixture pristine)
{
    [Theory]
    [InlineData("+")]
    [InlineData("-")]
    public async Task CliAndMcpStageAlphaRulesAndContradictoryVariablesLeaveTheDraftAndProjectUntouched(string inputPolarity)
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        var loader = new FwDataProjectLoader();
        var featureOps = AuthorPhonologicalFeatureComposer.Build(cache, new("place", [new("labial"), new("coronal")]));
        Execute(featureOps);
        var feature = featureOps[0].EntityId!.Value;
        var values = featureOps.Where(op => op.Kind == FsClosedFeatureValuesOperationKinds.Create).Select(op => op.EntityId!.Value).ToArray();
        Execute(AuthorPhonemeComposer.Build(cache, new("p", ["p"], [new(feature, values[0])])));
        Execute(AuthorPhonemeComposer.Build(cache, new("t", ["t"], [new(feature, values[1])])));
        IPhNCFeatures nc = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            nc = cache.ServiceLocator.GetInstance<IPhNCFeaturesFactory>().Create();
            cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Add(nc);
        });
        var naturalClass = CanonicalId.FromGuid(nc.Guid).Value;
        loader.Save(cache);
        cache.Dispose();
        using var privateRoot = PrivateManagedRoot.Use(Path.GetDirectoryName(path)!);
        KnownProjectTestSetup.AssignUniqueTag(RunnerOptions.ResolveRoot(), path, "feature-rule");
        var hash = SHA256.HashData(File.ReadAllBytes(path));
        foreach (var draft in new[] { "cli-alpha", "mcp-alpha" })
            Assert.True(ProposalCommands.New(new(path, "1.0", draft, "Alpha rule")).Succeeded);
        var intent = new JsonObject
        {
            ["name"] = "alpha disagreement", ["direction"] = "simultaneous",
            ["input"] = JsonSerializer.SerializeToNode(new[] { new { naturalClass,
                variables = new[] { new { variable = "alpha", feature = feature.Value, polarity = inputPolarity } } } }),
            ["output"] = JsonSerializer.SerializeToNode(new[] { new { change = Array.Empty<object>(),
                variables = new[] { new { variable = "alpha", feature = feature.Value, polarity = "-" } } } }),
        };
        var preferencePath = Path.Combine(Path.GetDirectoryName(path)!, "advanced-ai.json");
        new FileAdvancedAiModePreferenceStore(preferencePath).SetEnabled(true);
        var start = new ProcessStartInfo(BuildOutput.Cli);
        start.Environment[FileAdvancedAiModePreferenceStore.PathEnvironmentVariable] = preferencePath;
        var cli = CliInProcess.Run(start, "compose-author-phonological-rule", "--project", path,
            "--draft", "cli-alpha", "--intent", intent.ToJsonString(), "--json");
        Assert.True(cli.ExitCode == 0, cli.Error + cli.Output);
        var context = new ServerContext(path, "1.0", new NoRunnerLauncher(new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), null)),
            new ActivityLog(null), ToolProfile.Builtin, TextWriter.Null, advancedAiModeEnabled: true);
        var tools = MotifMcpServer.Expose(ToolProfile.Builtin);
        var arguments = intent.DeepClone().AsObject();
        arguments["draft"] = "mcp-alpha";
        arguments["project"] = path;
        var mcp = await MotifMcpServer.CallAsync(context, tools, "motif_add_phonological_rule", Arguments(arguments), CancellationToken.None);
        Assert.False(mcp.IsError == true, mcp.StructuredContent?.ToString());
        var staged = ReadDraft("mcp-alpha");
        var operations = ProposalCommands.DeserializeDraft(staged).Operations;
        Assert.Contains(operations, op => op.Kind == PhPhonDataFeatConstraintsOperationKinds.Create);
        Assert.Contains(operations, op => op.Kind == (inputPolarity == "+"
            ? PhSimpleContextNCPlusConstrOperationKinds.AddRefPlusConstr : PhSimpleContextNCMinusConstrOperationKinds.AddRefMinusConstr));
        Assert.Contains(operations, op => op.Kind == PhSimpleContextNCMinusConstrOperationKinds.AddRefMinusConstr);
        Assert.Equal(ProposalCommands.DeserializeDraft(ReadDraft("cli-alpha")).Operations.Select(op => op.Kind), operations.Select(op => op.Kind));
        arguments["input"]![0]!["variables"] = JsonSerializer.SerializeToNode(new[]
        {
            new { variable = "alpha", feature = feature.Value, polarity = "+" },
            new { variable = "alpha", feature = feature.Value, polarity = "-" },
        });
        var refusal = await MotifMcpServer.CallAsync(context, tools, "motif_add_phonological_rule", Arguments(arguments), CancellationToken.None);
        Assert.True(refusal.IsError);
        Assert.Contains("alpha", refusal.StructuredContent!.Value.GetProperty("message").GetString());
        Assert.Equal(staged, ReadDraft("mcp-alpha"));
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(path)));
        using var reopened = loader.LoadScratchCache(path);
        Assert.Empty(reopened.LangProject.PhonologicalDataOA.PhonRulesOS);
        Assert.Empty(reopened.LangProject.PhonologicalDataOA.FeatConstraintsOS);

        void Execute(IReadOnlyList<OperationEnvelope> operations) => NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            foreach (var op in operations) OperationHandlerRegistry.Resolve(op.Kind, "featured rule fixture").ApplyAndCaptureEffect(cache, op, []);
        });
        string ReadDraft(string name)
        {
            using var database = ProjectMotifDatabase.Open(path);
            return new ProposalRepository(database).GetDraft(name).ProposalJson!;
        }
    }

    private static Dictionary<string, JsonElement> Arguments(JsonObject node) =>
        node.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value));
}
