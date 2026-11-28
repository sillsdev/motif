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
        Assert.Equal(["dir", "draft", "file", "licence", "page", "project", "refused_only", "revision", "source", "strict"], tool.ArgumentNames.Order(StringComparer.Ordinal));
        Assert.False(tool.InputSchema["additionalProperties"]!.GetValue<bool>());
        Assert.NotNull(tool.OutputSchema);
        Assert.Contains(tool.Routes, route => route.Command == "import");
        Assert.DoesNotContain(AgentTools.All, item => item.Name.Contains("apply", StringComparison.Ordinal));
    }

    private (string Path, string File) Fixture()
    {
        Directory.CreateDirectory(_folder);
        string path;
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
        path = cache.ProjectId.Path;
        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
        var exporter = new ProjectRecordExporter(cache, schema);
        var read = RecordFileReader.Read(exporter.Export(entryId).Text!, schema.Schema, exporter.CreateReadOptions());
        var sense0 = (RecordObject)((RecordList)read.Document!.Data["senses"]!).Items[0];
        ((RecordAlternatives)sense0["gloss"]!).Add("en", new RecordScalar("zzedited"));
        var file = Path.Combine(Path.GetDirectoryName(path)!, "entry.md");
        File.WriteAllText(file, RecordFileWriter.Write(read.Document!, schema.Schema));
        return (path, file);
    }


    /// <summary>A saved project and a file editing seven senses' glosses, so lists span two pages.</summary>
    private static (string Project, string File) SevenEdits(PristineProjectFixture pristine, string folder)
    {
        Directory.CreateDirectory(folder);
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
            for (var index = 0; index < 7; index++)
            {
                var sense = services.GetInstance<ILexSenseFactory>().Create();
                entry.SensesOS.Add(sense);
                sense.Gloss.set_String(cache.DefaultAnalWs, "zzgloss" + index);
            }
        });
        new FwDataProjectLoader().Save(cache);
        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
        var exporter = new ProjectRecordExporter(cache, schema);
        var read = RecordFileReader.Read(exporter.Export(entryId).Text!, schema.Schema, exporter.CreateReadOptions());
        foreach (var item in ((RecordList)read.Document!.Data["senses"]!).Items)
            ((RecordAlternatives)((RecordObject)item)["gloss"]!).Add("en", new RecordScalar("zzedited"));
        var file = Path.Combine(folder, "seven.md");
        File.WriteAllText(file, RecordFileWriter.Write(read.Document!, schema.Schema));
        return (cache.ProjectId.Path, file);
    }

    private static ServerContext Context(string path, ToolProfile profile) =>
        new(path, "0.1.0", new NoRunnerLauncher(new JobRunnerLaunchOptions(RunnerOptions.ResolveRoot(), null)),
            new ActivityLog(), profile, TextWriter.Null, advancedAiModeEnabled: true);

    private static Dictionary<string, JsonElement> Args(params (string Name, object Value)[] arguments) =>
        arguments.ToDictionary(item => item.Name, item => JsonSerializer.SerializeToElement(item.Value));

    [Fact]
    public async Task OnlyAFileUnderTheProjectFolderOrTheWorkingDirectoryIsRead()
    {
        var (path, file) = Fixture();
        using var privateRoot = PrivateManagedRoot.Use(Path.GetDirectoryName(path)!);
        KnownProjectTestSetup.AssignUniqueTag(RunnerOptions.ResolveRoot(), path, "import-roots");
        var profile = new ToolProfile("import-roots", null, null, "concise", [], AllTools: true);
        var tools = MotifMcpServer.Expose(profile);
        var projectFolder = Path.GetDirectoryName(path)!;

        async Task<(bool Error, string Text)> Call(string candidate)
        {
            var result = await MotifMcpServer.CallAsync(Context(path, profile), tools, "motif_import_record",
                Args(("project", path), ("file", candidate)), CancellationToken.None);
            return (result.IsError == true, result.StructuredContent?.ToString() ?? "");
        }

        Assert.False((await Call(file)).Error);

        var outside = Path.Combine(_folder, "outside.md");
        File.Copy(file, outside, true);
        var escapes = Path.Combine(projectFolder, "..", Path.GetFileName(_folder), "outside.md");
        var link = Path.Combine(projectFolder, "link.md");
        var linked = false;
        try { File.CreateSymbolicLink(link, outside); linked = true; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { }
        var candidates = new List<string> { outside, escapes, Path.GetFullPath("/etc/hostname") };
        if (linked) candidates.Add(link);
        foreach (var candidate in candidates)
        {
            var refused = await Call(candidate);
            Assert.True(refused.Error, candidate);
            Assert.Contains(RefusalCodes.RecordImportFileUnreadable, refused.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AFolderIsReadOnlyUnderTheRootsAndALinkedFolderOrFileCannotLeadOut()
    {
        var (path, file) = Fixture();
        using var privateRoot = PrivateManagedRoot.Use(Path.GetDirectoryName(path)!);
        KnownProjectTestSetup.AssignUniqueTag(RunnerOptions.ResolveRoot(), path, "import-dir-roots");
        var profile = new ToolProfile("import-dir-roots", null, null, "concise", [], AllTools: true);
        var tools = MotifMcpServer.Expose(profile);
        var projectFolder = Path.GetDirectoryName(path)!;
        var inside = Path.Combine(projectFolder, "records");
        Directory.CreateDirectory(inside);
        File.Copy(file, Path.Combine(inside, "entry.fw.md"));
        var outside = Path.Combine(_folder, "outside");
        Directory.CreateDirectory(outside);
        File.Copy(file, Path.Combine(outside, "other.fw.md"));

        async Task<(bool Error, string Text)> Call(params (string Name, object Value)[] extra)
        {
            var result = await MotifMcpServer.CallAsync(Context(path, profile), tools, "motif_import_record",
                Args([("project", path), .. extra]), CancellationToken.None);
            return (result.IsError == true, result.StructuredContent?.ToString() ?? "");
        }

        var ok = await Call(("dir", inside));
        Assert.False(ok.Error, ok.Text);
        Assert.Contains("\"updated\":1", ok.Text.Replace(" ", "").Replace("\n", "").Replace("\r", ""), StringComparison.Ordinal);
        var rows = await Call(("dir", inside), ("page", 1));
        Assert.False(rows.Error, rows.Text);
        Assert.Contains("entry.fw.md", rows.Text, StringComparison.Ordinal);
        var root = await Call(("dir", projectFolder));
        Assert.False(root.Error, root.Text);
        Assert.True((await Call(("dir", outside))).Error);
        Assert.True((await Call(("dir", Path.Combine(inside, "..", "..", Path.GetFileName(_folder), "outside")))).Error);
        Assert.True((await Call(("dir", inside), ("file", file))).Error);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(inside, "escape"), outside);
            File.CreateSymbolicLink(Path.Combine(inside, "escaped.fw.md"), Path.Combine(outside, "other.fw.md"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { return; }
        var viaLink = await Call(("dir", Path.Combine(projectFolder, "records", "escape")));
        Assert.True(viaLink.Error);
        Assert.Contains(RefusalCodes.RecordImportFileUnreadable, viaLink.Text, StringComparison.Ordinal);
        var walked = await Call(("dir", inside), ("page", 1));
        Assert.DoesNotContain("other.fw.md", walked.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("escaped.fw.md", walked.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDraftArgumentCreatesExactlyOneDraftAppliesNothingAndMatchesTheCli()
    {
        var (path, file) = Fixture();
        var before = SHA256.HashData(File.ReadAllBytes(path));
        using var privateRoot = PrivateManagedRoot.Use(Path.GetDirectoryName(path)!);
        KnownProjectTestSetup.AssignUniqueTag(RunnerOptions.ResolveRoot(), path, "import-draft");
        var profile = new ToolProfile("import-draft", null, null, "concise", [], AllTools: true);

        var called = await MotifMcpServer.CallAsync(Context(path, profile), MotifMcpServer.Expose(profile),
            "motif_import_record", Args(("project", path), ("file", file), ("draft", "mcp-draft")), CancellationToken.None);
        Assert.False(called.IsError == true, called.StructuredContent?.ToString());
        var mcp = JsonNode.Parse(called.StructuredContent!.Value.GetRawText())!["result"]!.AsObject();
        Assert.Equal("draft", mcp["mode"]!.GetValue<string>());
        var listed = ProposalCommands.List(new(path, "0.1.0"));
        Assert.True(listed.Succeeded, listed.Refusal?.Message);
        Assert.Single(listed.Value!.Proposals);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));

        var cli = CliInProcess.Run(RunnerOptions.ResolveRoot(), FakeParser.ExecutablePath, true,
            "import", "--project", path, "--file", file, "--draft", "cli-draft", "--json");
        Assert.True(cli.ExitCode == 0, cli.Error);
        var cliNode = JsonNode.Parse(cli.Output)!.AsObject();
        foreach (var node in new[] { mcp, cliNode })
        {
            node.Remove("draftName");
            node.Remove("proposalId");
            // The MCP wrapper adds this notice to any Draft-class result; the response type does not carry it.
            node.Remove("commentsChanged");
        }
        Assert.True(JsonNode.DeepEquals(cliNode, mcp), cli.Output + "\n" + mcp.ToJsonString());
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
    }

    [Fact]
    public async Task AFileForANewEntryStagesOneDraftAndAnIncompleteOneIsRefusedWithItsCode()
    {
        var (path, file) = CreatedEntryFile.Build(pristine);
        var incomplete = CreatedEntryFile.Edited(path, file, "incomplete.md", document => document.Data["senses"] = null);
        using var privateRoot = PrivateManagedRoot.Use(Path.GetDirectoryName(path)!);
        KnownProjectTestSetup.AssignUniqueTag(RunnerOptions.ResolveRoot(), path, "import-create");
        var profile = new ToolProfile("import-create", null, null, "concise", [], AllTools: true);
        var tools = MotifMcpServer.Expose(profile);

        var refused = await MotifMcpServer.CallAsync(Context(path, profile), tools, "motif_import_record",
            Args(("project", path), ("file", incomplete), ("draft", "bad")), CancellationToken.None);
        var staged = await MotifMcpServer.CallAsync(Context(path, profile), tools, "motif_import_record",
            Args(("project", path), ("file", file), ("draft", "new-entry")), CancellationToken.None);

        Assert.False(staged.IsError == true, staged.StructuredContent?.ToString());
        var result = JsonNode.Parse(staged.StructuredContent!.Value.GetRawText())!["result"]!.AsObject();
        Assert.Equal("new", result["baseVerdict"]!.GetValue<string>());
        Assert.True(result["operationCount"]!.GetValue<int>() > 0);
        Assert.True(refused.IsError == true);
        Assert.Contains(RefusalCodes.RecordImportCreateShape, refused.StructuredContent?.ToString() ?? "", StringComparison.Ordinal);
        Assert.Single(ProposalCommands.List(new(path, "0.1.0")).Value!.Proposals);
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
            file = Path.Combine(Path.GetDirectoryName(path)!, "entry.md");
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
            new ActivityLog(), profile, TextWriter.Null, advancedAiModeEnabled: true);
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

    [Fact]
    public async Task PageTwoThroughTheToolListsTheRest()
    {
        var (path, file) = SevenEdits(pristine, _folder);
        using var privateRoot = PrivateManagedRoot.Use(Path.GetDirectoryName(path)!);
        KnownProjectTestSetup.AssignUniqueTag(RunnerOptions.ResolveRoot(), path, "import-page");
        var profile = new ToolProfile("import-page", null, null, "concise", [], AllTools: true);
        var tools = MotifMcpServer.Expose(profile);
        var inside = Path.Combine(Path.GetDirectoryName(path)!, "seven.md");
        File.Copy(file, inside, true);

        var called = await MotifMcpServer.CallAsync(Context(path, profile), tools, "motif_import_record",
            Args(("project", path), ("file", inside), ("page", 2)), CancellationToken.None);

        Assert.False(called.IsError == true, called.StructuredContent?.ToString());
        var result = JsonNode.Parse(called.StructuredContent!.Value.GetRawText())!["result"]!.AsObject();
        Assert.Equal(2, result["page"]!.GetValue<int>());
        Assert.Equal(3, result["pageCount"]!.GetValue<int>());
        Assert.Equal(3, result["operations"]!.AsArray().Count);
        Assert.Equal(3, result["changes"]!.AsArray().Count);
    }

    [Fact]
    public async Task StrictThroughTheToolIsAcceptedOnACleanFile()
    {
        var (path, file) = Fixture();
        using var privateRoot = PrivateManagedRoot.Use(Path.GetDirectoryName(path)!);
        KnownProjectTestSetup.AssignUniqueTag(RunnerOptions.ResolveRoot(), path, "import-strict");
        var profile = new ToolProfile("import-strict", null, null, "concise", [], AllTools: true);

        var called = await MotifMcpServer.CallAsync(Context(path, profile), MotifMcpServer.Expose(profile),
            "motif_import_record", Args(("project", path), ("file", file), ("draft", "strict-draft"), ("strict", true)),
            CancellationToken.None);

        Assert.False(called.IsError == true, called.StructuredContent?.ToString());
        Assert.Equal("draft", JsonNode.Parse(called.StructuredContent!.Value.GetRawText())!["result"]!["mode"]!.GetValue<string>());
    }

    [Fact]
    public async Task StrictWithARefusedChangeIsAnErrorAndAPartialStageSucceedsWithTheCount()
    {
        var (path, file) = RecordImportDoomedFile.Create(pristine, Path.Combine(_folder, "doomed"));
        var inside = Path.Combine(Path.GetDirectoryName(path)!, "doomed.md");
        File.Copy(file, inside, true);
        using var privateRoot = PrivateManagedRoot.Use(Path.GetDirectoryName(path)!);
        KnownProjectTestSetup.AssignUniqueTag(RunnerOptions.ResolveRoot(), path, "import-doomed");
        var profile = new ToolProfile("import-doomed", null, null, "concise", [], AllTools: true);
        var tools = MotifMcpServer.Expose(profile);

        var strict = await MotifMcpServer.CallAsync(Context(path, profile), tools, "motif_import_record",
            Args(("project", path), ("file", inside), ("draft", "mcp-strict"), ("strict", true)), CancellationToken.None);
        Assert.True(strict.IsError == true);
        Assert.Contains(RefusalCodes.RecordImportStrictRefused, strict.StructuredContent?.ToString(), StringComparison.Ordinal);
        Assert.Empty(ProposalCommands.List(new(path, "0.1.0")).Value?.Proposals ?? []);

        var partial = await MotifMcpServer.CallAsync(Context(path, profile), tools, "motif_import_record",
            Args(("project", path), ("file", inside), ("draft", "mcp-partial")), CancellationToken.None);
        Assert.False(partial.IsError == true, partial.StructuredContent?.ToString());
        Assert.Equal(1, JsonNode.Parse(partial.StructuredContent!.Value.GetRawText())!["result"]!["refusalCount"]!.GetValue<int>());

        var noDraft = await MotifMcpServer.CallAsync(Context(path, profile), tools, "motif_import_record",
            Args(("project", path), ("file", inside), ("strict", true)), CancellationToken.None);
        Assert.True(noDraft.IsError == true);
        Assert.Contains(RefusalCodes.RecordImportStrictNeedsDraft, noDraft.StructuredContent?.ToString(), StringComparison.Ordinal);
    }
}
