using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Mcp;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RecordExportToolTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "motif-export-mcp-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); }
        catch (IOException) { }
    }

    private (string Path, Guid Entry) Fixture()
    {
        Directory.CreateDirectory(_folder);
        using var cache = pristine.NewScratch();
        Guid entryId = default;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            var entry = services.GetInstance<ILexEntryFactory>().Create();
            entryId = entry.Guid;
            var form = services.GetInstance<IMoStemAllomorphFactory>().Create();
            entry.LexemeFormOA = form;
            form.Form.set_String(cache.DefaultVernWs, "zzword");
            var sense = services.GetInstance<ILexSenseFactory>().Create();
            entry.SensesOS.Add(sense);
            sense.Gloss.set_String(cache.DefaultAnalWs, "zzgloss");
        });
        new FwDataProjectLoader().Save(cache);
        return (cache.ProjectId.Path, entryId);
    }

    private static ServerContext Context(string path, ToolProfile profile) =>
        new(path, "0.1.0", new NoRunnerLauncher(new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), null)),
            new ActivityLog(null), profile, TextWriter.Null, advancedAiModeEnabled: true);

    private static Dictionary<string, JsonElement> Args(params (string Name, object Value)[] arguments) =>
        arguments.ToDictionary(item => item.Name, item => JsonSerializer.SerializeToElement(item.Value));

    [Fact]
    public void TheToolTakesAnOutPathAnOptionalRecordKindAndAllAndIsRegisteredOnTheCatalogEntry()
    {
        var tool = AgentTools.All.Single(item => item.Name == "motif_record_export");
        Assert.False(tool.InputSchema["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(["all", "kind", "out", "project", "recordId"], tool.ArgumentNames.Order(StringComparer.Ordinal));
        Assert.Contains(tool.Routes, route => route.Command == "record export");
        Assert.DoesNotContain(AgentTools.All, item => item.Name.Contains("apply", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExportThenImportThroughTheToolsPlansNoChangeAndOnlyUnderTheProjectFolderOrWorkingDirectoryIsWritten()
    {
        var (path, entry) = Fixture();
        var before = SHA256.HashData(File.ReadAllBytes(path));
        using var privateRoot = PrivateManagedRoot.Use(Path.GetDirectoryName(path)!);
        KnownProjectTestSetup.AssignUniqueTag(RunnerOptions.ResolveRoot(), path, "export-roots");
        var profile = new ToolProfile("export-roots", null, null, "concise", [], AllTools: true);
        var tools = MotifMcpServer.Expose(profile);
        var projectFolder = Path.GetDirectoryName(path)!;

        async Task<(bool Error, string Text, JsonElement? Structured)> Call(string name, params (string, object)[] arguments)
        {
            var result = await MotifMcpServer.CallAsync(Context(path, profile), tools, name,
                Args([("project", path), .. arguments]), CancellationToken.None);
            return (result.IsError == true, result.StructuredContent?.ToString() ?? "", result.StructuredContent);
        }

        var file = Path.Combine(projectFolder, "exported.md");
        var exported = await Call("motif_record_export", ("out", file), ("recordId", entry.ToString("D")));
        Assert.False(exported.Error, exported.Text);
        var result = JsonNode.Parse(exported.Structured!.Value.GetRawText())!["result"]!;
        Assert.Equal(1, result["writtenCount"]!.GetValue<int>());
        Assert.True(File.Exists(file));

        var imported = await Call("motif_import_record", ("file", file));
        Assert.False(imported.Error, imported.Text);
        var plan = JsonNode.Parse(imported.Structured!.Value.GetRawText())!["result"]!;
        Assert.Equal("fresh", plan["baseVerdict"]!.GetValue<string>());
        Assert.Equal(0, plan["changeCount"]!.GetValue<int>());
        Assert.Equal(0, plan["operationCount"]!.GetValue<int>());
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));

        var outside = Path.Combine(_folder, "outside.md");
        var escapes = Path.Combine(projectFolder, "..", Path.GetFileName(_folder), "escape.md");
        foreach (var candidate in new[] { outside, escapes, Path.GetFullPath("/etc/motif-export-test.md") })
        {
            var refused = await Call("motif_record_export", ("out", candidate), ("recordId", entry.ToString("D")));
            Assert.True(refused.Error, candidate);
            Assert.Contains(RefusalCodes.RecordExportOutputUnwritable, refused.Text, StringComparison.Ordinal);
        }
        Assert.False(File.Exists(outside));
        Assert.False(File.Exists(Path.Combine(_folder, "escape.md")));
    }
}
