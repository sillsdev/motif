using System.Text.Json.Nodes;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Mcp;
using SIL.Motif.Scripture;
using SIL.Motif.Tests.Scripture;
using SIL.Motif.Tests.Scripture.Fixtures;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class SourceToolTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.Tests.SourceTool", Guid.NewGuid().ToString("N"));

    private static readonly SeededSourceList.Item[] Sources =
    [
        new("WEB", "World English Bible", "GEN-ROM; 1CO 13-15", "Paratext", "English"),
        new("OLD", "Old", "GEN", "Paratext", "English", "Removed"),
    ];

    private static AgentTool Tool(string name) => AgentTools.All.Single(tool => tool.Name == name);

    private static ToolArgs Args(AgentTool tool, JsonObject arguments) => new(arguments, tool.ArgumentNames);

    private static ServerContext Context(string project) => new(project, "1.0",
        new NoRunnerLauncher(new JobRunnerLaunchOptions(SIL.Motif.Worker.RunnerOptions.ResolveRoot(), null)),
        new ActivityLog(null), ToolProfile.Builtin, TextWriter.Null, advancedAiModeEnabled: true);

    [Fact]
    public void TheTwoReadToolsHaveHelpDescriptionsAndFieldLinesAndAreInTheDefaultAndPhonologyProfiles()
    {
        var sources = Tool("motif_sources");
        var current = Tool("motif_current");

        Assert.Equal(["all", "id", "project", "ref"], sources.ArgumentNames.Order(StringComparer.Ordinal));
        Assert.Equal(["project", "source"], current.ArgumentNames.Order(StringComparer.Ordinal));
        Assert.All(new[] { sources, current }, tool =>
        {
            Assert.Equal(SIL.Motif.Commands.Catalog.AgentClass.Read, tool.Class);
            Assert.True(tool.ReadOnly);
        });
        Assert.Contains("\nid: ", sources.Description);
        Assert.Contains("\nref: ", sources.Description);
        Assert.Contains("\nall: ", sources.Description);
        Assert.DoesNotContain("ExpectedBaseline", sources.Description);
        Assert.Contains("\nsource: ", current.Description);
        foreach (var profile in new[] { ToolProfile.Builtin, ToolProfile.Load(Path.Combine(AppContext.BaseDirectory, "profiles", "phonology.json")) })
        {
            var names = MotifMcpServer.Expose(profile).Select(exposed => exposed.Name).ToArray();
            Assert.Contains("motif_sources", names);
            Assert.Contains("motif_current", names);
        }
        Assert.DoesNotContain(MotifMcpServer.Expose(ToolProfile.Load(Path.Combine(AppContext.BaseDirectory, "profiles", "lean.json"))),
            exposed => exposed.Name is "motif_sources" or "motif_current");
    }

    [Fact]
    public void ThereIsNoToolThatWritesASourceItsCurrentChapterOrItsFolder()
    {
        var names = AgentTools.All.Select(tool => tool.Name).Where(name => name.Contains("source", StringComparison.Ordinal) ||
            name.Contains("current", StringComparison.Ordinal) || name.Contains("folder", StringComparison.Ordinal)).Order().ToArray();

        // The reads, the four non-composer ones aside, and the five composers that only work in a named Draft.
        Assert.Equal(["motif_add_source", "motif_current", "motif_set_source_context", "motif_set_source_name",
            "motif_set_source_status", "motif_set_source_versification", "motif_sources"], names.Where(name =>
                !name.Contains("validated", StringComparison.Ordinal)).ToArray());
        Assert.All(ProposalCommands.ComposerDescriptors.Where(composer => SourceAiGuard.IsSourceVerb(composer.Verb)), composer =>
        {
            Assert.True(composer.InCompose && composer.InBatch);
            Assert.Equal(SIL.Motif.Commands.Catalog.AgentClass.Draft, composer.Agent);
        });
        Assert.Equal(5, ProposalCommands.ComposerDescriptors.Count(composer => SourceAiGuard.IsSourceVerb(composer.Verb)));
    }

    [Fact]
    public void ComposeAndBatchOfferTheFiveSourceVerbs()
    {
        foreach (var (name, property) in new[] { ("motif_compose", "steps"), ("motif_compose_batch", "intents") })
        {
            var verbs = Tool(name).InputSchema["properties"]![property]!["items"]!["oneOf"]!.AsArray()
                .Select(branch => branch!["properties"]!["verb"]!["enum"]![0]!.GetValue<string>()).ToArray();
            Assert.Equal(5, verbs.Count(SourceAiGuard.IsSourceVerb));
        }
    }

    [Theory]
    [InlineData("pending-changes")]
    [InlineData("Pending-Changes")]
    public async Task AnAiSourceEditInThePendingChangesIsRefusedByEveryComposeRoute(string draft)
    {
        var context = Context("unused.fwdata");
        var intent = new JsonObject { ["id"] = "WEB", ["name"] = "n", ["versification"] = "English", ["context"] = "MAT" };
        var generated = Tool("motif_add_source");
        var compose = Tool("motif_compose");
        var batch = Tool("motif_compose_batch");

        var outcomes = new[]
        {
            await generated.Run(context, Args(generated, new JsonObject { ["draft"] = draft, ["id"] = "WEB", ["name"] = "n",
                ["versification"] = "English", ["context"] = "MAT" }), default),
            await compose.Run(context, Args(compose, new JsonObject { ["proposal"] = draft, ["steps"] = new JsonArray(new JsonObject
                { ["verb"] = "compose-add-source", ["intent"] = intent.DeepClone() }) }), default),
            await batch.Run(context, Args(batch, new JsonObject { ["draft"] = draft, ["intents"] = new JsonArray(new JsonObject
                { ["verb"] = "compose-set-source-status", ["intent"] = new JsonObject { ["source"] = "WEB", ["status"] = "Removed" } }) }), default),
        };

        Assert.All(outcomes, outcome => Assert.Equal(RefusalCodes.SourceAiNeedsProposal, outcome.Refusal?.Code));
    }

    [Fact]
    public void OnlySourceVerbsInThePendingChangesAreRefused()
    {
        Assert.Null(SourceAiGuard.Check("my-proposal", "compose-add-source"));
        Assert.Null(SourceAiGuard.Check("pending-changes", "compose-author-phoneme"));
        Assert.Null(SourceAiGuard.Check(null, "compose-add-source"));
        Assert.NotNull(SourceAiGuard.Check(" pending-changes ", "compose-set-source-name"));
    }

    [Fact]
    public async Task SourcesListsHidesRemovedShowsOneAndPlacesAChapterThenCurrentReadsTheLocalFile()
    {
        var path = ProjectWithSources();
        var context = Context(path);
        var sources = Tool("motif_sources");
        var current = Tool("motif_current");

        var listed = await sources.Run(context, Args(sources, new JsonObject()), default);
        var all = await sources.Run(context, Args(sources, new JsonObject { ["all"] = true }), default);
        var one = await sources.Run(context, Args(sources, new JsonObject { ["id"] = "web" }), default);
        var placed = await sources.Run(context, Args(sources, new JsonObject { ["ref"] = "REV 5" }), default);
        var placedOne = await sources.Run(context, Args(sources, new JsonObject { ["id"] = "WEB", ["ref"] = "GEN 1" }), default);
        var missing = await sources.Run(context, Args(sources, new JsonObject { ["id"] = "NOPE" }), default);
        var badRef = await sources.Run(context, Args(sources, new JsonObject { ["ref"] = "banana" }), default);

        Assert.Equal(["WEB"], Ids(listed));
        Assert.Equal(["WEB", "OLD"], Ids(all));
        Assert.Equal(["WEB"], Ids(one));
        Assert.NotNull(listed.Value!["baseline"]);
        Assert.False(listed.Value["isStale"]!.GetValue<bool>());
        Assert.Contains(listed.Value["sources"]![0]!["contextRanges"]!.AsArray(),
            range => range!["book"]!.GetValue<string>() == "1CO" && range["chapters"]!.GetValue<string>() == "13-15");
        Assert.Equal("Later", placed.Value!["sources"]![0]!["place"]!.GetValue<string>());
        Assert.Equal("InContext", placedOne.Value!["sources"]![0]!["place"]!.GetValue<string>());
        Assert.Equal(RefusalCodes.SourceNotFound, missing.Refusal?.Code);
        Assert.Equal(RefusalCodes.SourceRefInvalid, badRef.Refusal?.Code);

        var none = await current.Run(context, Args(current, new JsonObject()), default);
        Assert.Empty(none.Value!["current"]!.AsArray());
        var key = ProjectStoreCommand.Run<string>(path, MotifProductVersion.CurrentText,
            (_, project) => CommandOutcome<string>.Success(ProjectWorkspaceKey.Compute(project))).Value!;
        ScriptureLocalStore.ForInstallation().SetCurrent(key, "WEB", new ChapterRef("MAT", 5));
        var set = await current.Run(context, Args(current, new JsonObject { ["source"] = "WEB" }), default);
        Assert.Equal("MAT 5", set.Value!["current"]![0]!["chapter"]!.GetValue<string>());
        Assert.Equal("WEB", set.Value["current"]![0]!["source"]!.GetValue<string>());
    }

    private static string[] Ids(ToolOutcome outcome) =>
        outcome.Value!["sources"]!.AsArray().Select(row => row!["id"]!.GetValue<string>()).ToArray();

    private string ProjectWithSources()
    {
        using var cache = pristine.NewScratch();
        SeededSourceList.AddList(cache, Sources);
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        var root = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
