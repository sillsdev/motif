using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Records;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Project;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>A record file for a new entry is staged, applied by a person, and the reopened project exports the same record.</summary>
[Collection(LcmCacheParallelCollections.Group2)]
public sealed class RecordCreateEntryTests(PristineProjectFixture pristine) : IDisposable
{
    private const string Version = "0.1.0";
    private const string DraftName = "create-entry";
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "motif-create-entry-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }

    private sealed record Seed(string Project, Guid Main, Guid Variant, Guid VariantType);

    private Seed Seeded()
    {
        Directory.CreateDirectory(_folder);
        using var cache = pristine.NewScratch();
        Guid main = default, variant = default, type = default;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            var entry = services.GetInstance<ILexEntryFactory>().Create();
            main = entry.Guid;
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
            var lexDb = cache.LangProject.LexDbOA;
            lexDb.VariantEntryTypesOA ??= services.GetInstance<ICmPossibilityListFactory>().Create();
            var kind = services.GetInstance<ILexEntryTypeFactory>().Create();
            lexDb.VariantEntryTypesOA.PossibilitiesOS.Add(kind);
            kind.Name.set_String(cache.DefaultAnalWs, "zzvariant");
            kind.Abbreviation.set_String(cache.DefaultAnalWs, "zzv");
            type = kind.Guid;
            variant = entry.CreateVariantEntryAndBackRef(kind, TsStringUtils.MakeString("zzvar", cache.DefaultVernWs))
                .OwnerOfClass<ILexEntry>().Guid;
        });
        new FwDataProjectLoader().Save(cache);
        return new(cache.ProjectId.Path, main, variant, type);
    }

    private static (ProjectRecordSchemaResult Schema, RecordDocument Document) Exported(LcmCache cache, Guid entry)
    {
        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
        var exporter = new ProjectRecordExporter(cache, schema);
        var read = RecordFileReader.Read(exporter.Export(entry).Text!, schema.Schema, exporter.CreateReadOptions());
        Assert.False(read.HasErrors);
        return (schema, read.Document!);
    }

    /// <summary>The export of the template as a file for a new record.</summary>
    private string NewFile(Seed seed, Guid template, Action<RecordDocument>? edit = null, string name = "new.md")
    {
        using var cache = new FwDataProjectLoader().LoadCache(seed.Project);
        var (schema, document) = Exported(cache, template);
        document.Base = null;
        document.Data["head-word"] = null;
        document.Data["homograph-number"] = null;
        edit?.Invoke(document);
        var file = Path.Combine(_folder, name);
        File.WriteAllText(file, RecordCreatePlanCommandTests.WriteRenamed(document, schema));
        return file;
    }

    private static string Normalized(string text, ProjectRecordSchemaResult schema, ProjectRecordExporter exporter)
    {
        var read = RecordFileReader.Read(text, schema.Schema, exporter.CreateReadOptions());
        Assert.False(read.HasErrors);
        read.Document!.Base = null;
        read.Document.Data["head-word"] = null;
        read.Document.Data["homograph-number"] = null;
        return RecordFileWriter.Write(read.Document, schema.Schema);
    }

    private static string StageFinalizeApply(string project, string file)
    {
        var staged = RecordImportCommand.Run(new(project, file, DraftName));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        DraftRationale.Author(project, DraftName, "Create an entry", "The record file describes a new entry.");
        var finalized = ProposalCommands.Finalize(new(project, Version, DraftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var dryRun = DryRunJobRunner.Run(project, Version, finalized.Value!.ProposalId);
        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);
        var applied = ProposalCommands.Apply(new(project, Version, finalized.Value.ProposalId, Force: true));
        Assert.True(applied.Succeeded, applied.Refusal?.Message);
        return finalized.Value.ProposalId;
    }

    private void RoundTrips(Func<Seed, Guid> template)
    {
        var seed = Seeded();
        var file = NewFile(seed, template(seed));
        var created = RecordFileReader.Read(File.ReadAllText(file), Exported(new FwDataProjectLoader().LoadCache(seed.Project), seed.Main).Schema.Schema,
            default).Document!.Data.Guid!.Value;
        StageFinalizeApply(seed.Project, file);

        using var reopened = new FwDataProjectLoader().LoadCache(seed.Project);
        var schema = ProjectRecordSchema.Generate(reopened, ProjectRecordSchema.LexiconEntryOverlay);
        var exporter = new ProjectRecordExporter(reopened, schema);
        Assert.Equal(Normalized(File.ReadAllText(file), schema, exporter), Normalized(exporter.Export(created).Text!, schema, exporter));
    }

    [Fact]
    public void AMainEntryIsStagedAppliedAndReadsBackAsTheFile() => RoundTrips(seed => seed.Main);

    [Fact]
    public void AVariantEntryIsStagedAppliedAndReadsBackAsTheFile() => RoundTrips(seed => seed.Variant);
}
