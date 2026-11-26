using SIL.LCModel;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Records;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Project;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>A record file with no base and a GUID the project lacks is planned as a create, shown in a dry run, and never staged.</summary>
[Collection(LcmCacheParallelCollections.Group2)]
public sealed class RecordCreatePlanCommandTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "motif-create-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }

    internal static void FreshGuids(RecordObject item, Dictionary<Guid, Guid> renamed)
    {
        if (item.Guid is { } old) item.Guid = renamed[old] = Guid.NewGuid();
        foreach (var field in item.Fields)
        {
            if (field.Value is RecordObject child) FreshGuids(child, renamed);
            else if (field.Value is RecordList list)
                foreach (var entry in list.Items.OfType<RecordObject>()) FreshGuids(entry, renamed);
        }
    }

    /// <summary>Writes <paramref name="document"/> with every object GUID renamed, and every link to one renamed with it.</summary>
    internal static string WriteRenamed(RecordDocument document, ProjectRecordSchemaResult schema, Guid? record = null)
    {
        var renamed = new Dictionary<Guid, Guid>();
        var root = document.Data.Guid!.Value;
        FreshGuids(document.Data, renamed);
        if (record is { } made) { renamed[root] = made; document.Data.Guid = made; }
        var text = RecordFileWriter.Write(document, schema.Schema);
        foreach (var pair in renamed) text = text.Replace(pair.Key.ToString("D"), pair.Value.ToString("D"), StringComparison.OrdinalIgnoreCase);
        return text;
    }

    /// <summary>A saved project with one entry, and a file that would create a second entry like it.</summary>
    private (string Project, string File, Guid Guid) Fixture(Guid? guid = null, bool withBase = false)
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
            form.MorphTypeRA = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
            form.Form.set_String(cache.DefaultVernWs, "zzword");
            var pos = services.GetInstance<IPartOfSpeechFactory>().Create();
            cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(pos);
            pos.Name.set_String(cache.DefaultAnalWs, "zznoun");
            var msa = services.GetInstance<IMoStemMsaFactory>().Create();
            entry.MorphoSyntaxAnalysesOC.Add(msa);
            msa.PartOfSpeechRA = pos;
            var sense = services.GetInstance<ILexSenseFactory>().Create();
            entry.SensesOS.Add(sense);
            sense.MorphoSyntaxAnalysisRA = msa;
            sense.Gloss.set_String(cache.DefaultAnalWs, "zzgloss");
        });
        new FwDataProjectLoader().Save(cache);
        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
        var exporter = new ProjectRecordExporter(cache, schema);
        var read = RecordFileReader.Read(exporter.Export(entryId).Text!, schema.Schema, exporter.CreateReadOptions());
        Assert.False(read.HasErrors);
        var made = guid ?? Guid.NewGuid();
        if (!withBase) read.Document!.Base = null;
        read.Document!.Data["head-word"] = null;
        read.Document.Data["homograph-number"] = null;
        var file = Path.Combine(_folder, "create.md");
        File.WriteAllText(file, WriteRenamed(read.Document, schema, made));
        return (cache.ProjectId.Path, file, made);
    }

    private static int DraftCount(string project)
    {
        var listed = ProposalCommands.List(new(project, "0.1.0"));
        return listed.Succeeded ? listed.Value!.Proposals.Count : 0;
    }

    [Fact]
    public void ADryRunShowsACreateWithItsFieldsAndWritesNothing()
    {
        var (project, file, guid) = Fixture();
        var before = Directory.EnumerateFiles(Path.GetDirectoryName(project)!, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(path => path + ":" + new FileInfo(path).Length).ToList();

        var outcome = RecordImportCommand.Run(new(project, file));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var response = outcome.Value!;
        Assert.Equal("dryRun", response.Mode);
        Assert.Equal("new", response.BaseVerdict);
        Assert.Equal("lexicon/entry", response.RecordKind);
        Assert.Equal(CanonicalId.FromGuid(guid).Value, response.RecordId);
        Assert.True(response.ChangeCount > 0);
        Assert.True(response.OperationCount > 0);
        Assert.Equal(0, response.RefusalCount);
        Assert.NotEmpty(response.Changes!);
        Assert.Equal(before, Directory.EnumerateFiles(Path.GetDirectoryName(project)!, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(path => path + ":" + new FileInfo(path).Length).ToList());
    }

    [Fact]
    public void StagingACreatedEntryMakesOneDraft()
    {
        var (project, file, _) = Fixture();

        var outcome = RecordImportCommand.Run(new(project, file, "create-draft"));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(1, DraftCount(project));
    }

    [Fact]
    public void AGuidAnotherOpenDraftAlreadyNamesIsRefusedAtStageAsPending()
    {
        var (project, file, guid) = Fixture();
        Assert.True(ProposalCommands.New(new(project, "0.1.0", "earlier", null)).Succeeded);
        var saved = ProjectStoreCommand.Run(project, MotifProductVersion.CurrentText, (database, _) =>
        {
            new ProposalRepository(database).SaveDraft("earlier", "{\"creates\":\"" + CanonicalId.FromGuid(guid).Value + "\"}");
            return CommandOutcome<string>.Success("saved");
        });
        Assert.True(saved.Succeeded, saved.Refusal?.Message);

        var outcome = RecordImportCommand.Run(new(project, file, "later"));

        Assert.False(outcome.Succeeded);
        Assert.Equal(RefusalCodes.RecordImportGuidPending, outcome.Refusal!.Code);
        Assert.Equal("earlier", outcome.Refusal.Facts["proposal"]);
        // The dry run does not look at pending work: only a stage can collide with it.
        Assert.True(RecordImportCommand.Run(new(project, file)).Succeeded);
    }

    [Fact]
    public void WhenTheOtherDraftsCannotBeReadTheStageIsRefusedAsUncheckedNotSkipped()
    {
        var (project, file, _) = Fixture();
        RecordImportCommand.PendingScanOverride = (_, _) => CommandOutcome<RecordImportCommand.PendingScan>.Refused(
            new Refusal("store.busy", FailureReason.Refused, "busy", new Dictionary<string, string>()));
        try
        {
            var outcome = RecordImportCommand.Run(new(project, file, "later"));

            Assert.False(outcome.Succeeded);
            Assert.Equal(RefusalCodes.RecordImportGuidPendingUnchecked, outcome.Refusal!.Code);
            Assert.Equal("store.busy", outcome.Refusal.Facts["storeRefusal"]);
            Assert.Equal(0, DraftCount(project));
        }
        finally
        {
            RecordImportCommand.PendingScanOverride = null;
        }
    }

    [Fact]
    public void AGuidThatIsNotVersionFourFiveOrSevenIsRefusedWholeWithItsOwnCode()
    {
        var (project, file, _) = Fixture(Guid.Parse("0a1b2c3d-4e5f-1a7b-8c9d-0e1f2a3b4c5d"));

        var outcome = RecordImportCommand.Run(new(project, file));

        Assert.False(outcome.Succeeded);
        Assert.Equal(RefusalCodes.RecordImportGuidInvalid, outcome.Refusal!.Code);
    }

    [Fact]
    public void AFileWhoseBaseCameFromALostRecordIsRecordGoneNotACreate()
    {
        var (project, file, _) = Fixture(withBase: true);
        // The file keeps its base, so it is an edit of a record the project does not have.

        var outcome = RecordImportCommand.Run(new(project, file));

        Assert.False(outcome.Succeeded);
        Assert.Equal(RefusalCodes.RecordImportRecordGone, outcome.Refusal!.Code);
    }

    [Fact]
    public void AListItemCreateIsRefusedWholeAsNotCreatable()
    {
        Directory.CreateDirectory(_folder);
        using var cache = pristine.NewScratch();
        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.ListItemOverlay);
        var document = new RecordDocument("lists/item") { Data = new RecordObject { Guid = Guid.NewGuid(), Class = "CmPossibility" } };
        document.Data["name"] = new RecordAlternatives().Add("en", new RecordScalar("zznew"));
        var file = Path.Combine(_folder, "item.md");
        File.WriteAllText(file, RecordFileWriter.Write(document, schema.Schema));

        var outcome = RecordImportCommand.Run(new(cache.ProjectId.Path, file));

        Assert.False(outcome.Succeeded);
        Assert.Equal(RefusalCodes.RecordImportCreateKindNotCreatable, outcome.Refusal!.Code);
        Assert.Equal("CreateKindNotCreatable", outcome.Refusal.Facts["refusalKind"]);
    }
}
