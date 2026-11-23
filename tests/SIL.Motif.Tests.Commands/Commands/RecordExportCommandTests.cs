using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Records;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.TextEditing.Project;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class RecordExportCommandTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "motif-export-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }

    private (string Project, Guid Entry) Fixture(int entries = 1)
    {
        Directory.CreateDirectory(_folder);
        using var cache = pristine.NewScratch();
        Guid first = default;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            for (var index = 0; index < entries; index++)
            {
                var entry = services.GetInstance<ILexEntryFactory>().Create();
                if (index == 0) first = entry.Guid;
                var form = services.GetInstance<IMoStemAllomorphFactory>().Create();
                entry.LexemeFormOA = form;
                form.Form.set_String(cache.DefaultVernWs, "zzword" + index);
                var sense = services.GetInstance<ILexSenseFactory>().Create();
                entry.SensesOS.Add(sense);
                sense.Gloss.set_String(cache.DefaultAnalWs, "zzgloss" + index);
                sense.Definition.set_String(cache.DefaultAnalWs, "zzdefinition" + index);
            }
        });
        new FwDataProjectLoader().Save(cache);
        return (cache.ProjectId.Path, first);
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    [Fact]
    public void ExportingOneRecordThenImportingItPlansNoChange()
    {
        var (project, entry) = Fixture();
        var before = Hash(project);
        var file = Path.Combine(_folder, "entry.md");

        var exported = RecordExportCommand.Run(new(project, file, entry.ToString("D")));

        Assert.True(exported.Succeeded, exported.Refusal?.Message);
        Assert.Equal("lexicon/entry", exported.Value!.RecordKind);
        Assert.Equal(1, exported.Value.WrittenCount);
        Assert.Equal(0, exported.Value.NotWrittenCount);
        Assert.True(exported.Value.UnexportedCount > 0);
        Assert.Equal(exported.Value.UnexportedCount, exported.Value.Unexported.Sum(item => item.Count));
        Assert.Equal(before, Hash(project));

        var imported = RecordImportCommand.Run(new(project, file));
        Assert.True(imported.Succeeded, imported.Refusal?.Message);
        Assert.Equal("fresh", imported.Value!.BaseVerdict);
        Assert.Equal(0, imported.Value.ChangeCount);
        Assert.Equal(0, imported.Value.OperationCount);
        Assert.Equal(0, imported.Value.RefusalCount);
        Assert.Equal(before, Hash(project));
    }

    [Fact]
    public void ExportingEveryRecordWritesOneFilePerRecordNamedByItsGuidAndEachImportsAsNoChange()
    {
        var (project, _) = Fixture(3);
        var folder = Path.Combine(_folder, "all");

        var exported = RecordExportCommand.Run(new(project, folder, All: true));

        Assert.True(exported.Succeeded, exported.Refusal?.Message);
        Assert.Equal(3, exported.Value!.RecordCount);
        Assert.Equal(3, exported.Value.WrittenCount);
        var files = Directory.GetFiles(folder);
        Assert.Equal(3, files.Length);
        Assert.All(files, file => Assert.Matches(@"^[0-9a-f]{8}-([0-9a-f]{4}-){3}[0-9a-f]{12}\.md$", Path.GetFileName(file)));
        foreach (var file in files)
        {
            var imported = RecordImportCommand.Run(new(project, file));
            Assert.True(imported.Succeeded, imported.Refusal?.Message);
            Assert.Equal(0, imported.Value!.ChangeCount);
        }
    }

    [Fact]
    public void ExportingAgainAfterReopeningTheProjectWritesTheSameBytes()
    {
        var (project, entry) = Fixture();
        string FirstExport()
        {
            using var cache = new FwDataProjectLoader().LoadCache(project);
            var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
            return new ProjectRecordExporter(cache, schema).Export(entry).Text!;
        }
        var one = FirstExport();
        var two = FirstExport();
        Assert.Equal(one, two);

        var a = Path.Combine(_folder, "a.md");
        var b = Path.Combine(_folder, "b.md");
        Assert.True(RecordExportCommand.Run(new(project, a, entry.ToString("D"))).Succeeded);
        Assert.True(RecordExportCommand.Run(new(project, b, entry.ToString("D"))).Succeeded);
        Assert.Equal(File.ReadAllBytes(a), File.ReadAllBytes(b));
        Assert.Equal(Encoding.UTF8.GetBytes(one), File.ReadAllBytes(a));
    }

    [Fact]
    public void AnEditedFileOfAProjectThatChangedSinceTheExportIsRefusedAsStale()
    {
        var (project, entry) = Fixture();
        var file = Path.Combine(_folder, "entry.md");
        Assert.True(RecordExportCommand.Run(new(project, file, entry.ToString("D"))).Succeeded);
        using (var cache = new FwDataProjectLoader().LoadCache(project))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(entry).SensesOS[0].Gloss.set_String(cache.DefaultAnalWs, "zzchanged"));
            new FwDataProjectLoader().Save(cache);
        }

        var imported = RecordImportCommand.Run(new(project, file));

        Assert.Equal(RefusalCodes.RecordImportPlanRefused, imported.Refusal!.Code);
        Assert.Equal("stale", imported.Refusal.Facts["baseVerdict"]);
    }

    [Fact]
    public void ABadRequestOrAnExistingFileIsRefusedAndWritesNothing()
    {
        var (project, entry) = Fixture();
        var file = Path.Combine(_folder, "entry.md");
        var id = entry.ToString("D");

        Assert.Equal(RefusalCodes.RecordExportInvalidRequest, RecordExportCommand.Run(new(project, file)).Refusal!.Code);
        Assert.Equal(RefusalCodes.RecordExportInvalidRequest, RecordExportCommand.Run(new(project, file, id, null, true)).Refusal!.Code);
        Assert.Equal(RefusalCodes.RecordExportInvalidRequest, RecordExportCommand.Run(new(project, file, "not-a-guid")).Refusal!.Code);
        Assert.Equal(RefusalCodes.RecordExportNotFound, RecordExportCommand.Run(new(project, file, Guid.NewGuid().ToString("D"))).Refusal!.Code);
        Assert.Equal(RefusalCodes.RecordExportNotFound, RecordExportCommand.Run(new(project, file, id, "nonsense/kind")).Refusal!.Code);
        Assert.False(File.Exists(file));

        Assert.True(RecordExportCommand.Run(new(project, file, id)).Succeeded);
        var before = Hash(file);
        var again = RecordExportCommand.Run(new(project, file, id));
        Assert.Equal(RefusalCodes.RecordExportOutputUnwritable, again.Refusal!.Code);
        Assert.Equal(before, Hash(file));
        foreach (var code in new[] { RefusalCodes.RecordExportInvalidRequest, RefusalCodes.RecordExportNotFound, RefusalCodes.RecordExportOutputUnwritable })
            Assert.Contains(code, RefusalCatalog.Specs.Keys);
    }

    [Fact]
    public void AnExistingFileAmongManyRefusesTheWholeFolderBeforeAnythingIsWritten()
    {
        var (project, entry) = Fixture(2);
        var folder = Path.Combine(_folder, "all");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, entry.ToString("D") + ".md"), "keep");

        var outcome = RecordExportCommand.Run(new(project, folder, All: true));

        Assert.Equal(RefusalCodes.RecordExportOutputUnwritable, outcome.Refusal!.Code);
        Assert.Single(Directory.GetFiles(folder));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(folder, entry.ToString("D") + ".md")));
    }

    private static readonly Regex Guids = new(@"[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}", RegexOptions.Compiled);

    private static string Normalize(string text)
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var replaced = Guids.Replace(text, match =>
        {
            if (!seen.TryGetValue(match.Value, out var name)) seen[match.Value] = name = "GUID-" + (seen.Count + 1);
            return name;
        });
        return Regex.Replace(replaced, "sha256:[0-9a-f]{64}", "sha256:DIGEST").ReplaceLineEndings("\n");
    }

    private static string Resource(string name)
    {
        using var stream = typeof(RecordExportCommandTests).Assembly.GetManifestResourceStream("RecordExport." + name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    }

    private static string Report(RecordExportResponse response) =>
        string.Concat(response.Unexported.Select(item => $"{item.Count}\t{item.Class}.{item.Field}\t{item.Reason}\n"));

    [Fact]
    public void TheExportedFileAndTheReportedListMatchTheirGoldens()
    {
        var (project, entry) = Fixture();
        var file = Path.Combine(_folder, "entry.md");

        var exported = RecordExportCommand.Run(new(project, file, entry.ToString("D")));

        Assert.True(exported.Succeeded, exported.Refusal?.Message);
        var actualFile = Normalize(File.ReadAllText(file));
        var actualReport = Report(exported.Value!);
        var candidates = Directory.CreateTempSubdirectory("motif-record-export-golden-").FullName;
        File.WriteAllText(Path.Combine(candidates, "entry.md"), actualFile);
        File.WriteAllText(Path.Combine(candidates, "unexported.tsv"), actualReport);
        Assert.True(Resource("entry.md") == actualFile, "The export differs from its golden; the candidate is at " + candidates);
        Assert.True(Resource("unexported.tsv") == actualReport, "The reported list differs from its golden; the candidate is at " + candidates);
    }
}
