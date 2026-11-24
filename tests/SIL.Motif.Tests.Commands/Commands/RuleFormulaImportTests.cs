using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Records;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Project;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>An affix process's Input and Output are shown as rule-formula text, and an import that edits either is refused precisely.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RuleFormulaImportTests(PristineProjectFixture pristine)
{
    private static readonly Guid EntryId = Guid.Parse("00000000-0000-4000-8000-0000000000e2");

    private static void Build(LcmCache cache)
    {
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(EntryId, cache.LangProject.LexDbOA);
            var process = cache.ServiceLocator.GetInstance<IMoAffixProcessFactory>().Create();
            entry.AlternateFormsOS.Add(process);
            var gap = cache.ServiceLocator.GetInstance<IPhVariableFactory>().Create();
            process.InputOS.Add(gap);
            var copy = cache.ServiceLocator.GetInstance<IMoCopyFromInputFactory>().Create();
            process.OutputOS.Add(copy);
            copy.ContentRA = gap;
        });
    }

    private static RecordObject Process(RecordDocument document) =>
        ((RecordList)document.Data["allomorphs"]!).Items.OfType<RecordObject>().Single();

    private static RecordLowering Lower(LcmCache cache, string key, string text)
    {
        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
        var exporter = new ProjectRecordExporter(cache, schema);
        var read = RecordFileReader.Read(exporter.Export(EntryId).Text!, schema.Schema, exporter.CreateReadOptions());
        Assert.False(read.HasErrors);
        Process(read.Document!).Set(key, new RecordScalar(text));
        var plan = new ProjectRecordImporter(exporter).Plan(RecordFileWriter.Write(read.Document!, schema.Schema), new CatalogWritableFields(schema));
        Assert.Empty(plan.ChangeSet.Refusals);
        return new RecordChangeSetLowerer(schema, new RecordWritableFields(schema)).Lower(cache, plan);
    }

    [Fact]
    public void TheFieldsExportAsFormulaText()
    {
        using var cache = pristine.NewScratch();
        Build(cache);
        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
        var exporter = new ProjectRecordExporter(cache, schema);
        var process = Process(exporter.Export(EntryId).Document);
        Assert.Equal("*", ((RecordScalar)process["input"]!).Text);
        Assert.Equal("$1", ((RecordScalar)process["output"]!).Text);
    }

    [Theory]
    [InlineData("input", "* a")]
    [InlineData("output", "a $1")]
    public void AnEditedFormulaIsRefusedAsReadOnlyAndStagesNothing(string key, string text)
    {
        using var cache = pristine.NewScratch();
        Build(cache);
        new FwDataProjectLoader().Save(cache);
        var lowering = Lower(cache, key, text);
        Assert.Empty(lowering.Operations);
        Assert.Equal(RefusalCodes.RuleNotationReadOnly, Assert.Single(lowering.Refusals).Code);
    }
}
