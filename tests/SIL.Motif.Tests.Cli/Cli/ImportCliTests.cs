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

    public ImportCliTests(PristineProjectFixture pristine)
    {
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
}
