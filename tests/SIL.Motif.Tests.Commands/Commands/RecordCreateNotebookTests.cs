using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Records;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.LiveHost.HumanJudgments;
using SIL.Motif.Tests.TestFixtures;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Project;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>A record file for a new notebook record is staged, applied by a person, and the reopened project exports the same record.</summary>
[Collection(LcmCacheParallelCollections.Group2)]
public sealed class RecordCreateNotebookTests(PristineProjectFixture pristine) : IDisposable
{
    private const string Version = "0.1.0";
    private const string DraftName = "create-record";
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "motif-create-record-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }

    private sealed record Seed(string Project, Guid Record, Guid Parent, Guid OtherType);

    private Seed Seeded(bool withJudgmentField = false, bool withoutObservation = false)
    {
        Directory.CreateDirectory(_folder);
        using var cache = pristine.NewScratch();
        Guid record = default, parent = default, other = default;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            var notebook = cache.LangProject.ResearchNotebookOA ?? services.GetInstance<IRnResearchNbkFactory>().Create();
            cache.LangProject.ResearchNotebookOA = notebook;
            var types = notebook.RecTypesOA ?? services.GetInstance<ICmPossibilityListFactory>().Create();
            notebook.RecTypesOA = types;
            if (!services.ObjectRepository.TryGetObject(RnResearchNbkTags.kguidRecObservation, out _))
            {
                var observation = services.GetInstance<ICmPossibilityFactory>().Create(RnResearchNbkTags.kguidRecObservation, types);
                observation.Name.set_String(cache.DefaultAnalWs, "zzobservation");
            }
            var kind = services.GetInstance<ICmPossibilityFactory>().Create();
            types.PossibilitiesOS.Add(kind);
            kind.Name.set_String(cache.DefaultAnalWs, "zzinterview");
            other = kind.Guid;
            var made = services.GetInstance<IRnGenericRecFactory>().Create();
            notebook.RecordsOC.Add(made);
            made.TypeRA = kind;
            made.Title = TsStringUtils.MakeString("zztitle", cache.DefaultAnalWs);
            made.DescriptionOA = services.GetInstance<IStTextFactory>().Create();
            var paragraph = services.GetInstance<IStTxtParaFactory>().Create();
            made.DescriptionOA.ParagraphsOS.Add(paragraph);
            paragraph.Contents = TsStringUtils.MakeString("zzbody", cache.DefaultAnalWs);
            record = made.Guid;
            var holder = services.GetInstance<IRnGenericRecFactory>().Create();
            notebook.RecordsOC.Add(holder);
            holder.Title = TsStringUtils.MakeString("zzparent", cache.DefaultAnalWs);
            parent = holder.Guid;
        });
        if (withoutObservation && cache.ServiceLocator.ObjectRepository.TryGetObject(RnResearchNbkTags.kguidRecObservation, out var observation))
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => cache.DomainDataByFlid.DeleteObj(observation.Hvo));
        if (withJudgmentField) HumanJudgmentFieldDefinition.CreateDefinition(cache);
        new FwDataProjectLoader().Save(cache);
        return new(cache.ProjectId.Path, record, parent, other);
    }

    private static (ProjectRecordSchemaResult Schema, RecordDocument Document) Exported(LcmCache cache, Guid record)
    {
        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.NotebookRecordOverlay);
        var exporter = new ProjectRecordExporter(cache, schema);
        var read = RecordFileReader.Read(exporter.Export(record).Text!, schema.Schema, exporter.CreateReadOptions());
        Assert.False(read.HasErrors);
        return (schema, read.Document!);
    }

    private string NewFile(Seed seed, Guid template, Action<RecordDocument, ProjectRecordSchemaResult>? edit = null, string name = "new.md")
    {
        using var cache = new FwDataProjectLoader().LoadCache(seed.Project);
        var (schema, document) = Exported(cache, template);
        document.Base = null;
        edit?.Invoke(document, schema);
        var file = Path.Combine(_folder, name);
        File.WriteAllText(file, RecordCreatePlanCommandTests.WriteRenamed(document, schema));
        return file;
    }

    private static string Normalized(string text, ProjectRecordSchemaResult schema, ProjectRecordExporter exporter)
    {
        var read = RecordFileReader.Read(text, schema.Schema, exporter.CreateReadOptions());
        Assert.False(read.HasErrors);
        read.Document!.Base = null;
        return RecordFileWriter.Write(read.Document, schema.Schema);
    }

    private static string StageFinalizeApply(string project, string file)
    {
        var staged = RecordImportCommand.Run(new(project, file, DraftName));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        DraftRationale.Author(project, DraftName, "Create a record", "The record file describes a new notebook record.");
        var finalized = ProposalCommands.Finalize(new(project, Version, DraftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var dryRun = DryRunJobRunner.Run(project, Version, finalized.Value!.ProposalId);
        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);
        var applied = ProposalCommands.Apply(new(project, Version, finalized.Value.ProposalId, Force: true));
        Assert.True(applied.Succeeded, applied.Refusal?.Message);
        return finalized.Value.ProposalId;
    }

    private static Guid RecordOf(string file, ProjectRecordSchemaResult schema) =>
        RecordFileReader.Read(File.ReadAllText(file), schema.Schema, default).Document!.Data.Guid!.Value;

    private static int Drafts(string project) => ProposalCommands.List(new(project, Version)).Value?.Proposals.Count ?? 0;

    [Fact]
    public void ARecordIsStagedAppliedAndReadsBackAsTheFile()
    {
        var seed = Seeded();
        var file = NewFile(seed, seed.Record);
        Guid created;
        using (var cache = new FwDataProjectLoader().LoadCache(seed.Project))
            created = RecordOf(file, Exported(cache, seed.Record).Schema);
        Assert.NotEqual(seed.Record, created);

        StageFinalizeApply(seed.Project, file);

        using var reopened = new FwDataProjectLoader().LoadCache(seed.Project);
        var schema = ProjectRecordSchema.Generate(reopened, ProjectRecordSchema.NotebookRecordOverlay);
        var exporter = new ProjectRecordExporter(reopened, schema);
        Assert.Equal(Normalized(File.ReadAllText(file), schema, exporter), Normalized(exporter.Export(created).Text!, schema, exporter));
        var made = reopened.ServiceLocator.GetInstance<IRnGenericRecRepository>().GetObject(created);
        Assert.Equal(seed.OtherType, made.TypeRA!.Guid);
    }

    [Fact]
    public void AFileWithNoTypeGetsObservationAsTheDialogDoes()
    {
        var seed = Seeded();
        var file = NewFile(seed, seed.Record, (document, _) => document.Data["type"] = null);
        Assert.DoesNotContain("zzinterview", File.ReadAllText(file), StringComparison.Ordinal);
        Guid created;
        using (var cache = new FwDataProjectLoader().LoadCache(seed.Project))
            created = RecordOf(file, Exported(cache, seed.Record).Schema);

        StageFinalizeApply(seed.Project, file);

        using var reopened = new FwDataProjectLoader().LoadCache(seed.Project);
        var made = reopened.ServiceLocator.GetInstance<IRnGenericRecRepository>().GetObject(created);
        Assert.Equal(RnResearchNbkTags.kguidRecObservation, made.TypeRA!.Guid);
        Assert.Equal("zztitle", made.Title.Text);
        Assert.Contains(made, reopened.LangProject.ResearchNotebookOA.RecordsOC);
    }

    [Fact]
    public void AFileWithNoTitleIsRefusedWholeAndMakesNoDraft()
    {
        var seed = Seeded();
        var file = NewFile(seed, seed.Record, (document, _) => document.Data["title"] = null);

        var outcome = RecordImportCommand.Run(new(seed.Project, file, DraftName));

        Assert.False(outcome.Succeeded);
        Assert.Equal(RefusalCodes.RecordImportCreateShape, outcome.Refusal!.Code);
        Assert.Contains("FieldWorks", outcome.Refusal.Message, StringComparison.Ordinal);
        Assert.Equal(0, Drafts(seed.Project));
        Assert.Equal(RefusalCodes.RecordImportCreateShape, RecordImportCommand.Run(new(seed.Project, file)).Refusal!.Code);
    }

    [Theory]
    [InlineData("MotifJudgmentId")]
    [InlineData("MotifJudgmentData")]
    public void AReservedJudgmentFieldIsRefusedWholeAsReserved(string reserved)
    {
        var seed = Seeded(withJudgmentField: true);
        var file = NewFile(seed, seed.Record, (document, schema) =>
        {
            var key = schema.Schema.ObjectType("RnGenericRec")!.Fields.Select(field => field.Name).First(name =>
                schema.FieldBinding("RnGenericRec", name) is { IsCustom: true } binding && binding.Field == reserved);
            document.Data[key] = new RecordScalar("zzreserved");
        });

        var outcome = RecordImportCommand.Run(new(seed.Project, file, DraftName));

        Assert.False(outcome.Succeeded);
        Assert.Equal(RefusalCodes.RecordImportCreateReservedField, outcome.Refusal!.Code);
        Assert.Equal(0, Drafts(seed.Project));
    }

    [Fact]
    public void ASubRecordAddedInItsParentsFileIsAnUpdateNotACreate()
    {
        var seed = Seeded();
        string file;
        using (var cache = new FwDataProjectLoader().LoadCache(seed.Project))
        {
            var (schema, document) = Exported(cache, seed.Parent);
            var child = new RecordObject { Guid = Guid.NewGuid() };
            child["title"] = new RecordScalar("zzchild");
            document.Data["sub-records"] = new RecordList().Add(child);
            file = Path.Combine(_folder, "edit.md");
            File.WriteAllText(file, RecordFileWriter.Write(document, schema.Schema));
        }

        var dry = RecordImportCommand.Run(new(seed.Project, file));

        Assert.True(dry.Succeeded, dry.Refusal?.Message);
        Assert.NotEqual("new", dry.Value!.BaseVerdict);
        Assert.Equal(CanonicalIdOf(seed.Parent), dry.Value.RecordId);
        Assert.Contains(dry.Value.Refusals ?? [], row => row.Field == "sub-records");
        Assert.DoesNotContain(dry.Value.Operations ?? [], operation => operation.Kind.EndsWith("createRecords", StringComparison.Ordinal));
    }

    [Fact]
    public void AFileWithNoTypeIsRefusedWholeWhenTheProjectHasNoObservationType()
    {
        var seed = Seeded(withoutObservation: true);
        var file = NewFile(seed, seed.Record, (document, _) => document.Data["type"] = null);

        var outcome = RecordImportCommand.Run(new(seed.Project, file, DraftName));

        Assert.False(outcome.Succeeded);
        Assert.Equal(RefusalCodes.RecordImportCreateShape, outcome.Refusal!.Code);
        Assert.Contains("Observation", outcome.Refusal.Message, StringComparison.Ordinal);
        Assert.Equal(0, Drafts(seed.Project));
    }

    [Fact]
    public void ARecordThatIsItsOwnSubRecordIsRefusedAndMakesNoDraft()
    {
        var seed = Seeded();
        var child = Guid.NewGuid();
        var file = NewFile(seed, seed.Record, (document, _) =>
        {
            var nested = new RecordObject { Guid = child };
            nested["title"] = new RecordScalar("zzself");
            document.Data["sub-records"] = new RecordList().Add(nested);
        });
        Guid root;
        Guid renamed;
        using (var cache = new FwDataProjectLoader().LoadCache(seed.Project))
        {
            var schema = Exported(cache, seed.Record).Schema;
            var document = RecordFileReader.Read(File.ReadAllText(file), schema.Schema, default).Document!;
            root = document.Data.Guid!.Value;
            renamed = ((RecordObject)((RecordList)document.Data["sub-records"]!).Items[0]).Guid!.Value;
        }
        File.WriteAllText(file, File.ReadAllText(file).Replace(renamed.ToString("D"), root.ToString("D"), StringComparison.OrdinalIgnoreCase));

        var outcome = RecordImportCommand.Run(new(seed.Project, file, DraftName));

        Assert.False(outcome.Succeeded);
        Assert.Equal(0, Drafts(seed.Project));
    }

    private static string CanonicalIdOf(Guid guid) => SIL.Motif.Contract.Ids.CanonicalId.FromGuid(guid).Value;
}
