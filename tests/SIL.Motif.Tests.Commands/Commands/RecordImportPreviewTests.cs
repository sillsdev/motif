using System.Text;
using SIL.LCModel;
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

/// <summary>Tests that the import dry run turns away each doomed change and that staging matches the preview.</summary>
[Collection(LcmCacheParallelCollections.Group2)]
public sealed class RecordImportPreviewTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "motif-import-preview-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        RecordImportCommand.BeforeStage = null;
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }

    /// <summary>A project of <paramref name="senses"/> senses; wrongList sets an illegal status.</summary>
    private (string Project, string File, Guid Entry) Fixture(int senses = 2, bool wrongList = false, string? reference = null)
    {
        Directory.CreateDirectory(_folder);
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        Guid entryId = default, typeItem = default;
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
            var status = cache.LangProject.StatusOA ??= services.GetInstance<ICmPossibilityListFactory>().Create();
            status.PossibilitiesOS.Add(services.GetInstance<ICmPossibilityFactory>().Create());
            var types = cache.LangProject.LexDbOA.SenseTypesOA ??= services.GetInstance<ICmPossibilityListFactory>().Create();
            var item = services.GetInstance<ICmPossibilityFactory>().Create();
            types.PossibilitiesOS.Add(item);
            typeItem = item.Guid;
        });
        new FwDataProjectLoader().Save(cache);
        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
        var exporter = new ProjectRecordExporter(cache, schema);
        var read = RecordFileReader.Read(exporter.Export(entryId).Text!, schema.Schema, exporter.CreateReadOptions());
        Assert.False(read.HasErrors);
        var items = ((RecordList)read.Document!.Data["senses"]!).Items;
        ((RecordAlternatives)((RecordObject)items[0])["gloss"]!).Add("en", new RecordScalar("zzedited"));
        if (wrongList)
            ((RecordObject)items[^1]).Set("status", Link(typeItem));
        if (reference is not null)
            ((RecordObject)items[^1]).Set("status", Link(Guid.Parse(reference)));
        var file = Path.Combine(_folder, "entry.md");
        File.WriteAllText(file, RecordFileWriter.Write(read.Document!, schema.Schema));
        return (path, file, entryId);
    }

    private static RecordScalar Link(Guid id) =>
        new("[zzitem](/objects/" + id.ToString("D") + "){.name guid=" + id.ToString("D") + "}");

    private static int DraftCount(string project)
    {
        var listed = ProposalCommands.List(new(project, "0.1.0"));
        return listed.Succeeded ? listed.Value!.Proposals.Count : 0;
    }

    private static RecordImportRefusal DoomedRefusal(RecordImportResponse response) =>
        Assert.Single(response.Refusals, row => row.Field == "status");

    [Fact]
    public void AValueTheRunnerWouldRefuseIsRefusedInTheDryRunWithItsFieldAndCode()
    {
        var (project, file, _) = Fixture(wrongList: true);

        var outcome = RecordImportCommand.Run(new(project, file));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var refusal = DoomedRefusal(outcome.Value!);
        Assert.Equal(RefusalCodes.RecordImportChangeRefused, refusal.Code);
        Assert.Equal("LexSense", refusal.Class);
        Assert.NotNull(refusal.Object);
        Assert.Equal(1, outcome.Value!.RefusalCount);
        // The gloss edit, which the Runner takes, is still previewed.
        Assert.Equal("lexical/lexSense/setGloss", Assert.Single(outcome.Value.Operations).Kind);
    }

    [Fact]
    public void TheStageHoldsExactlyWhatTheDryRunPredictedAndRecordsTheRefusedCount()
    {
        var (project, file, _) = Fixture(wrongList: true);
        var dry = RecordImportCommand.Run(new(project, file)).Value!;

        var staged = RecordImportCommand.Run(new(project, file, "import-partial"));

        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        Assert.Equal(dry.OperationCount, staged.Value!.OperationCount);
        Assert.Equal(dry.Operations.Select(item => item.OperationId), staged.Value.Operations.Select(item => item.OperationId));
        Assert.Equal(1, staged.Value.RefusalCount);
        var shown = ProposalCommands.Show(new(project, "0.1.0", staged.Value.ProposalId!));
        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        Assert.Equal(dry.OperationCount, shown.Value!.Operations.Count);
        Assert.Equal(1, shown.Value.RecordImportRefused);
    }

    [Fact]
    public void AFullyStagedImportReadsZeroRefusedOnTheProposal()
    {
        var (project, file, _) = Fixture();

        var staged = RecordImportCommand.Run(new(project, file, "import-clean"));

        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        Assert.Equal(0, staged.Value!.RefusalCount);
        Assert.Equal(0, ProposalCommands.Show(new(project, "0.1.0", staged.Value.ProposalId!)).Value!.RecordImportRefused);
    }

    [Fact]
    public void StrictRefusesTheStageWhenAnyChangeIsRefusedAndMakesNoDraft()
    {
        var (project, file, _) = Fixture(wrongList: true);

        var outcome = RecordImportCommand.Run(new(project, file, "import-strict", Strict: true));

        Assert.False(outcome.Succeeded);
        Assert.Equal(RefusalCodes.RecordImportStrictRefused, outcome.Refusal!.Code);
        Assert.Equal("1", outcome.Refusal.Facts["refusals"]);
        Assert.Equal(0, DraftCount(project));
        // Strict does not change what a clean file stages, and the name was never taken.
        var clean = Fixture();
        Assert.True(RecordImportCommand.Run(new(clean.Project, clean.File, "import-strict", Strict: true)).Succeeded);
    }

    [Fact]
    public void AnImporterRefusalHasTheExactChangeRefusedCode()
    {
        var (project, file, _) = Fixture(reference: Guid.NewGuid().ToString("D"));

        var outcome = RecordImportCommand.Run(new(project, file));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(RefusalCodes.RecordImportChangeRefused, DoomedRefusal(outcome.Value!).Code);
        Assert.Equal("record-import.change-refused", DoomedRefusal(outcome.Value!).Code);
    }

    [Fact]
    public void ThePreviewCarriesTheFieldTheOldValueAndTheNewValueOfEachChange()
    {
        var (project, file, _) = Fixture();

        var response = RecordImportCommand.Run(new(project, file)).Value!;

        var change = Assert.Single(response.Changes!);
        Assert.Equal("LexSense", change.Class);
        Assert.Equal("gloss", change.Field);
        Assert.Equal("en", change.WritingSystem);
        Assert.Equal("zzgloss0", change.Old);
        Assert.Equal("zzedited", change.New);
        Assert.NotNull(change.Object);
    }

    [Fact]
    public void ProjectChangedBetweenThePreviewAndTheStageIsRefusedAndLeavesNoDraft()
    {
        var (project, file, entry) = Fixture();
        RecordImportCommand.BeforeStage = () =>
        {
            using var cache = new FwDataProjectLoader().LoadScratchCache(project);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(entry).SensesOS[1].Gloss
                    .set_String(cache.DefaultAnalWs, "changedlater"));
            new FwDataProjectLoader().Save(cache);
        };

        var outcome = RecordImportCommand.Run(new(project, file, "import-raced"));

        Assert.False(outcome.Succeeded);
        Assert.Equal(RefusalCodes.RecordImportChangedSincePreview, outcome.Refusal!.Code);
        Assert.DoesNotContain("draftNotDiscarded", outcome.Refusal.Facts.Keys);
        Assert.Equal(0, DraftCount(project));

        // The name is free, and the file made from the old state is now refused whole.
        RecordImportCommand.BeforeStage = null;
        var again = RecordImportCommand.Run(new(project, file, "import-raced"));
        Assert.Equal(RefusalCodes.RecordImportPlanRefused, again.Refusal!.Code);
        Assert.True(ProposalCommands.New(new(project, "0.1.0", "import-raced", "x")).Succeeded);
    }

    [Fact]
    public void AnEmptyFileIsRefusedAsNamingNoRecordKindInTheDryRunAndTheStage()
    {
        var (project, _, _) = Fixture();
        var empty = Path.Combine(_folder, "empty.md");
        File.WriteAllText(empty, "");

        foreach (var draft in new string?[] { null, "import-empty-file" })
        {
            var outcome = RecordImportCommand.Run(new(project, empty, draft));
            Assert.False(outcome.Succeeded);
            Assert.Equal(RefusalCodes.RecordImportPlanRefused, outcome.Refusal!.Code);
            Assert.Equal("unknown-record-kind", outcome.Refusal.Facts["refusalKind"]);
        }
        Assert.Equal(0, DraftCount(project));
    }

    [Fact]
    public void AFileWithAByteOrderMarkImportsLikeOneWithout()
    {
        var (project, file, _) = Fixture();
        var marked = Path.Combine(_folder, "bom.md");
        File.WriteAllText(marked, File.ReadAllText(file), new UTF8Encoding(true));
        Assert.Equal(0xEF, File.ReadAllBytes(marked)[0]);

        var plain = RecordImportCommand.Run(new(project, file));
        var withMark = RecordImportCommand.Run(new(project, marked));

        Assert.True(withMark.Succeeded, withMark.Refusal?.Message);
        Assert.Equal(plain.Value!.Operations.Select(item => item.OperationId), withMark.Value!.Operations.Select(item => item.OperationId));
    }

    [Fact]
    public void TheSameFileTwiceAndASecondImportAfterStagingGiveTheSameOperations()
    {
        var (project, file, _) = Fixture();

        var first = RecordImportCommand.Run(new(project, file, "import-first"));
        var second = RecordImportCommand.Run(new(project, file, "import-second"));
        var dry = RecordImportCommand.Run(new(project, file));

        Assert.True(first.Succeeded, first.Refusal?.Message);
        Assert.True(second.Succeeded, second.Refusal?.Message);
        Assert.Equal(first.Value!.Operations.Select(item => item.OperationId), second.Value!.Operations.Select(item => item.OperationId));
        Assert.Equal(first.Value.Operations.Select(item => item.OperationId), dry.Value!.Operations.Select(item => item.OperationId));
        Assert.Equal(2, DraftCount(project));
        var taken = RecordImportCommand.Run(new(project, file, "import-first"));
        Assert.Equal("draft.name-collision", taken.Refusal!.Code);
        Assert.Equal(2, DraftCount(project));
    }

    [Fact]
    public void EveryPageStaysWithinTwoKilobytesWhenTheNamesAndValuesAreLongAndNotAscii()
    {
        var (project, file, _) = Fixture(senses: 12);
        var text = File.ReadAllText(file);
        File.WriteAllText(file, text.Replace("zzedited", new string('\u00e9', 400), StringComparison.Ordinal)
            .Replace("zzgloss", new string('\u4e2d', 400), StringComparison.Ordinal));

        for (var page = 1; page <= 3; page++)
        {
            var response = RecordImportCommand.Run(new(project, file, null, page)).Value!;
            Assert.Equal(3, response.PageCount);
            Assert.True(Encoding.UTF8.GetByteCount(ProjectionJson.Serialize(response)) <= RecordImportPaging.SummaryBytes, "page " + page);
        }
    }

    [Fact]
    public void ADoomedChangeIsMarkedRefusedWithItsCodeInTheChangesAndTheOthersAreStaged()
    {
        var (project, file, _) = Fixture(wrongList: true);

        var response = RecordImportCommand.Run(new(project, file)).Value!;

        Assert.Equal(2, response.Changes!.Count);
        var doomed = Assert.Single(response.Changes, change => change.Field == "status");
        Assert.Equal("refused", doomed.Status);
        Assert.Equal(RefusalCodes.RecordImportChangeRefused, doomed.RefusalCode);
        var fine = Assert.Single(response.Changes, change => change.Field == "gloss");
        Assert.Equal("staged", fine.Status);
        Assert.Null(fine.RefusalCode);
    }

    [Fact]
    public void ADryRunLeavesTheProjectFolderByteForByteAsItWasEvenWhenAChangeIsRefused()
    {
        var (project, file, _) = Fixture(wrongList: true);
        string Hashes() => string.Join("|", Directory.EnumerateFiles(Path.GetDirectoryName(project)!, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal).Select(item => item + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(item)))));
        var before = Hashes();

        Assert.True(RecordImportCommand.Run(new(project, file)).Succeeded);

        Assert.Equal(before, Hashes());
    }

    [Fact]
    public void StrictWithoutADraftNameIsRefusedAsAnInvalidArgument()
    {
        var (project, file, _) = Fixture();

        var outcome = RecordImportCommand.Run(new(project, file, Strict: true));

        Assert.Equal(RefusalCodes.RecordImportStrictNeedsDraft, outcome.Refusal!.Code);
        Assert.Equal(FailureReason.InvalidArgument, outcome.Refusal.Reason);
    }
}
