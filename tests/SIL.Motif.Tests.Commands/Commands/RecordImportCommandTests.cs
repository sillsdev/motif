using System.Security.Cryptography;
using System.Text;
using SIL.LCModel;
using SIL.LCModel.Core.Cellar;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Records;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Project;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class RecordImportCommandTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "motif-import-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }

    /// <summary>A saved project with glossed senses and the entry's exported file, edited by <c>edit</c>.</summary>
    private (string Project, string File, Guid Entry, ProjectRecordExporter Exporter, ProjectRecordSchemaResult Schema) Fixture(
        int senses, Action<RecordObject>? edit = null)
    {
        Directory.CreateDirectory(_folder);
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        Guid entryId = default;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            var entry = services.GetInstance<ILexEntryFactory>().Create();
            entryId = entry.Guid;
            var form = services.GetInstance<IMoStemAllomorphFactory>().Create();
            entry.LexemeFormOA = form;
            form.Form.set_String(cache.DefaultVernWs, "zzword");
            for (var index = 0; index < senses; index++)
            {
                var sense = services.GetInstance<ILexSenseFactory>().Create();
                entry.SensesOS.Add(sense);
                sense.Gloss.set_String(cache.DefaultAnalWs, "zzgloss" + index);
            }
        });
        new FwDataProjectLoader().Save(cache);
        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
        var exporter = new ProjectRecordExporter(cache, schema);
        var export = exporter.Export(entryId);
        var read = RecordFileReader.Read(export.Text!, schema.Schema, exporter.CreateReadOptions());
        Assert.False(read.HasErrors);
        if (edit is not null)
            foreach (var item in ((RecordList)read.Document!.Data["senses"]!).Items)
                edit((RecordObject)item);
        var file = Path.Combine(_folder, "entry.md");
        File.WriteAllText(file, RecordFileWriter.Write(read.Document!, schema.Schema));
        return (path, file, entryId, exporter, schema);
    }

    private static void EditGloss(RecordObject sense) =>
        ((RecordAlternatives)sense["gloss"]!).Add("en", new RecordScalar("zzedited"));

    private string Hashes(string project) => string.Join("|", Directory.EnumerateFiles(Path.GetDirectoryName(project)!, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(file => file + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))));

    [Fact]
    public void ADryRunReportsTheOperationsAndWritesNothing()
    {
        var (project, file, _, _, _) = Fixture(1, EditGloss);
        var before = Hashes(project);

        var outcome = RecordImportCommand.Run(new(project, file));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var response = outcome.Value!;
        Assert.Equal("dryRun", response.Mode);
        Assert.Equal("fresh", response.BaseVerdict);
        Assert.Equal(1, response.OperationCount);
        Assert.Equal("lexical/lexSense/setGloss", Assert.Single(response.Operations).Kind);
        Assert.Null(response.DraftName);
        Assert.Null(response.ProposalId);
        // The project folder, including any store beside it, is byte for byte as it was.
        Assert.Equal(before, Hashes(project));
    }

    [Fact]
    public void EditingACustomFieldThroughTheImportCommandReportsTheCustomFieldOperation()
    {
        Directory.CreateDirectory(_folder);
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        Guid entryId = default;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            new FieldDescription(cache)
            {
                Class = cache.MetaDataCacheAccessor.GetClassId("LexEntry"), Name = "ZedRank", Type = CellarPropertyType.Integer,
            }.UpdateCustomField();
            var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
            entryId = entry.Guid;
            var form = cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>().Create();
            entry.LexemeFormOA = form;
            form.Form.set_String(cache.DefaultVernWs, "zzword");
        });
        new FwDataProjectLoader().Save(cache);
        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
        var exporter = new ProjectRecordExporter(cache, schema);
        var read = RecordFileReader.Read(exporter.Export(entryId).Text!, schema.Schema, exporter.CreateReadOptions());
        Assert.False(read.HasErrors);
        read.Document!.Data["custom-LexEntry-ZedRank"] = RecordScalar.Of(9);
        var file = Path.Combine(_folder, "entry.md");
        File.WriteAllText(file, RecordFileWriter.Write(read.Document!, schema.Schema));
        var before = Hashes(path);

        var outcome = RecordImportCommand.Run(new(path, file));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal("customField/setInteger", Assert.Single(outcome.Value!.Operations).Kind);
        Assert.Equal(before, Hashes(path));
    }

    [Fact]
    public void EditingASenseStatusThroughTheImportCommandReportsTheStatusOperation()
    {
        Directory.CreateDirectory(_folder);
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        Guid entryId = default;
        Guid statusId = default;
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
            var status = services.GetInstance<ICmPossibilityFactory>().Create();
            (cache.LangProject.StatusOA ??= services.GetInstance<ICmPossibilityListFactory>().Create()).PossibilitiesOS.Add(status);
            statusId = status.Guid;
        });
        new FwDataProjectLoader().Save(cache);
        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
        var exporter = new ProjectRecordExporter(cache, schema);
        var read = RecordFileReader.Read(exporter.Export(entryId).Text!, schema.Schema, exporter.CreateReadOptions());
        Assert.False(read.HasErrors);
        var sense0 = (RecordObject)((RecordList)read.Document!.Data["senses"]!).Items[0];
        sense0.Set("status", new RecordScalar("[zzstatus](/objects/" + statusId.ToString("D") + "){.name guid=" + statusId.ToString("D") + "}"));
        var file = Path.Combine(_folder, "entry.md");
        File.WriteAllText(file, RecordFileWriter.Write(read.Document!, schema.Schema));
        var before = Hashes(path);

        var outcome = RecordImportCommand.Run(new(path, file));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal("lexical/lexSense/setStatus", Assert.Single(outcome.Value!.Operations).Kind);
        Assert.Equal(before, Hashes(path));
    }

    [Fact]
    public void DraftStagesExactlyOneDraftAndAppliesNothing()
    {
        var (project, file, entry, _, _) = Fixture(1, EditGloss);
        var projectBytes = SHA256.HashData(File.ReadAllBytes(project));

        var outcome = RecordImportCommand.Run(new(project, file, "import-one"));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal("draft", outcome.Value!.Mode);
        Assert.Equal("import-one", outcome.Value.DraftName);
        Assert.Equal(1, outcome.Value.OperationCount);
        var shown = ProposalCommands.Show(new(project, "0.1.0", outcome.Value.ProposalId!));
        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        Assert.Equal("Draft", shown.Value!.Status, ignoreCase: true);
        Assert.Equal(outcome.Value.Operations[0].OperationId, Assert.Single(shown.Value.Operations).OperationId);
        var listed = ProposalCommands.List(new(project, "0.1.0"));
        Assert.True(listed.Succeeded, listed.Refusal?.Message);
        Assert.Single(listed.Value!.Proposals);
        // Nothing reached the project file.
        Assert.Equal(projectBytes, SHA256.HashData(File.ReadAllBytes(project)));
        _ = entry;
    }

    [Fact]
    public void AFileMadeFromAnOlderStateOfTheRecordIsRefusedWholeWithItsCodeAndMakesNoDraft()
    {
        var (project, file, entry, _, _) = Fixture(1, EditGloss);
        using (var cache = new FwDataProjectLoader().LoadScratchCache(project))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(entry).SensesOS[0].Gloss
                    .set_String(cache.DefaultAnalWs, "changedlater"));
            new FwDataProjectLoader().Save(cache);
        }

        foreach (var draft in new string?[] { null, "import-stale" })
        {
            var outcome = RecordImportCommand.Run(new(project, file, draft));
            Assert.False(outcome.Succeeded);
            Assert.Equal(RefusalCodes.RecordImportPlanRefused, outcome.Refusal!.Code);
            Assert.Equal("stale", outcome.Refusal.Facts["baseVerdict"]);
        }
        var listed = ProposalCommands.List(new(project, "0.1.0"));
        Assert.True(!listed.Succeeded || listed.Value!.Proposals.Count == 0);
    }

    [Fact]
    public void AFileWithNoChangesStagesNoDraftAndSaysSo()
    {
        var (project, file, _, _, _) = Fixture(1);

        var dry = RecordImportCommand.Run(new(project, file));
        Assert.True(dry.Succeeded, dry.Refusal?.Message);
        Assert.Equal(0, dry.Value!.OperationCount);

        var staged = RecordImportCommand.Run(new(project, file, "import-empty"));
        Assert.False(staged.Succeeded);
        Assert.Equal(RefusalCodes.RecordImportNothingToStage, staged.Refusal!.Code);
        var listed = ProposalCommands.List(new(project, "0.1.0"));
        Assert.True(!listed.Succeeded || listed.Value!.Proposals.Count == 0);
    }

    [Fact]
    public void AMissingFileAndABadPageAreRefusedByRegisteredCodes()
    {
        var (project, file, _, _, _) = Fixture(1, EditGloss);

        var missing = RecordImportCommand.Run(new(project, Path.Combine(_folder, "absent.md")));
        Assert.Equal(RefusalCodes.RecordImportFileUnreadable, missing.Refusal!.Code);
        Assert.Equal(FailureReason.InvalidArgument, missing.Refusal.Reason);

        var page = RecordImportCommand.Run(new(project, file, null, 0));
        Assert.Equal(RefusalCodes.RecordImportInvalidPage, page.Refusal!.Code);

        foreach (var code in new[]
                 {
                     RefusalCodes.RecordImportFileUnreadable, RefusalCodes.RecordImportInvalidPage,
                     RefusalCodes.RecordImportNothingToStage, RefusalCodes.RecordImportChangeRefused,
                 })
            Assert.Contains(code, RefusalCatalog.Specs.Keys);
    }

    [Fact]
    public void LongListsPageAndEveryPageStaysWithinTwoKilobytes()
    {
        var (project, file, _, _, _) = Fixture(7, EditGloss);
        var seen = new List<string>();

        for (var page = 1; page <= 3; page++)
        {
            var outcome = RecordImportCommand.Run(new(project, file, null, page));
            Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
            Assert.Equal(7, outcome.Value!.OperationCount);
            Assert.Equal(3, outcome.Value.PageCount);
            Assert.True(Encoding.UTF8.GetByteCount(ProjectionJson.Serialize(outcome.Value)) <= RecordImportPaging.SummaryBytes);
            seen.AddRange(outcome.Value.Operations.Select(operation => operation.OperationId));
        }
        Assert.Equal(7, seen.Count);
        Assert.Equal(7, seen.Distinct().Count());
        var past = RecordImportCommand.Run(new(project, file, null, 4));
        Assert.Equal(RefusalCodes.RecordImportInvalidPage, past.Refusal!.Code);
        Assert.Equal("3", past.Refusal.Facts["pageCount"]);
    }

    [Fact]
    public void AListItemFilePlansWithTheListItemSchemaAndAnUnknownRecordIsRefused()
    {
        Directory.CreateDirectory(_folder);
        string project, file;
        Guid item;
        using (var cache = pristine.NewScratch())
        {
            project = cache.ProjectId.Path;
            var possibility = cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.First();
            item = possibility.Guid;
            var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.ListItemOverlay);
            var exporter = new ProjectRecordExporter(cache, schema);
            var read = RecordFileReader.Read(exporter.Export(item).Text!, schema.Schema, exporter.CreateReadOptions());
            Assert.False(read.HasErrors);
            ((RecordAlternatives)read.Document!.Data["name"]!).Add("en", new RecordScalar("zzrenamed"));
            file = Path.Combine(_folder, "item.md");
            File.WriteAllText(file, RecordFileWriter.Write(read.Document!, schema.Schema));
        }

        var outcome = RecordImportCommand.Run(new(project, file));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var response = outcome.Value!;
        Assert.Equal(KindOf(file), response.RecordKind);
        Assert.StartsWith("lists/", response.RecordKind);
        Assert.Equal(0, response.RefusalCount);
        Assert.True(response.OperationCount >= 1);

        var unknown = Path.Combine(_folder, "unknown.md");
        File.WriteAllText(unknown, System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(file), "(?m)^record:.*$", "record: no/such-kind"));
        var refused = RecordImportCommand.Run(new(project, unknown));
        Assert.False(refused.Succeeded);
        Assert.Contains(refused.Refusal!.Code, new[] { RefusalCodes.RecordImportPlanRefused });

        foreach (var value in new[] { "\"lists/cmPossibility x\"", "\"lists/cm\u00e9\"", "\"lexicon/entry \"x" })
        {
            var odd = Path.Combine(_folder, "odd.md");
            File.WriteAllText(odd, System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(file), "(?m)^record:.*$", "record: " + value));
            Assert.Equal(RefusalCodes.RecordImportPlanRefused, RecordImportCommand.Run(new(project, odd)).Refusal?.Code);
        }
        var none = Path.Combine(_folder, "none.md");
        File.WriteAllText(none, System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(file), "(?m)^record:.*\n", ""));
        Assert.Equal(RefusalCodes.RecordImportPlanRefused, RecordImportCommand.Run(new(project, none)).Refusal?.Code);

        static string KindOf(string path) =>
            File.ReadLines(path).First(line => line.StartsWith("record:")).Substring("record:".Length).Trim().Trim('"', '\'');
    }
}
