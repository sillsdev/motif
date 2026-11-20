using System.Text.Json;
using ModelContextProtocol.Protocol;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Mcp;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class DispositionToolTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task AnAgentReadsAWordTypesDispositionAndThePageButCannotWriteOne()
    {
        string path;
        using (var cache = pristine.NewScratch()) path = cache.ProjectId.Path;
        using var privateRoot = PrivateManagedRoot.Use(Path.GetDirectoryName(path)!);
        KnownProjectTestSetup.AssignUniqueTag(RunnerOptions.ResolveRoot(), path, "disposition-tools");
        var profile = new ToolProfile("dispositions", null, null, "concise", [], AllTools: true);
        var context = new ServerContext(path, "1.0", new NoRunnerLauncher(new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), null)),
            new ActivityLog(null), profile, TextWriter.Null, advancedAiModeEnabled: true);
        var tools = MotifMcpServer.Expose(profile);
        Assert.True(WordTypeDispositionCommands.Record(new DisposeWordFormsRequest(path, "1.0", ["Nairobi", "Mombasa"], "name",
            "towns", RecordedBy: "ana")).Succeeded);

        var shown = await Call(context, tools, "motif_disposition_show", path, ("form", "Nairobi"));
        var result = shown.StructuredContent!.Value.GetProperty("result");
        Assert.Equal("name", result.GetProperty("current").GetProperty("category").GetString());
        Assert.Equal("ana", result.GetProperty("current").GetProperty("recordedBy").GetString());
        Assert.Equal("word-wide", result.GetProperty("scope").GetString());

        var listed = await Call(context, tools, "motif_disposition_list", path, ("category", "name"));
        var page = listed.StructuredContent!.Value.GetProperty("result");
        Assert.Equal(2, page.GetProperty("total").GetInt32());
        Assert.Equal(2, page.GetProperty("items").GetArrayLength());

        Assert.DoesNotContain(tools, tool => tool.Name.Contains("wordform", StringComparison.Ordinal) ||
            tool.Name is "motif_dispose_word_forms" or "motif_undispose_word_forms");
    }

    private static async Task<CallToolResult> Call(ServerContext context, IReadOnlyList<ExposedTool> tools, string tool,
        string project, params (string Name, string Value)[] more)
    {
        var args = new Dictionary<string, JsonElement> { ["project"] = JsonSerializer.SerializeToElement(project) };
        foreach (var (name, value) in more) args[name] = JsonSerializer.SerializeToElement(value);
        return await MotifMcpServer.CallAsync(context, tools, tool, args, CancellationToken.None);
    }
}
