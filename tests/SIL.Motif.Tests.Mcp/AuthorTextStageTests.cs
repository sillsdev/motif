using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Commands;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Mcp;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class AuthorTextStageTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task CliAndMcpStageAnnotationsAndFieldWorksTokensWithoutWritingTheProject()
    {
        string path, ws, analysisWs;
        using (var cache = pristine.NewScratch())
        {
            new FwDataProjectLoader().Save(cache);
            path = cache.ProjectId.Path;
            ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
            analysisWs = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        }
        var before = SHA256.HashData(File.ReadAllBytes(path));
        foreach (var draft in new[] { "text-cli", "text-mcp", "text-batch" })
            Assert.True(ProposalCommands.New(new(path, "0.1.0", draft, "Text annotation staging")).Succeeded);
        var intent = JsonSerializer.SerializeToNode(new
        {
            title = "T", ws, analysisWs, paragraphs = new[] { new { segments = new[]
            {
                new { text = "(nakapaluwas) mag-ágaw ug, ?".Normalize(NormalizationForm.FormD),
                    freeTranslation = "Was someone rescued?", literalTranslation = "rescued?", notes = new[] { "Check context." } },
            } } },
        })!.AsObject();
        using var privateRoot = PrivateManagedRoot.Use(Path.GetDirectoryName(path)!);
        KnownProjectTestSetup.AssignUniqueTag(RunnerOptions.ResolveRoot(), path, "text-stage");
        var cli = CliInProcess.Run(RunnerOptions.ResolveRoot(), FakeParser.ExecutablePath, true,
            "compose-author-text", "--project", path, "--draft", "text-cli", "--intent", intent.ToJsonString(), "--json");
        Assert.True(cli.ExitCode == 0, cli.Error + cli.Output);
        var profile = new ToolProfile("text-stage", null, null, "concise", [], AllTools: true);
        var context = new ServerContext(path, "0.1.0", new NoRunnerLauncher(new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), null)),
            new ActivityLog(null), profile, TextWriter.Null, advancedAiModeEnabled: true);
        var tools = MotifMcpServer.Expose(profile);
        var args = intent.DeepClone().AsObject();
        args["draft"] = "text-mcp";
        args["project"] = path;
        var staged = await MotifMcpServer.CallAsync(context, tools, "motif_add_text", Arguments(args), CancellationToken.None);
        Assert.False(staged.IsError == true, staged.StructuredContent?.ToString());
        var batched = await MotifMcpServer.CallAsync(context, tools, "motif_compose_batch", Arguments(new JsonObject
        {
            ["draft"] = "text-batch", ["project"] = path,
            ["intents"] = new JsonArray(new JsonObject { ["verb"] = "compose-author-text", ["intent"] = intent.DeepClone() }),
        }), CancellationToken.None);
        Assert.False(batched.IsError == true, batched.StructuredContent?.ToString());
        foreach (var draft in new[] { "text-cli", "text-mcp", "text-batch" })
        {
            using var database = ProjectMotifDatabase.Open(path);
            var document = ProposalCommands.DeserializeDraft(new ProposalRepository(database).GetDraft(draft).ProposalJson!);
            var created = document.Operations.Where(op => op.Kind == LangProjectWordformsOperationKinds.Create)
                .Select(op => op.After!["text"].GetString()).ToArray();
            Assert.Contains("nakapaluwas", created);
            Assert.Contains("ug", created);
            Assert.DoesNotContain("?", created);
            var text = JsonSerializer.SerializeToElement(Assert.Single(document.Operations, op => op.Kind == LangProjectTextsOperationKinds.Create).After);
            Assert.Equal(analysisWs, text.GetProperty("analysisWs").GetString());
            var segment = text.GetProperty("paragraphs")[0].GetProperty("segments")[0];
            Assert.Equal("Was someone rescued?", segment.GetProperty("freeTranslation").GetString());
            Assert.Equal("Check context.", segment.GetProperty("notes")[0].GetProperty("text").GetString());
        }
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
    }

    private static Dictionary<string, JsonElement> Arguments(JsonObject node) =>
        node.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value));
}
