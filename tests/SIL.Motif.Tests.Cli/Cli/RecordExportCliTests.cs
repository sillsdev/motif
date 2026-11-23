using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Cli;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>The <c>record export</c> verb through the in-process CLI, and the file it writes read back by <c>import</c>.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class RecordExportCliTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-export-cli-" + Guid.NewGuid().ToString("N"));
    private readonly string _project;
    private readonly Guid _entry;
    private readonly int _total;

    public RecordExportCliTests(PristineProjectFixture pristine)
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        _project = cache.ProjectId.Path;
        Guid entryId = default;
        var seeded = cache.ServiceLocator.GetInstance<ILexEntryRepository>().Count;
        _total = seeded + 2;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            for (var index = 0; index < 2; index++)
            {
                var entry = services.GetInstance<ILexEntryFactory>().Create();
                if (index == 0) entryId = entry.Guid;
                var form = services.GetInstance<IMoStemAllomorphFactory>().Create();
                entry.LexemeFormOA = form;
                form.Form.set_String(cache.DefaultVernWs, "zzword" + index);
                var sense = services.GetInstance<ILexSenseFactory>().Create();
                entry.SensesOS.Add(sense);
                sense.Gloss.set_String(cache.DefaultAnalWs, "zzgloss" + index);
            }
        });
        new FwDataProjectLoader().Save(cache);
        _entry = entryId;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (IOException) { }
    }

    private CliProcessResult Run(params string[] arguments) =>
        CliInProcess.Run(Path.Combine(_root, "machine"), FakeParser.ExecutablePath, true, arguments);

    [Fact]
    public void TheVerbIsCataloguedAndItsHelpSaysToExportAgainWhenTheProjectChanged()
    {
        var verb = Assert.Single(CliVerbCatalog.All, item => item.CommandName == "record export");
        Assert.Equal("record export", verb.Verb);
        var help = Run("help", "record export");
        Assert.True(help.ExitCode == 0, help.Error);
        var text = help.Output + help.Error;
        Assert.Contains("--out", text, StringComparison.Ordinal);
        Assert.Contains("export the record again and redo the edits; there is no merge", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExportThenImportThroughTheCliPlansNoChangeAndLeavesTheProjectAlone()
    {
        var before = SHA256.HashData(File.ReadAllBytes(_project));
        var file = Path.Combine(_root, "entry.md");

        var exported = Run("record", "export", "--project", _project, "--out", file, "--record", _entry.ToString("D"), "--json");

        Assert.True(exported.ExitCode == 0, exported.Error + exported.Output);
        var node = JsonNode.Parse(exported.Output)!;
        Assert.Equal("lexicon/entry", node["recordKind"]!.GetValue<string>());
        Assert.Equal(1, node["writtenCount"]!.GetValue<int>());
        Assert.True(node["unexportedCount"]!.GetValue<int>() > 0);
        Assert.True(File.Exists(file));

        var imported = Run("import", "--project", _project, "--file", file, "--json");
        Assert.True(imported.ExitCode == 0, imported.Error + imported.Output);
        var plan = JsonNode.Parse(imported.Output)!;
        Assert.Equal("dryRun", plan["mode"]!.GetValue<string>());
        Assert.Equal("fresh", plan["baseVerdict"]!.GetValue<string>());
        Assert.Equal(0, plan["changeCount"]!.GetValue<int>());
        Assert.Equal(0, plan["operationCount"]!.GetValue<int>());
        Assert.Equal(0, plan["refusalCount"]!.GetValue<int>());
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(_project)));
    }

    [Fact]
    public void AllWritesOneFilePerRecordAndTheTextReportNamesWhatIsLeftOut()
    {
        var folder = Path.Combine(_root, "all");

        var exported = Run("record", "export", "--project", _project, "--out", folder, "--all");

        Assert.True(exported.ExitCode == 0, exported.Error + exported.Output);
        Assert.Equal(_total, Directory.GetFiles(folder).Length);
        Assert.Contains($"Wrote {_total} of {_total} lexicon/entry record file(s)", exported.Output, StringComparison.Ordinal);
        Assert.Contains("left out of the file(s)", exported.Output, StringComparison.Ordinal);
        Assert.Contains("export again", exported.Output, StringComparison.Ordinal);
        foreach (var file in Directory.GetFiles(folder))
        {
            var plan = JsonNode.Parse(Run("import", "--project", _project, "--file", file, "--json").Output)!;
            Assert.Equal(0, plan["changeCount"]!.GetValue<int>());
        }
    }

    [Fact]
    public void AnExistingFileIsRefusedWithItsCode()
    {
        var file = Path.Combine(_root, "entry.md");
        File.WriteAllText(file, "keep");

        var result = Run("record", "export", "--project", _project, "--out", file, "--record", _entry.ToString("D"), "--json");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(RefusalCodes.RecordExportOutputUnwritable, ProjectionJson.Deserialize<FailureEnvelope>(result.Error)!.Code);
        Assert.Equal("keep", File.ReadAllText(file));
    }

    [Theory]
    [InlineData("record export --project {p}")]
    [InlineData("record export --out {o} --all")]
    [InlineData("record export --project {p} --out {o}")]
    [InlineData("record export --project {p} --out {o} --all --record {r}")]
    [InlineData("record export --project {p} --out {o} --all --draft x")]
    [InlineData("record export --project {p} --out {o} --all extra")]
    [InlineData("record --project {p} --out {o} --all")]
    public void UnknownOrMissingArgumentsAreAUsageError(string template)
    {
        var line = template.Replace("{p}", _project).Replace("{o}", Path.Combine(_root, "x")).Replace("{r}", _entry.ToString("D"));
        var result = Run(line.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToArray());

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(_root, "x")));
        Assert.False(Directory.Exists(Path.Combine(_root, "x")));
    }
}
