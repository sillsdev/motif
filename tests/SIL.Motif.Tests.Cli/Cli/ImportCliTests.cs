using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Cli;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Project;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>The <c>import</c> verb through the in-process CLI: dry run, <c>--draft</c>, a refused file and bad arguments.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class ImportCliTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-import-cli-" + Guid.NewGuid().ToString("N"));
    private readonly string _project;
    private readonly string _file;
    private readonly PristineProjectFixture _pristine;

    public ImportCliTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        _project = cache.ProjectId.Path;
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
        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
        var exporter = new ProjectRecordExporter(cache, schema);
        var read = RecordFileReader.Read(exporter.Export(entryId).Text!, schema.Schema, exporter.CreateReadOptions());
        Assert.False(read.HasErrors);
        var sense0 = (RecordObject)((RecordList)read.Document!.Data["senses"]!).Items[0];
        ((RecordAlternatives)sense0["gloss"]!).Add("en", new RecordScalar("zzedited"));
        _file = Path.Combine(_root, "entry.md");
        File.WriteAllText(_file, RecordFileWriter.Write(read.Document!, schema.Schema));
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

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (IOException) { }
    }

    private CliProcessResult Run(params string[] arguments) =>
        CliInProcess.Run(Path.Combine(_root, "machine"), FakeParser.ExecutablePath, true, arguments);

    [Fact]
    public void TheVerbIsCataloguedAndHelpListsIt()
    {
        var verb = Assert.Single(CliVerbCatalog.All, item => item.CommandName == "import");
        Assert.Equal("import", verb.Verb);
        var help = Run("help", "import");
        Assert.True(help.ExitCode == 0, help.Error);
        Assert.Contains("--file", help.Output + help.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ADryRunPrintsTheOperationsAndWritesNothing()
    {
        var before = SHA256.HashData(File.ReadAllBytes(_project));

        var text = Run("import", "--project", _project, "--file", _file);
        Assert.True(text.ExitCode == 0, text.Error + text.Output);
        Assert.Contains("Dry run", text.Output, StringComparison.Ordinal);
        Assert.Contains("lexical/lexSense/setGloss", text.Output, StringComparison.Ordinal);
        Assert.Contains("Nothing was written", text.Output, StringComparison.Ordinal);

        var json = Run("import", "--project", _project, "--file", _file, "--json");
        Assert.True(json.ExitCode == 0, json.Error);
        var node = JsonNode.Parse(json.Output)!;
        Assert.Equal("dryRun", node["mode"]!.GetValue<string>());
        Assert.Equal(1, node["operationCount"]!.GetValue<int>());
        Assert.Null(node["draftName"]);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(_project)));
    }

    [Fact]
    public void DraftCreatesExactlyOneDraftAndTheProjectIsUnchanged()
    {
        var before = SHA256.HashData(File.ReadAllBytes(_project));

        var staged = Run("import", "--project", _project, "--file", _file, "--draft", "from-file", "--json");

        Assert.True(staged.ExitCode == 0, staged.Error + staged.Output);
        var node = JsonNode.Parse(staged.Output)!;
        Assert.Equal("draft", node["mode"]!.GetValue<string>());
        Assert.Equal("from-file", node["draftName"]!.GetValue<string>());
        Assert.Equal(1, node["operationCount"]!.GetValue<int>());
        var listed = Run("proposals", "--project", _project, "--json");
        Assert.True(listed.ExitCode == 0, listed.Error);
        var proposals = JsonNode.Parse(listed.Output)!["proposals"]!.AsArray();
        Assert.Equal(node["proposalId"]!.GetValue<string>(), Assert.Single(proposals)!["proposalId"]!.GetValue<string>());
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(_project)));
        // A second import under the same name is refused and adds nothing.
        var again = Run("import", "--project", _project, "--file", _file, "--draft", "from-file", "--json");
        Assert.NotEqual(0, again.ExitCode);
    }

    [Fact]
    public void AFileForANewEntryIsCheckedAndThenStagedAsOneDraftWithTheProjectUnchanged()
    {
        var (project, file) = CreatedEntryFile.Build(_pristine);
        var before = SHA256.HashData(File.ReadAllBytes(project));

        var dry = Run("import", "--project", project, "--file", file, "--json");
        var staged = Run("import", "--project", project, "--file", file, "--draft", "new-entry", "--json");

        Assert.True(dry.ExitCode == 0, dry.Error + dry.Output);
        var dryNode = JsonNode.Parse(dry.Output)!;
        Assert.Equal("dryRun", dryNode["mode"]!.GetValue<string>());
        Assert.Equal("new", dryNode["baseVerdict"]!.GetValue<string>());
        Assert.True(dryNode["operationCount"]!.GetValue<int>() > 0);
        Assert.Equal(0, dryNode["refusalCount"]!.GetValue<int>());
        Assert.True(staged.ExitCode == 0, staged.Error + staged.Output);
        Assert.Equal("draft", JsonNode.Parse(staged.Output)!["mode"]!.GetValue<string>());
        var listed = Run("proposals", "--project", project, "--json");
        Assert.Single(JsonNode.Parse(listed.Output)!["proposals"]!.AsArray());
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(project)));
    }

    [Fact]
    public void AFileForANewEntryWithNoSenseIsRefusedWithItsCodeAndMakesNoDraft()
    {
        var (project, created) = CreatedEntryFile.Build(_pristine);
        var file = CreatedEntryFile.Edited(project, created, "no-sense.md", document => document.Data["senses"] = null);

        var result = Run("import", "--project", project, "--file", file, "--draft", "bad", "--json");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("record-import.create-shape", result.Output + result.Error, StringComparison.Ordinal);
        Assert.Empty(JsonNode.Parse(Run("proposals", "--project", project, "--json").Output)!["proposals"]!.AsArray());
    }

    [Fact]
    public void AStaleFileIsRefusedWithItsCodeAndANonZeroExit()
    {
        var stale = File.ReadAllText(_file).Replace("base:", "base: 0000", StringComparison.Ordinal);
        var path = Path.Combine(_root, "stale.md");
        File.WriteAllText(path, stale);

        var result = Run("import", "--project", _project, "--file", path, "--json");

        Assert.NotEqual(0, result.ExitCode);
        var envelope = ProjectionJson.Deserialize<FailureEnvelope>(result.Error)!;
        Assert.Equal(RefusalCodes.RecordImportPlanRefused, envelope.Code);
    }

    [Theory]
    [InlineData("import --project {p}")]
    [InlineData("import --file {f}")]
    [InlineData("import --project {p} --file {f} --draft")]
    [InlineData("import --project {p} --file {f} --page 0")]
    [InlineData("import --project {p} --file {f} --page two")]
    [InlineData("import --project {p} --file {f} --apply")]
    [InlineData("import --project {p} --file {f} extra")]
    public void UnknownOrBadArgumentsAreAUsageError(string template)
    {
        var line = template.Replace("{p}", _project).Replace("{f}", _file);
        var result = Run(line.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToArray());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("import --project <fwdata> --file <path>", result.Error + result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnreadableFileIsATypedRefusal()
    {
        var result = Run("import", "--project", _project, "--file", Path.Combine(_root, "absent.md"), "--json");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(RefusalCodes.RecordImportFileUnreadable, ProjectionJson.Deserialize<FailureEnvelope>(result.Error)!.Code);
    }

    [Fact]
    public void PageTwoListsTheRestAndAPastTheEndPageIsRefusedWithItsCode()
    {
        var (project, file) = SevenEdits(_pristine, Path.Combine(_root, "seven"));

        var one = Run("import", "--project", project, "--file", file, "--json");
        var two = Run("import", "--project", project, "--file", file, "--page", "2", "--json");

        Assert.True(one.ExitCode == 0 && two.ExitCode == 0, one.Error + two.Error);
        var first = JsonNode.Parse(one.Output)!;
        var second = JsonNode.Parse(two.Output)!;
        Assert.Equal(3, second["pageCount"]!.GetValue<int>());
        Assert.Equal(2, second["page"]!.GetValue<int>());
        Assert.Equal(3, first["operations"]!.AsArray().Count);
        Assert.Equal(3, second["operations"]!.AsArray().Count);
        Assert.Equal(3, first["changes"]!.AsArray().Count);
        Assert.Equal(3, second["changes"]!.AsArray().Count);
        var ids = first["operations"]!.AsArray().Concat(second["operations"]!.AsArray()).Select(item => item!["operationId"]!.GetValue<string>());
        Assert.Equal(6, ids.Distinct().Count());
        var past = Run("import", "--project", project, "--file", file, "--page", "4", "--json");
        Assert.NotEqual(0, past.ExitCode);
        Assert.Equal(RefusalCodes.RecordImportInvalidPage, ProjectionJson.Deserialize<FailureEnvelope>(past.Error)!.Code);
    }

    [Fact]
    public void StrictIsASwitchThatAcceptsACleanFileAndIsAUsageErrorWithoutAValue()
    {
        var staged = Run("import", "--project", _project, "--file", _file, "--draft", "strict-clean", "--strict", "--json");

        Assert.True(staged.ExitCode == 0, staged.Error + staged.Output);
        Assert.Equal("draft", JsonNode.Parse(staged.Output)!["mode"]!.GetValue<string>());
    }

    [Fact]
    public void StrictWithARefusedChangeExitsNonZeroWithItsCodeAndMakesNoDraft()
    {
        var (project, file) = RecordImportDoomedFile.Create(_pristine, Path.Combine(_root, "doomed-strict"));

        var result = Run("import", "--project", project, "--file", file, "--draft", "strict-doomed", "--strict", "--json");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(RefusalCodes.RecordImportStrictRefused, ProjectionJson.Deserialize<FailureEnvelope>(result.Error)!.Code);
        var listed = Run("proposals", "--project", project, "--json");
        Assert.Empty(JsonNode.Parse(listed.Output)!["proposals"]!.AsArray());
    }

    [Fact]
    public void ASomeRefusedImportWithoutStrictExitsZeroAndSaysHowManyWereRefused()
    {
        var (project, file) = RecordImportDoomedFile.Create(_pristine, Path.Combine(_root, "doomed-partial"));

        var json = Run("import", "--project", project, "--file", file, "--draft", "partial", "--json");
        Assert.True(json.ExitCode == 0, json.Error + json.Output);
        var node = JsonNode.Parse(json.Output)!;
        Assert.Equal(1, node["refusalCount"]!.GetValue<int>());
        Assert.Equal(1, node["operationCount"]!.GetValue<int>());
        var dry = Run("import", "--project", project, "--file", file);
        Assert.Contains("REFUSED record-import.change-refused", dry.Output, StringComparison.Ordinal);
        var text = Run("import", "--project", project, "--file", file, "--draft", "partial-text");
        Assert.Contains("1 change(s) refused and not staged", text.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void ACleanStrictStageExitsZeroAndStrictWithoutADraftIsAUsageError()
    {
        var clean = Run("import", "--project", _project, "--file", _file, "--draft", "strict-ok", "--strict");
        Assert.Equal(0, clean.ExitCode);

        var usage = Run("import", "--project", _project, "--file", _file, "--strict");
        Assert.Equal(1, usage.ExitCode);
        Assert.Contains("import --project <fwdata> --file <path>", usage.Error + usage.Output, StringComparison.Ordinal);
    }
}
