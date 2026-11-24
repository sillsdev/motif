using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Records;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Project;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Feature structures of an entry's stem and inflectional analyses as record-file text over a synthetic feature system:
/// the text round-trips, an edited file stages operations, Apply writes them, and the reopened project reads back.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class FeatureStructureNotationRecordTests(PristineProjectFixture pristine)
{
    private const string Version = "0.1.0";
    private const string DraftName = "fs";
    private static readonly Guid EntryId = Guid.Parse("00000000-0000-4000-8000-0000000000e1");

    private sealed record Built(Guid Stem, Guid Infl);

    private static IFsClosedFeature Feature(LcmCache cache, string abbreviation, params (string Name, string Abbreviation)[] values)
    {
        var system = cache.LangProject.MsFeatureSystemOA ??= cache.ServiceLocator.GetInstance<IFsFeatureSystemFactory>().Create();
        var feature = cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>().Create();
        system.FeaturesOC.Add(feature);
        feature.Abbreviation.set_String(cache.DefaultAnalWs, abbreviation);
        feature.Name.set_String(cache.DefaultAnalWs, abbreviation + " name");
        foreach (var (name, abbreviationOfValue) in values)
        {
            var value = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
            feature.ValuesOC.Add(value);
            value.Name.set_String(cache.DefaultAnalWs, name);
            value.Abbreviation.set_String(cache.DefaultAnalWs, abbreviationOfValue);
        }
        return feature;
    }

    private static void Specify(LcmCache cache, IFsFeatStruc structure, IFsClosedFeature feature, string valueName)
    {
        var spec = cache.ServiceLocator.GetInstance<IFsClosedValueFactory>().Create();
        structure.FeatureSpecsOC.Add(spec);
        spec.FeatureRA = feature;
        spec.ValueRA = feature.ValuesOC.Single(value => value.Name.get_String(cache.DefaultAnalWs).Text == valueName);
    }

    private Built Build(LcmCache cache, bool inflStructure)
    {
        Built? built = null;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var number = Feature(cache, "num", ("singular", "sg"), ("plural", "pl"));
            var grammaticalCase = Feature(cache, "cas", ("nominative", "nom"), ("accusative", "acc"));
            Feature(cache, "dup", ("same", "s1"), ("same", "s2"));
            var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(EntryId, cache.LangProject.LexDbOA);
            var stem = cache.ServiceLocator.GetInstance<IMoStemMsaFactory>().Create();
            entry.MorphoSyntaxAnalysesOC.Add(stem);
            var infl = cache.ServiceLocator.GetInstance<IMoInflAffMsaFactory>().Create();
            entry.MorphoSyntaxAnalysesOC.Add(infl);
            if (inflStructure)
            {
                infl.InflFeatsOA = cache.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create();
                Specify(cache, infl.InflFeatsOA, number, "plural");
                Specify(cache, infl.InflFeatsOA, grammaticalCase, "accusative");
            }
            built = new Built(stem.Guid, infl.Guid);
        });
        return built!;
    }

    private static string ExportText(LcmCache cache) =>
        new ProjectRecordExporter(cache, ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay)).Export(EntryId).Text!;

    private static RecordObject Msa(RecordDocument document, string cls) =>
        ((RecordList)document.Data["morpho-syntax-analyses"]!).Items.OfType<RecordObject>().Single(item => item.Class == cls);

    private static void Edit(LcmCache cache, string path, Action<RecordDocument> edit, out ProjectRecordPlan plan, out ProjectRecordSchemaResult schema)
    {
        schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
        var exporter = new ProjectRecordExporter(cache, schema);
        var read = RecordFileReader.Read(exporter.Export(EntryId).Text!, schema.Schema, exporter.CreateReadOptions());
        Assert.False(read.HasErrors);
        edit(read.Document!);
        plan = new ProjectRecordImporter(exporter).Plan(RecordFileWriter.Write(read.Document!, schema.Schema), new CatalogWritableFields(schema));
    }

    private static void FinalizeAndApply(string path)
    {
        DraftRationale.Author(path, DraftName, "Imported record", "The record file was edited and imported.");
        var finalized = ProposalCommands.Finalize(new(path, Version, DraftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var dryRun = DryRunJobRunner.Run(path, Version, finalized.Value!.ProposalId);
        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);
        var applied = ProposalCommands.Apply(new(path, Version, finalized.Value.ProposalId, Force: true));
        Assert.True(applied.Succeeded, applied.Refusal?.Message);
    }

    [Fact]
    public void AStructureExportsAsOrderedTextAndAnAbsentOneAsNothing()
    {
        using var cache = pristine.NewScratch();
        var built = Build(cache, inflStructure: true);
        var document = RecordFileReader.Read(ExportText(cache), ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay).Schema,
            new ProjectRecordExporter(cache, ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay)).CreateReadOptions()).Document!;
        Assert.Equal("[cas:accusative num:plural]", ((RecordScalar)Msa(document, "MoInflAffMsa")["infl-feats"]!).Text);
        Assert.Null(Msa(document, "MoStemMsa")["ms-features"]);
        Assert.NotEqual(Guid.Empty, built.Stem);
    }

    [Theory]
    [InlineData("[]", 0)]
    [InlineData("[num:singular]", 1)]
    [InlineData("  [ num:plural   cas:nominative ]  ", 2)]
    public void TextReadsToSelectionsAndRendersCanonically(string text, int count)
    {
        using var cache = pristine.NewScratch();
        Build(cache, inflStructure: false);
        var parsed = FeatureStructureNotation.Parse(text, cache.LangProject.MsFeatureSystemOA, cache);
        Assert.Equal(count, parsed.Count);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var structure = cache.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create();
            cache.ServiceLocator.GetInstance<IMoStemMsaFactory>().Create();
            var holder = cache.ServiceLocator.GetInstance<IMoStemMsaFactory>().Create();
            cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(Guid.NewGuid(), cache.LangProject.LexDbOA).MorphoSyntaxAnalysesOC.Add(holder);
            holder.MsFeaturesOA = structure;
            foreach (var selection in parsed)
                Specify(cache, structure, selection.Feature, selection.Value.Name.get_String(cache.DefaultAnalWs).Text);
            var rendered = FeatureStructureNotation.Render(structure, cache);
            Assert.Equal(rendered, FeatureStructureNotation.Render(structure, cache));
            Assert.Equal(parsed.Select(p => p.Value.Guid).Order(),
                FeatureStructureNotation.Parse(rendered, cache.LangProject.MsFeatureSystemOA, cache).Select(p => p.Value.Guid).Order());
        });
    }

    [Theory]
    [InlineData("num:plural", FeatureStructureNotation.Syntax)]
    [InlineData("[num plural]", FeatureStructureNotation.Syntax)]
    [InlineData("[num:plural", FeatureStructureNotation.Syntax)]
    [InlineData("[num:plural] x", FeatureStructureNotation.Syntax)]
    [InlineData("[zzz:plural]", FeatureStructureNotation.UnknownFeature)]
    [InlineData("[num:dual]", FeatureStructureNotation.UnknownValue)]
    [InlineData("[num:plural num:singular]", FeatureStructureNotation.RepeatedFeature)]
    [InlineData("[dup:same]", FeatureStructureNotation.AmbiguousLabel)]
    public void BadTextIsRefusedWithACode(string text, string code)
    {
        using var cache = pristine.NewScratch();
        Build(cache, inflStructure: false);
        var failure = Assert.Throws<FeatureStructureNotationException>(() =>
            FeatureStructureNotation.Parse(text, cache.LangProject.MsFeatureSystemOA, cache));
        Assert.Equal(code, failure.Code);
    }

    [Fact]
    public void EditedStemAndInflectionalStructuresStageApplyAndReadBackAfterReopening()
    {
        string path;
        Built built;
        using (var cache = pristine.NewScratch())
        {
            built = Build(cache, inflStructure: true);
            path = cache.ProjectId.Path;
            new FwDataProjectLoader().Save(cache);
            Edit(cache, path, document =>
            {
                Msa(document, "MoStemMsa").Set("ms-features", new RecordScalar("[cas:nominative num:singular]"));
                Msa(document, "MoInflAffMsa").Set("infl-feats", new RecordScalar("[num:singular]"));
            }, out var plan, out var schema);
            Assert.Empty(plan.ChangeSet.Refusals);
            Assert.Equal(2, plan.ChangeSet.Changes.Count);
            Assert.True(ProposalCommands.New(new(path, Version, DraftName, "Record import")).Succeeded);
            var staged = ProposalCommands.StageRecordImport(path, DraftName, schema, plan);
            Assert.True(staged.Succeeded, staged.Refusal?.Message);
            Assert.Empty(staged.Value!.Lowering.Refusals);
            Assert.Contains(staged.Value.Lowering.Operations, operation => operation.Kind == MoStemMsaMsFeaturesOperationKinds.CreateMsFeatures);
            Assert.Contains(staged.Value.Lowering.Operations, operation => operation.Kind == FsFeatStrucFeatureSpecsOperationKinds.DeleteFeatureSpecs);
        }

        FinalizeAndApply(path);

        using var reloaded = new FwDataProjectLoader().LoadCache(path);
        var entry = reloaded.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(EntryId);
        Assert.Equal("[cas:nominative num:singular]",
            FeatureStructureNotation.Render(((IMoStemMsa)entry.MorphoSyntaxAnalysesOC.Single(m => m.Guid == built.Stem)).MsFeaturesOA, reloaded));
        Assert.Equal("[num:singular]",
            FeatureStructureNotation.Render(((IMoInflAffMsa)entry.MorphoSyntaxAnalysesOC.Single(m => m.Guid == built.Infl)).InflFeatsOA, reloaded));
        var schemaAfter = ProjectRecordSchema.Generate(reloaded, ProjectRecordSchema.LexiconEntryOverlay);
        Assert.True(new ProjectRecordImporter(new ProjectRecordExporter(reloaded, schemaAfter))
            .Plan(ExportText(reloaded), new CatalogWritableFields(schemaAfter)).IsEmpty);
    }

    [Theory]
    [InlineData("[zzz:plural]", "fs-notation.unknown-feature")]
    [InlineData("[num:dual]", "fs-notation.unknown-value")]
    [InlineData("[num:plural", "fs-notation.syntax")]
    public void AnUnknownFeatureOrValueIsRefusedAndStagesNothing(string text, string code)
    {
        using var cache = pristine.NewScratch();
        Build(cache, inflStructure: true);
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        Edit(cache, path, document => Msa(document, "MoInflAffMsa").Set("infl-feats", new RecordScalar(text)), out var plan, out var schema);
        var lowering = new RecordChangeSetLowerer(schema, new RecordWritableFields(schema)).Lower(cache, plan);
        Assert.Empty(lowering.Operations);
        var refusal = Assert.Single(lowering.Refusals);
        Assert.Equal(RefusalCodes.RecordImportNotExecutable, refusal.Code);
        Assert.Contains(code, refusal.Reason);
    }

    [Fact]
    public void AStructureChangedInTheProjectSinceTheExportIsRefusedAsStale()
    {
        using var cache = pristine.NewScratch();
        var built = Build(cache, inflStructure: true);
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        Edit(cache, path, document => Msa(document, "MoInflAffMsa").Set("infl-feats", new RecordScalar("[num:singular]")), out var plan, out var schema);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            ((IMoInflAffMsa)cache.ServiceLocator.ObjectRepository.GetObject(built.Infl)).InflFeatsOA.FeatureSpecsOC.First().Delete());
        var lowering = new RecordChangeSetLowerer(schema, new RecordWritableFields(schema)).Lower(cache, plan);
        Assert.Empty(lowering.Operations);
        Assert.Equal(RefusalCodes.RecordImportValueStale, Assert.Single(lowering.Refusals).Code);
    }
}
