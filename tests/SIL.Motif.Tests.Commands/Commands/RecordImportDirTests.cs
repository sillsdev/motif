using System.Text.RegularExpressions;
using SIL.LCModel;
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

/// <summary>A folder of record files is planned, staged as one Proposal and read back, with each file's own row.</summary>
[Collection(LcmCacheParallelCollections.Group2)]
public sealed class RecordImportDirTests(PristineProjectFixture pristine) : IDisposable
{
    private const string Version = "0.1.0";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-import-dir-" + Guid.NewGuid().ToString("N"));
    private string Folder => Path.Combine(_root, "records");

    public void Dispose()
    {
        RecordImportCommand.FileReaderOverride = null;
        RecordImportCommand.ScratchProbe = null;
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private sealed record Seed(string Project, Guid[] Entries);

    private Seed Seeded(int entries)
    {
        Directory.CreateDirectory(Folder);
        using var cache = pristine.NewScratch();
        var ids = new List<Guid>();
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            var pos = services.GetInstance<IPartOfSpeechFactory>().Create();
            cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(pos);
            pos.Name.set_String(cache.DefaultAnalWs, "zznoun");
            for (var index = 0; index < entries; index++)
            {
                var entry = services.GetInstance<ILexEntryFactory>().Create();
                ids.Add(entry.Guid);
                var form = services.GetInstance<IMoStemAllomorphFactory>().Create();
                entry.LexemeFormOA = form;
                form.MorphTypeRA = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
                form.Form.set_String(cache.DefaultVernWs, "zzword" + index);
                var msa = services.GetInstance<IMoStemMsaFactory>().Create();
                entry.MorphoSyntaxAnalysesOC.Add(msa);
                msa.PartOfSpeechRA = pos;
                var sense = services.GetInstance<ILexSenseFactory>().Create();
                entry.SensesOS.Add(sense);
                sense.MorphoSyntaxAnalysisRA = msa;
                sense.Gloss.set_String(cache.DefaultAnalWs, "zzgloss" + index);
            }
        });
        new FwDataProjectLoader().Save(cache);
        return new(cache.ProjectId.Path, ids.ToArray());
    }

    private static (ProjectRecordSchemaResult Schema, ProjectRecordExporter Exporter) Tools(LcmCache cache)
    {
        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
        return (schema, new ProjectRecordExporter(cache, schema));
    }

    /// <summary>An entry's export as a file, edited, or as a create when asNew.</summary>
    private string WriteFile(Seed seed, Guid entry, string name, Action<RecordDocument>? edit = null, bool asNew = false)
    {
        using var cache = new FwDataProjectLoader().LoadCache(seed.Project);
        var (schema, exporter) = Tools(cache);
        var read = RecordFileReader.Read(exporter.Export(entry).Text!, schema.Schema, exporter.CreateReadOptions());
        Assert.False(read.HasErrors);
        var document = read.Document!;
        if (asNew)
        {
            document.Base = null;
            document.Data["head-word"] = null;
            document.Data["homograph-number"] = null;
        }
        edit?.Invoke(document);
        var path = Path.Combine(Folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, asNew ? RecordCreatePlanCommandTests.WriteRenamed(document, schema) : RecordFileWriter.Write(document, schema.Schema));
        return path;
    }

    private static void Regloss(RecordDocument document)
    {
        var sense = (RecordObject)((RecordList)document.Data["senses"]!).Items[0];
        ((RecordAlternatives)sense["gloss"]!).Add("en", new RecordScalar("zzedited"));
    }

    private RecordImportRequest Dry(Seed seed, string? draft = null, bool strict = false, int page = 1, bool? rows = null,
        bool? refusedOnly = null, string? report = null) =>
        new(seed.Project, null, draft, page, strict, DirPathOf(seed), rows, refusedOnly, report);

    private string DirPathOf(Seed seed) => Folder;

    private static Dictionary<string, RecordImportFileRow> AllRows(Seed seed, RecordImportRequest request)
    {
        var outcome = RecordImportCommand.Run(request with { Rows = true, Page = 1 });
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        return outcome.Value!.Files!.ToDictionary(row => row.Path);
    }

    /// <summary>Creates, updates, stale, unchanged, unreadable and non-record files, plus ignored ones.</summary>
    private Seed MixedFolder()
    {
        var seed = Seeded(6);
        for (var index = 0; index < 3; index++) WriteFile(seed, seed.Entries[0], $"create{index}.fw.md", asNew: true);
        WriteFile(seed, seed.Entries[1], "sub/update1.fw.md", Regloss);
        WriteFile(seed, seed.Entries[2], "update2.fw.md", Regloss);
        WriteFile(seed, seed.Entries[3], "stale.fw.md", document => { Regloss(document); document.Base = "0000"; });
        WriteFile(seed, seed.Entries[4], "same.fw.md");
        WriteFile(seed, seed.Entries[5], "unreadable.fw.md");
        File.WriteAllText(Path.Combine(Folder, "notarecord.fw.md"), "just text\n");
        File.WriteAllText(Path.Combine(Folder, "readme.txt"), "ignored");
        File.WriteAllText(Path.Combine(Folder, ".hidden.fw.md"), "hidden");
        RecordImportCommand.FileReaderOverride = path =>
            Path.GetFileName(path) == "unreadable.fw.md" ? throw new IOException("locked") : File.ReadAllText(path);
        return seed;
    }

    [Fact]
    public void AFolderOfMixedFilesGivesOneRowEachAndStagesNothingInADryRun()
    {
        var seed = MixedFolder();

        var outcome = RecordImportCommand.Run(Dry(seed));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var dir = outcome.Value!.Dir!;
        Assert.Equal("dryRun", outcome.Value.Mode);
        Assert.Equal((9, 3, 2, 1, 3, 1), (dir.Files, dir.Created, dir.Updated, dir.Unchanged, dir.Refused, dir.Ignored));
        Assert.Null(outcome.Value.Files);
        Assert.True(ProjectionJson.ToUtf8Bytes(outcome.Value).Length <= RecordImportPaging.SummaryBytes);
        var rows = AllRows(seed, Dry(seed));
        Assert.Equal(["create0.fw.md", "create1.fw.md", "create2.fw.md", "notarecord.fw.md", "same.fw.md", "stale.fw.md", "sub/update1.fw.md",
            "unreadable.fw.md", "update2.fw.md"], rows.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(("created", "new"), (rows["create0.fw.md"].Status, rows["create0.fw.md"].BaseVerdict));
        Assert.Equal(("updated", "fresh"), (rows["sub/update1.fw.md"].Status, rows["sub/update1.fw.md"].BaseVerdict));
        Assert.Equal("unchanged", rows["same.fw.md"].Status);
        Assert.Equal(("refused", "stale", RefusalCodes.RecordImportPlanRefused), (rows["stale.fw.md"].Status, rows["stale.fw.md"].BaseVerdict, rows["stale.fw.md"].Code));
        Assert.Equal(RefusalCodes.RecordImportFileUnreadable, rows["unreadable.fw.md"].Code);
        Assert.Equal(RefusalCodes.RecordImportPlanRefused, rows["notarecord.fw.md"].Code);
        Assert.True(rows["create1.fw.md"].Operations > 0 && rows["update2.fw.md"].Operations > 0);
        Assert.Equal(0, ProposalCommands.List(new(seed.Project, Version)).Value?.Proposals.Count ?? 0);
    }

    [Fact]
    public void ARefusedFileDoesNotBlockTheRestAndTheStagedDraftHoldsTheOthers()
    {
        var seed = MixedFolder();
        var dry = RecordImportCommand.Run(Dry(seed)).Value!;

        var staged = RecordImportCommand.Run(Dry(seed, "dir-1"));

        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        Assert.Equal("draft", staged.Value!.Mode);
        Assert.Equal(dry.OperationCount, staged.Value.OperationCount);
        Assert.Equal(dry.Dir!.Manifest, staged.Value.Dir!.Manifest);
        var shown = ProposalCommands.Show(new(seed.Project, Version, "dir-1"));
        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        Assert.Single(ProposalCommands.List(new(seed.Project, Version)).Value!.Proposals);
        Assert.Equal(staged.Value.OperationCount, shown.Value!.Operations.Count);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("unreadable")]
    [InlineData("notarecord")]
    [InlineData("change")]
    public void StrictRefusesTheWholeFolderForEachKindOfRefusal(string kind)
    {
        var seed = Seeded(3);
        WriteFile(seed, seed.Entries[0], "good.fw.md", Regloss);
        switch (kind)
        {
            case "stale": WriteFile(seed, seed.Entries[1], "bad.fw.md", document => { Regloss(document); document.Base = "0000"; }); break;
            case "unreadable":
                WriteFile(seed, seed.Entries[1], "bad.fw.md");
                RecordImportCommand.FileReaderOverride = path => Path.GetFileName(path) == "bad.fw.md" ? throw new IOException("x") : File.ReadAllText(path);
                break;
            case "notarecord": File.WriteAllText(Path.Combine(Folder, "bad.fw.md"), "nothing\n"); break;
            default:
                WriteFile(seed, seed.Entries[1], "bad.fw.md", document =>
                {
                    var sense = (RecordObject)((RecordList)document.Data["senses"]!).Items[0];
                    sense["gloss"] = new RecordAlternatives().Add("zz-nope", new RecordScalar("x"));
                    Regloss(document);
                });
                break;
        }
        var lenient = RecordImportCommand.Run(Dry(seed, "lenient"));
        Assert.True(lenient.Succeeded, lenient.Refusal?.Message);
        Assert.True(lenient.Value!.RefusalCount > 0, kind);

        var strict = RecordImportCommand.Run(Dry(seed, "strict", strict: true));

        Assert.False(strict.Succeeded, kind);
        Assert.Equal(RefusalCodes.RecordImportStrictRefused, strict.Refusal!.Code);
        Assert.Single(ProposalCommands.List(new(seed.Project, Version)).Value!.Proposals);
    }

    private static Action<RecordDocument> Gloss(string text) => document =>
    {
        var sense = (RecordObject)((RecordList)document.Data["senses"]!).Items[0];
        ((RecordAlternatives)sense["gloss"]!).Add("en", new RecordScalar(text));
    };

    [Fact]
    public void AFileWhoseOperationFailsUnexpectedlyIsRefusedAndTheRestOfTheFolderStages()
    {
        var seed = Seeded(3);
        WriteFile(seed, seed.Entries[0], "a.fw.md", Gloss("zzfirst"));
        WriteFile(seed, seed.Entries[1], "b.fw.md", Gloss("zzboom"));
        WriteFile(seed, seed.Entries[2], "c.fw.md", Gloss("zzthird"));
        RecordImportCommand.ScratchProbe = operation =>
        {
            if (operation.After?.GetRawText().Contains("zzboom", StringComparison.Ordinal) == true) throw new NotSupportedException("boom");
        };

        var rows = AllRows(seed, Dry(seed));

        Assert.Equal(("refused", RefusalCodes.RecordImportPlanRefused), (rows["b.fw.md"].Status, rows["b.fw.md"].Code));
        Assert.Equal("updated", rows["a.fw.md"].Status);
        Assert.Equal("updated", rows["c.fw.md"].Status);
        var staged = RecordImportCommand.Run(Dry(seed, "isolated"));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        Assert.Equal((2, 1), (staged.Value!.Dir!.Updated, staged.Value.Dir.Refused));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("revision")]
    [InlineData("licence")]
    public void AnOptionOverTwoHundredCharactersOrWithControlCharactersIsRefused(string option)
    {
        var seed = Seeded(1);
        WriteFile(seed, seed.Entries[0], "a.fw.md", Regloss);
        foreach (var value in new[] { new string('x', RecordImportPaging.MaxOptionLength + 1), "a\u001b[31mb" })
        {
            var request = Dry(seed) with
            {
                Source = option == "source" ? value : null,
                Revision = option == "revision" ? value : null,
                Licence = option == "licence" ? value : null,
            };
            Assert.Equal(RefusalCodes.RecordImportOptionInvalid, RecordImportCommand.Run(request).Refusal!.Code);
        }
    }

    [Fact]
    public void StrictDoesNotCountUnchangedFilesAsRefusals()
    {
        var seed = Seeded(2);
        WriteFile(seed, seed.Entries[0], "good.fw.md", Regloss);
        WriteFile(seed, seed.Entries[1], "same.fw.md");

        var strict = RecordImportCommand.Run(Dry(seed, "strict-ok", strict: true));

        Assert.True(strict.Succeeded, strict.Refusal?.Message);
        Assert.Equal(1, strict.Value!.Dir!.Unchanged);
    }

    [Fact]
    public void ImportingTheFolderAgainAfterApplyingItReadsCreatesAsUnchangedAndTheUpdateAsStale()
    {
        var seed = Seeded(3);
        for (var index = 0; index < 2; index++) WriteFile(seed, seed.Entries[0], $"create{index}.fw.md", asNew: true);
        WriteFile(seed, seed.Entries[1], "update.fw.md", Regloss);
        var staged = RecordImportCommand.Run(Dry(seed, "apply-me"));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        Assert.Equal((2, 1), (staged.Value!.Dir!.Created, staged.Value.Dir.Updated));
        DraftRationale.Author(seed.Project, "apply-me", "Import a folder", "The folder holds two new entries and one edit.");
        var finalized = ProposalCommands.Finalize(new(seed.Project, Version, "apply-me"));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var dryRun = DryRunJobRunner.Run(seed.Project, Version, finalized.Value!.ProposalId);
        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);
        var applied = ProposalCommands.Apply(new(seed.Project, Version, finalized.Value.ProposalId, Force: true));
        Assert.True(applied.Succeeded, applied.Refusal?.Message);

        var again = RecordImportCommand.Run(Dry(seed));

        Assert.True(again.Succeeded, again.Refusal?.Message);
        Assert.Equal((3, 0, 0, 2, 1), (again.Value!.Dir!.Files, again.Value.Dir.Created, again.Value.Dir.Updated, again.Value.Dir.Unchanged, again.Value.Dir.Refused));
        Assert.Equal("stale", AllRows(seed, Dry(seed))["update.fw.md"].BaseVerdict);
    }

    [Fact]
    public void TheSameFolderUnderASecondDraftNameRefusesEveryCreateAsGuidPending()
    {
        var seed = Seeded(1);
        for (var index = 0; index < 2; index++) WriteFile(seed, seed.Entries[0], $"create{index}.fw.md", asNew: true);
        Assert.True(RecordImportCommand.Run(Dry(seed, "first")).Succeeded);

        var second = RecordImportCommand.Run(Dry(seed, "second"));

        Assert.False(second.Succeeded);
        Assert.Equal(RefusalCodes.RecordImportGuidPending, second.Refusal!.Code);
        Assert.Single(ProposalCommands.List(new(seed.Project, Version)).Value!.Proposals);
        WriteFile(seed, seed.Entries[0], "create2.fw.md", asNew: true);
        var mixed = RecordImportCommand.Run(Dry(seed, "third"));
        Assert.True(mixed.Succeeded, mixed.Refusal?.Message);
        Assert.Equal((1, 2), (mixed.Value!.Dir!.Created, mixed.Value.Dir.Refused));
        Assert.All(mixed.Value.Dir.FirstRefused, row => Assert.Equal(RefusalCodes.RecordImportGuidPending, row.Code));
    }

    [Fact]
    public void TwoFilesAboutOneRecordAreBothRefusedAsRecordTwice()
    {
        var seed = Seeded(2);
        WriteFile(seed, seed.Entries[0], "a.fw.md", Regloss);
        WriteFile(seed, seed.Entries[0], "b.fw.md", Regloss);
        WriteFile(seed, seed.Entries[1], "c.fw.md", Regloss);

        var rows = AllRows(seed, Dry(seed));

        Assert.Equal(RefusalCodes.RecordImportRecordTwice, rows["a.fw.md"].Code);
        Assert.Equal(RefusalCodes.RecordImportRecordTwice, rows["b.fw.md"].Code);
        Assert.Equal("updated", rows["c.fw.md"].Status);
    }

    [Fact]
    public void ANewObjectGuidUsedInTwoFilesRefusesBothAsDuplicateInBatch()
    {
        var seed = Seeded(1);
        var first = WriteFile(seed, seed.Entries[0], "a.fw.md", asNew: true);
        var text = File.ReadAllText(first);
        var root = new Regex("guid: \"?([0-9a-f-]{36})").Match(text).Groups[1].Value;
        File.WriteAllText(Path.Combine(Folder, "b.fw.md"), text.Replace(root, Guid.NewGuid().ToString("D"), StringComparison.OrdinalIgnoreCase));
        WriteFile(seed, seed.Entries[0], "c.fw.md", asNew: true);

        var rows = AllRows(seed, Dry(seed));

        Assert.Equal(RefusalCodes.RecordImportGuidDuplicateInBatch, rows["a.fw.md"].Code);
        Assert.Equal(RefusalCodes.RecordImportGuidDuplicateInBatch, rows["b.fw.md"].Code);
        Assert.Equal("created", rows["c.fw.md"].Status);
    }

    [Fact]
    public void AFileThatRefersToAnotherFilesNewRecordIsRefusedWithAClearCode()
    {
        var seed = Seeded(2);
        var main = WriteFile(seed, seed.Entries[0], "main.fw.md", asNew: true);
        var mainText = File.ReadAllText(main);
        var newMsa = new Regex("guid: \"([0-9a-f-]{36})\"\\s+class: \"MoStemMsa\"").Match(mainText).Groups[1].Value;
        // The second file's sense points at an analysis that only the first file creates.
        var other = WriteFile(seed, seed.Entries[1], "other.fw.md", asNew: true);
        var otherText = File.ReadAllText(other);
        var own = new Regex("morpho-syntax-analysis: \"\\[zznoun\\]\\(/objects/([0-9a-f-]{36})\\)").Match(otherText).Groups[1].Value;
        var lines = otherText.Split('\n').Select(line => line.Contains("morpho-syntax-analysis: \"[") ? line.Replace(own, newMsa, StringComparison.OrdinalIgnoreCase) : line);
        File.WriteAllText(other, string.Join('\n', lines));

        var rows = AllRows(seed, Dry(seed));

        Assert.Equal("created", rows["main.fw.md"].Status);
        Assert.Equal(("refused", RefusalCodes.RecordImportReferenceToNewInBatch), (rows["other.fw.md"].Status, rows["other.fw.md"].Code));
    }

    [Fact]
    public void RowPagesAreBoundedAndARefusedOnlyPageListsJustTheRefusedRows()
    {
        var seed = MixedFolder();

        var beyond = RecordImportCommand.Run(Dry(seed, page: 2, rows: true));
        var refused = RecordImportCommand.Run(Dry(seed, page: 1, rows: true, refusedOnly: true));
        var summary = RecordImportCommand.Run(Dry(seed));

        Assert.Equal(RefusalCodes.RecordImportInvalidPage, beyond.Refusal!.Code);
        Assert.Equal(3, refused.Value!.Files!.Count);
        Assert.All(refused.Value.Files, row => Assert.Equal("refused", row.Status));
        Assert.Equal(1, summary.Value!.PageCount);
        Assert.True(summary.Value.Dir!.FirstRefused.Count <= RecordImportPaging.SummaryRefusedRows);
    }

    [Fact]
    public void TheSummaryStaysWithinTwoKilobytesWhateverManyLongNamedFilesAreRefused()
    {
        var seed = Seeded(1);
        for (var index = 0; index < 12; index++)
            File.WriteAllText(Path.Combine(Folder, new string('n', 80) + index + ".fw.md"), "text of no record\n");

        var outcome = RecordImportCommand.Run(Dry(seed));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(12, outcome.Value!.Dir!.Refused);
        Assert.True(ProjectionJson.ToUtf8Bytes(outcome.Value).Length <= RecordImportPaging.SummaryBytes);
        Assert.True(outcome.Value.Dir.FirstRefused.Count > 0);
    }

    [Fact]
    public void TheReportHoldsEveryRowAsJsonLinesAndIsNotReplaced()
    {
        var seed = MixedFolder();
        var report = Path.Combine(_root, "report.jsonl");

        var outcome = RecordImportCommand.Run(Dry(seed, report: report));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var lines = File.ReadAllLines(report);
        Assert.Equal(9, lines.Length);
        Assert.All(lines, line => Assert.NotNull(ProjectionJson.Deserialize<RecordImportFileRow>(line)));
        Assert.Equal(RefusalCodes.RecordExportOutputUnwritable, RecordImportCommand.Run(Dry(seed, report: report)).Refusal!.Code);
    }

    [Fact]
    public void TheProposalKeepsTheOptionsAndTheTextOfEveryStagedFile()
    {
        var seed = Seeded(1);
        WriteFile(seed, seed.Entries[0], "a.fw.md", Regloss);

        var staged = RecordImportCommand.Run(Dry(seed, "kept") with { Source = "synthetic", Revision = "r1", Licence = "CC0" });

        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        Assert.Equal(("synthetic", "r1", "CC0"), (staged.Value!.Dir!.Source, staged.Value.Dir.Revision, staged.Value.Dir.Licence));
        var json = ProposalCommands.Show(new(seed.Project, Version, "kept")).Value!;
        var text = System.Text.Json.JsonSerializer.Serialize(json);
        Assert.Contains("zzedited", text, StringComparison.Ordinal);
        Assert.Contains("synthetic", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileAndAFolderTogetherOrNeitherAreRefusedAndEmptyAndMissingFoldersHaveTheirOwnCodes()
    {
        var seed = Seeded(1);
        Assert.Equal(RefusalCodes.RecordImportFileOrDir,
            RecordImportCommand.Run(new(seed.Project, "a.md", DirPath: Folder)).Refusal!.Code);
        Assert.Equal(RefusalCodes.RecordImportFileOrDir, RecordImportCommand.Run(new(seed.Project, null)).Refusal!.Code);
        Assert.Equal(RefusalCodes.RecordImportDirEmpty, RecordImportCommand.Run(Dry(seed)).Refusal!.Code);
        File.WriteAllText(Path.Combine(Folder, "x.txt"), "x");
        Assert.Equal(RefusalCodes.RecordImportDirEmpty, RecordImportCommand.Run(Dry(seed)).Refusal!.Code);
        Assert.Equal(RefusalCodes.RecordImportDirUnreadable,
            RecordImportCommand.Run(new(seed.Project, null, DirPath: Path.Combine(_root, "missing"))).Refusal!.Code);
    }

    [Fact]
    public void AFolderOverTheFileCapIsRefusedBeforeAnythingIsRead()
    {
        Directory.CreateDirectory(Folder);
        for (var index = 0; index <= RecordImportPaging.MaxFiles; index++) File.WriteAllText(Path.Combine(Folder, index + ".fw.md"), "");

        var outcome = RecordImportCommand.Run(new("missing.fwdata", null, DirPath: Folder));

        Assert.Equal(RefusalCodes.RecordImportBatchTooLarge, outcome.Refusal!.Code);
        Assert.Equal((RecordImportPaging.MaxFiles + 1).ToString(), outcome.Refusal.Facts["files"]);
    }

    [Fact]
    public void LinksInsideTheFolderAreNotFollowedAndAreCountedAsIgnored()
    {
        var seed = Seeded(1);
        var real = WriteFile(seed, seed.Entries[0], "real.fw.md", Regloss);
        var outside = Path.Combine(_root, "outside.fw.md");
        File.Copy(real, outside);
        try { File.CreateSymbolicLink(Path.Combine(Folder, "link.fw.md"), outside); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { return; }

        var outcome = RecordImportCommand.Run(Dry(seed));

        Assert.Equal((1, 1), (outcome.Value!.Dir!.Files, outcome.Value.Dir.Ignored));
    }
}
