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

    private static IFsClosedFeature Feature(LcmCache cache, string abbreviation, params (string Name, string Abbreviation)[] values) =>
        Feature(cache, abbreviation, true, values);

    private static IFsClosedFeature Feature(LcmCache cache, string abbreviation, bool showInGloss, params (string Name, string Abbreviation)[] values)
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
            value.ShowInGloss = showInGloss;
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

    private Built Build(LcmCache cache, bool inflStructure, bool stemStructure = false)
    {
        Built? built = null;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var number = Feature(cache, "num", ("singular", "sg"), ("plural", "pl"));
            var grammaticalCase = Feature(cache, "cas", ("nominative", "nom"), ("accusative", "acc"));
            Feature(cache, "dup", false, ("same", "s1"), ("same", "s1"));
            Feature(cache, "a b", ("n1", "v:1"), ("n2", @"w\2"));
            Feature(cache, "k[1]", ("n3", "[x]"));
            Feature(cache, "\u00e9", ("n4", "sg2"));
            var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(EntryId, cache.LangProject.LexDbOA);
            var stem = cache.ServiceLocator.GetInstance<IMoStemMsaFactory>().Create();
            entry.MorphoSyntaxAnalysesOC.Add(stem);
            var infl = cache.ServiceLocator.GetInstance<IMoInflAffMsaFactory>().Create();
            entry.MorphoSyntaxAnalysesOC.Add(infl);
            if (stemStructure)
            {
                stem.MsFeaturesOA = cache.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create();
                Specify(cache, stem.MsFeaturesOA, number, "singular");
            }
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
        Assert.Equal("[cas:acc num:pl]", ((RecordScalar)Msa(document, "MoInflAffMsa")["infl-feats"]!).Text);
        Assert.Null(Msa(document, "MoStemMsa")["ms-features"]);
        Assert.NotEqual(Guid.Empty, built.Stem);
    }

    private static IFsFeatStruc Structure(LcmCache cache, IReadOnlyList<FeatureStructureSelection> selections)
    {
        var structure = cache.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create();
        var holder = cache.ServiceLocator.GetInstance<IMoStemMsaFactory>().Create();
        cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(Guid.NewGuid(), cache.LangProject.LexDbOA).MorphoSyntaxAnalysesOC.Add(holder);
        holder.MsFeaturesOA = structure;
        foreach (var selection in selections)
            Specify(cache, structure, selection.Feature, selection.Value.Name.get_String(cache.DefaultAnalWs).Text);
        return structure;
    }

    [Theory]
    [InlineData("[]", "[]")]
    [InlineData("[num:sg]", "[num:sg]")]
    [InlineData("  [ num:pl   cas:nom ]  ", "[cas:nom num:pl]")]
    [InlineData("[cas:n\\om num:p\\l]", "[cas:nom num:pl]")]
    [InlineData("[a\\ b:v\\:1 k\\[1\\]:\\[x\\]]", "[a\\ b:v\\:1 k\\[1\\]:\\[x\\]]")]
    [InlineData("[a\\ b:w\\\\2]", "[a\\ b:w\\\\2]")]
    [InlineData("[e\u0301:sg2]", "[\u00e9:sg2]")]
    [InlineData("[\u00e9:sg2 num:pl]", "[num:pl \u00e9:sg2]")]
    public void TextRoundTripsThroughAStructureToItsCanonicalForm(string text, string canonical)
    {
        using var cache = pristine.NewScratch();
        Build(cache, inflStructure: false);
        var system = cache.LangProject.MsFeatureSystemOA;
        var parsed = FeatureStructureNotation.Parse(text, system, cache);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var structure = Structure(cache, parsed);
            Assert.Equal(canonical, FeatureStructureNotation.Render(structure, cache));
            var again = FeatureStructureNotation.Parse(FeatureStructureNotation.Render(structure, cache), system, cache);
            Assert.Equal(parsed.Select(p => (p.Feature.Guid, p.Value.Guid)).Order(), again.Select(p => (p.Feature.Guid, p.Value.Guid)).Order());
            Assert.Equal(canonical, FeatureStructureNotation.Render(Structure(cache, again), cache));
        });
    }

    [Fact]
    public void ValuesLabelByAbbreviationWhateverShowInGlossSays()
    {
        using var cache = pristine.NewScratch();
        Build(cache, inflStructure: true);
        var infl = cache.ServiceLocator.GetInstance<IMoInflAffMsaRepository>().AllInstances().Single();
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            Assert.Equal("[cas:acc num:pl]", FeatureStructureNotation.Render(infl.InflFeatsOA, cache));
            foreach (var value in cache.LangProject.MsFeatureSystemOA.FeaturesOC.OfType<IFsClosedFeature>().SelectMany(f => f.ValuesOC)) value.ShowInGloss = false;
            Assert.Equal("[cas:acc num:pl]", FeatureStructureNotation.Render(infl.InflFeatsOA, cache));
        });
    }

    [Fact]
    public void ALabelFoundOnlyInASecondAnalysisWritingSystemIsUsed()
    {
        using var cache = pristine.NewScratch();
        Build(cache, inflStructure: true);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            cache.ServiceLocator.WritingSystemManager.GetOrSet("fr", out var french);
            cache.ServiceLocator.WritingSystems.AddToCurrentAnalysisWritingSystems(french);
            var number = cache.LangProject.MsFeatureSystemOA.FeaturesOC.OfType<IFsClosedFeature>().Single(f => f.Abbreviation.get_String(cache.DefaultAnalWs).Text == "num");
            number.Abbreviation.set_String(cache.DefaultAnalWs, "");
            number.Abbreviation.set_String(french.Handle, "nbr");
            var infl = cache.ServiceLocator.GetInstance<IMoInflAffMsaRepository>().AllInstances().Single();
            Assert.Equal("[cas:acc nbr:pl]", FeatureStructureNotation.Render(infl.InflFeatsOA, cache));
        });
    }

    private static IFsClosedValue Spec(LcmCache cache, IFsFeatStruc structure, IFsClosedFeature feature, IFsSymFeatVal value)
    {
        var spec = cache.ServiceLocator.GetInstance<IFsClosedValueFactory>().Create();
        structure.FeatureSpecsOC.Add(spec);
        spec.FeatureRA = feature;
        spec.ValueRA = value;
        return spec;
    }

    [Fact]
    public void AStructureThatDoesNotRoundTripIsUnrenderableAndItsFieldIsLockedOnExport()
    {
        using var cache = pristine.NewScratch();
        var built = Build(cache, inflStructure: true);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var features = cache.LangProject.MsFeatureSystemOA.FeaturesOC.OfType<IFsClosedFeature>().ToList();
            var number = features.Single(f => f.Abbreviation.get_String(cache.DefaultAnalWs).Text == "num");
            var other = features.Single(f => f.Abbreviation.get_String(cache.DefaultAnalWs).Text == "a b");
            var infl = (IMoInflAffMsa)cache.ServiceLocator.ObjectRepository.GetObject(built.Infl);
            infl.InflFeatsOA.FeatureSpecsOC.ToList().ForEach(spec => spec.Delete());
            Spec(cache, infl.InflFeatsOA, number, other.ValuesOC.First());
        });
        var failure = Assert.Throws<FeatureStructureNotationException>(() =>
            FeatureStructureNotation.Render(((IMoInflAffMsa)cache.ServiceLocator.ObjectRepository.GetObject(built.Infl)).InflFeatsOA, cache));
        Assert.Equal(FeatureStructureNotation.Unrenderable, failure.Code);
        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
        var export = new ProjectRecordExporter(cache, schema).Export(EntryId);
        var unexported = Assert.Single(export.Unexported, item => item.Field == "InflFeats");
        Assert.Equal(built.Infl, unexported.Object);
        Assert.Null(Msa(export.Document, "MoInflAffMsa")["infl-feats"]);
        var plan = new ProjectRecordImporter(new ProjectRecordExporter(cache, schema)).Plan(ExportText(cache), new CatalogWritableFields(schema));
        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void AComplexValueIsUnrenderable()
    {
        using var cache = pristine.NewScratch();
        var built = Build(cache, inflStructure: true);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var infl = (IMoInflAffMsa)cache.ServiceLocator.ObjectRepository.GetObject(built.Infl);
            infl.InflFeatsOA.FeatureSpecsOC.Add(cache.ServiceLocator.GetInstance<IFsComplexValueFactory>().Create());
        });
        var failure = Assert.Throws<FeatureStructureNotationException>(() =>
            FeatureStructureNotation.Render(((IMoInflAffMsa)cache.ServiceLocator.ObjectRepository.GetObject(built.Infl)).InflFeatsOA, cache));
        Assert.Equal(FeatureStructureNotation.Unrenderable, failure.Code);
    }

    [Theory]
    [InlineData("num:pl", FeatureStructureNotation.Syntax)]
    [InlineData("[num pl]", FeatureStructureNotation.Syntax)]
    [InlineData("[num:pl", FeatureStructureNotation.Syntax)]
    [InlineData("[num:pl] x", FeatureStructureNotation.Syntax)]
    [InlineData("[zzz:pl]", FeatureStructureNotation.UnknownFeature)]
    [InlineData("[num:dual]", FeatureStructureNotation.UnknownValue)]
    [InlineData("[num:plural]", FeatureStructureNotation.UnknownValue)]
    [InlineData("[num:pl:sg]", FeatureStructureNotation.Syntax)]
    [InlineData("[n[um:pl]", FeatureStructureNotation.Syntax)]
    [InlineData("[num:pl num:sg]", FeatureStructureNotation.RepeatedFeature)]
    [InlineData("[dup:s1]", FeatureStructureNotation.AmbiguousLabel)]
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
                Msa(document, "MoStemMsa").Set("ms-features", new RecordScalar("[cas:nom num:sg]"));
                Msa(document, "MoInflAffMsa").Set("infl-feats", new RecordScalar("[num:sg]"));
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
        Assert.Equal("[cas:nom num:sg]",
            FeatureStructureNotation.Render(((IMoStemMsa)entry.MorphoSyntaxAnalysesOC.Single(m => m.Guid == built.Stem)).MsFeaturesOA, reloaded));
        Assert.Equal("[num:sg]",
            FeatureStructureNotation.Render(((IMoInflAffMsa)entry.MorphoSyntaxAnalysesOC.Single(m => m.Guid == built.Infl)).InflFeatsOA, reloaded));
        var schemaAfter = ProjectRecordSchema.Generate(reloaded, ProjectRecordSchema.LexiconEntryOverlay);
        Assert.True(new ProjectRecordImporter(new ProjectRecordExporter(reloaded, schemaAfter))
            .Plan(ExportText(reloaded), new CatalogWritableFields(schemaAfter)).IsEmpty);
    }

    [Theory]
    [InlineData("[zzz:pl]", "fs-notation.unknown-feature")]
    [InlineData("[num:dual]", RefusalCodes.FsNotationUnknownValue)]
    [InlineData("[num:pl", "fs-notation.syntax")]
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
        Assert.Equal(code, refusal.Code);
    }

    [Fact]
    public void AStructureChangedInTheProjectSinceTheExportIsRefusedAsStale()
    {
        using var cache = pristine.NewScratch();
        var built = Build(cache, inflStructure: true);
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        Edit(cache, path, document => Msa(document, "MoInflAffMsa").Set("infl-feats", new RecordScalar("[num:sg]")), out var plan, out var schema);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            ((IMoInflAffMsa)cache.ServiceLocator.ObjectRepository.GetObject(built.Infl)).InflFeatsOA.FeatureSpecsOC.First().Delete());
        var lowering = new RecordChangeSetLowerer(schema, new RecordWritableFields(schema)).Lower(cache, plan);
        Assert.Empty(lowering.Operations);
        Assert.Equal(RefusalCodes.RecordImportValueStale, Assert.Single(lowering.Refusals).Code);
    }

    [Fact]
    public void EmptyTextCreatesAStructureAndClearingDeletesOnlyTheStemOne()
    {
        string path;
        using (var cache = pristine.NewScratch())
        {
            Build(cache, inflStructure: true, stemStructure: true);
            path = cache.ProjectId.Path;
            new FwDataProjectLoader().Save(cache);
            Edit(cache, path, document => Msa(document, "MoStemMsa")["ms-features"] = null, out var plan, out var schema);
            Assert.Empty(plan.ChangeSet.Refusals);
            var lowering = new RecordChangeSetLowerer(schema, new RecordWritableFields(schema)).Lower(cache, plan);
            Assert.Empty(lowering.Refusals);
            Assert.Contains(lowering.Operations, operation => operation.Kind == MoStemMsaMsFeaturesOperationKinds.DeleteMsFeatures);
            Edit(cache, path, document => Msa(document, "MoInflAffMsa")["infl-feats"] = null, out plan, out schema);
            var refused = new RecordChangeSetLowerer(schema, new RecordWritableFields(schema)).Lower(cache, plan);
            Assert.Empty(refused.Operations);
            Assert.Equal(RefusalCodes.RecordImportFieldUncovered, Assert.Single(refused.Refusals).Code);
        }
    }

    [Fact]
    public void EmptyBracketsCreateAnEmptyStructure()
    {
        using var cache = pristine.NewScratch();
        Build(cache, inflStructure: false);
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        Edit(cache, path, document => Msa(document, "MoStemMsa").Set("ms-features", new RecordScalar("[]")), out var plan, out var schema);
        var lowering = new RecordChangeSetLowerer(schema, new RecordWritableFields(schema)).Lower(cache, plan);
        Assert.Empty(lowering.Refusals);
        Assert.Contains(lowering.Operations, operation => operation.Kind == MoStemMsaMsFeaturesOperationKinds.CreateMsFeatures);
        Assert.DoesNotContain(lowering.Operations, operation => operation.Kind == FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs);
    }
}
