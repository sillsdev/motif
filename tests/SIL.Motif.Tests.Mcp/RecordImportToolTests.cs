using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Mcp;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Project;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RecordImportToolTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "motif-import-mcp-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); }
        catch (IOException) { }
    }

    [Fact]
    public void TheToolIsADraftToolThatTakesAFileAnOptionalDraftAndAPageAndNeverApplies()
    {
        var tool = AgentTools.All.Single(item => item.Name == "motif_import_record");
        Assert.Equal(SIL.Motif.Commands.Catalog.AgentClass.Draft, tool.Class);
        Assert.False(tool.ReadOnly);
        Assert.Equal(["draft", "file", "page", "project"], tool.ArgumentNames.Order(StringComparer.Ordinal));
        Assert.False(tool.InputSchema["additionalProperties"]!.GetValue<bool>());
        Assert.NotNull(tool.OutputSchema);
        Assert.Contains(tool.Routes, route => route.Command == "import");
        Assert.DoesNotContain(AgentTools.All, item => item.Name.Contains("apply", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CliAndMcpReturnTheSameResponseAndTheMcpOutputConformsToTheSharedSchema()
    {
        Directory.CreateDirectory(_folder);
        string path, file;
        using (var cache = pristine.NewScratch())
        {
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
            path = cache.ProjectId.Path;
            var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
            var exporter = new ProjectRecordExporter(cache, schema);
            var read = RecordFileReader.Read(exporter.Export(entryId).Text!, schema.Schema, exporter.CreateReadOptions());
            var sense0 = (RecordObject)((RecordList)read.Document!.Data["senses"]!).Items[0];
            ((RecordAlternatives)sense0["gloss"]!).Add("en", new RecordScalar("zzedited"));
            file = Path.Combine(_folder, "entry.md");
            File.WriteAllText(file, RecordFileWriter.Write(read.Document!, schema.Schema));
        }
        var before = SHA256.HashData(File.ReadAllBytes(path));
        using var privateRoot = PrivateManagedRoot.Use(Path.GetDirectoryName(path)!);
        KnownProjectTestSetup.AssignUniqueTag(RunnerOptions.ResolveRoot(), path, "import-shape");

        var cli = CliInProcess.Run(RunnerOptions.ResolveRoot(), FakeParser.ExecutablePath, true,
            "import", "--project", path, "--file", file, "--json");
        Assert.True(cli.ExitCode == 0, cli.Error);

        var profile = new ToolProfile("import-shape", null, null, "concise", [], AllTools: true);
        var context = new ServerContext(path, "0.1.0", new NoRunnerLauncher(new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), null)),
            new ActivityLog(null), profile, TextWriter.Null, advancedAiModeEnabled: true);
        var called = await MotifMcpServer.CallAsync(context, MotifMcpServer.Expose(profile), "motif_import_record",
            new Dictionary<string, JsonElement>
            {
                ["project"] = JsonSerializer.SerializeToElement(path),
                ["file"] = JsonSerializer.SerializeToElement(file),
            }, CancellationToken.None);
        Assert.False(called.IsError == true, called.StructuredContent?.ToString());

        var mcp = JsonNode.Parse(called.StructuredContent!.Value.GetRawText())!;
        var result = mcp["result"]!;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(cli.Output), result),
            "CLI: " + cli.Output + "\nMCP: " + result.ToJsonString());
        var validator = JsonSchema.FromText(RecordImportToolSchema.Output.ToJsonString());
        Assert.True(validator.Evaluate(called.StructuredContent.Value).IsValid);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
    }
}
