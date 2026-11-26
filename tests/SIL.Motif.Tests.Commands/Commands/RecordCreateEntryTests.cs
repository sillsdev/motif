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

    private sealed record Seed(string Project, Guid Main, Guid Variant, Guid VariantType, Guid Complex);

    private Seed Seeded()
    {
        Directory.CreateDirectory(_folder);
        using var cache = pristine.NewScratch();
        Guid main = default, variant = default, type = default, complex = default;
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
            lexDb.ComplexEntryTypesOA ??= services.GetInstance<ICmPossibilityListFactory>().Create();
            var compound = services.GetInstance<ILexEntryTypeFactory>().Create();
            lexDb.ComplexEntryTypesOA.PossibilitiesOS.Add(compound);
            compound.Name.set_String(cache.DefaultAnalWs, "zzcompound");
            compound.Abbreviation.set_String(cache.DefaultAnalWs, "zzc");
            var other = services.GetInstance<ILexEntryFactory>().Create();
            complex = other.Guid;
            var otherForm = services.GetInstance<IMoStemAllomorphFactory>().Create();
            other.LexemeFormOA = otherForm;
            otherForm.MorphTypeRA = form.MorphTypeRA;
            otherForm.Form.set_String(cache.DefaultVernWs, "zzcomp");
            var otherMsa = services.GetInstance<IMoStemMsaFactory>().Create();
            other.MorphoSyntaxAnalysesOC.Add(otherMsa);
            otherMsa.PartOfSpeechRA = pos;
            var otherSense = services.GetInstance<ILexSenseFactory>().Create();
            other.SensesOS.Add(otherSense);
            otherSense.MorphoSyntaxAnalysisRA = otherMsa;
            otherSense.Gloss.set_String(cache.DefaultAnalWs, "zzcompgloss");
            var link = services.GetInstance<ILexEntryRefFactory>().Create();
            other.EntryRefsOS.Add(link);
            link.RefType = LexEntryRefTags.krtComplexForm;
            link.ComplexEntryTypesRS.Add(compound);
            link.ComponentLexemesRS.Add(entry);
            link.PrimaryLexemesRS.Add(entry);
        });
        new FwDataProjectLoader().Save(cache);
        return new(cache.ProjectId.Path, main, variant, type, complex);
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
    public void AnAffixVariantWithInflectionClassesIsRefusedByName()
    {
        var seed = Seeded();
        Guid affixVariant = default;
        using (var cache = new FwDataProjectLoader().LoadCache(seed.Project))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var services = cache.ServiceLocator;
                var pos = cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.OfType<IPartOfSpeech>().First();
                var inflClass = services.GetInstance<IMoInflClassFactory>().Create();
                pos.InflectionClassesOC.Add(inflClass);
                inflClass.Name.set_String(cache.DefaultAnalWs, "zzclass");
                var entry = services.GetInstance<ILexEntryRepository>().GetObject(seed.Main);
                var kind = services.GetInstance<ILexEntryTypeRepository>().GetObject(seed.VariantType);
                var variant = entry.CreateVariantEntryAndBackRef(kind, TsStringUtils.MakeString("zzsuf", cache.DefaultVernWs)).OwnerOfClass<ILexEntry>();
                affixVariant = variant.Guid;
                var form = services.GetInstance<IMoAffixAllomorphFactory>().Create();
                variant.LexemeFormOA = form;
                form.MorphTypeRA = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphSuffix);
                form.Form.set_String(cache.DefaultVernWs, "zzsuf");
                form.InflectionClassesRC.Add(inflClass);
            });
            new FwDataProjectLoader().Save(cache);
        }
        var file = NewFile(seed, affixVariant);
        Assert.Contains("zzclass", File.ReadAllText(file), StringComparison.Ordinal);

        var outcome = RecordImportCommand.Run(new(seed.Project, file, DraftName));

        Assert.False(outcome.Succeeded);
        Assert.Equal(RefusalCodes.RecordImportCreateShape, outcome.Refusal!.Code);
        Assert.Contains("inflection classes", outcome.Refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AVariantEntryIsStagedAppliedAndReadsBackAsTheFile() => RoundTrips(seed => seed.Variant);

    private static RecordObject Form(RecordDocument d) => (RecordObject)d.Data["lexeme-form"]!;

    private static RecordObject Sense(RecordDocument d) => (RecordObject)((RecordList)d.Data["senses"]!).Items[0];

    private static RecordObject Msa(RecordDocument d) => (RecordObject)((RecordList)d.Data["morpho-syntax-analyses"]!).Items[0];

    private static RecordObject Ref(RecordDocument d) => (RecordObject)((RecordList)d.Data["entry-refs"]!).Items[0];

    public static TheoryData<string> Shapes() => new()
    {
        "no-form", "no-morph-type", "no-form-text", "no-sense", "no-gloss", "no-sense-msa", "no-msa-category",
        "variant-with-sense", "variant-no-type", "variant-no-component", "variant-two-components"
    };

    private static Action<RecordDocument> Break(string shape) => shape switch
    {
        "no-form" => d => d.Data["lexeme-form"] = null,
        "no-morph-type" => d => Form(d)["morph-type"] = null,
        "no-form-text" => d => Form(d)["form"] = null,
        "no-sense" => d => d.Data["senses"] = null,
        "no-gloss" => d => Sense(d)["gloss"] = null,
        "no-sense-msa" => d => Sense(d)["morpho-syntax-analysis"] = null,
        "no-msa-category" => d => Msa(d)["part-of-speech"] = null,
        "variant-with-sense" => d =>
        {
            var sense = new RecordObject { Guid = Guid.NewGuid() };
            sense["gloss"] = new RecordAlternatives().Add("en", new RecordScalar("zzextra"));
            d.Data["senses"] = new RecordList().Add(sense);
        },
        "variant-no-type" => d => Ref(d)["variant-entry-types"] = null,
        "variant-no-component" => d => Ref(d)["component-lexemes"] = null,
        "variant-two-components" => d =>
        {
            var components = (RecordList)Ref(d)["component-lexemes"]!;
            components.Add(components.Items[0]);
        },
        _ => _ => { },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void ACreateThatIsNotTheShapeFieldWorksMakesIsRefusedWholeAndMakesNoDraft(string shape)
    {
        var seed = Seeded();
        var variant = shape.StartsWith("variant", StringComparison.Ordinal);
        var file = NewFile(seed, variant ? seed.Variant : seed.Main, Break(shape));

        var outcome = RecordImportCommand.Run(new(seed.Project, file, DraftName));

        Assert.False(outcome.Succeeded, shape);
        Assert.Equal(RefusalCodes.RecordImportCreateShape, outcome.Refusal!.Code);
        Assert.Contains("FieldWorks", outcome.Refusal.Message, StringComparison.Ordinal);
        Assert.Equal(0, ProposalCommands.List(new(seed.Project, Version)).Value?.Proposals.Count ?? 0);
        Assert.Equal(RefusalCodes.RecordImportCreateShape, RecordImportCommand.Run(new(seed.Project, file)).Refusal!.Code);
    }

    [Fact]
    public void AComplexFormIsStagedAppliedAndReadsBackAsTheFile()
    {
        var seed = Seeded();
        var file = NewFile(seed, seed.Complex, document => Ref(document)["show-complex-forms-in"] = Ref(document)["primary-lexemes"]);
        var created = RecordFileReader.Read(File.ReadAllText(file), Exported(new FwDataProjectLoader().LoadCache(seed.Project), seed.Main).Schema.Schema,
            default).Document!.Data.Guid!.Value;

        StageFinalizeApply(seed.Project, file);

        using var reopened = new FwDataProjectLoader().LoadCache(seed.Project);
        var schema = ProjectRecordSchema.Generate(reopened, ProjectRecordSchema.LexiconEntryOverlay);
        var exporter = new ProjectRecordExporter(reopened, schema);
        Assert.Equal(Normalized(File.ReadAllText(file), schema, exporter), Normalized(exporter.Export(created).Text!, schema, exporter));
    }

    [Fact]
    public void ADryRunReportsTheHomographNumbersTheNewEntryChanges()
    {
        var seed = Seeded();
        var file = NewFile(seed, seed.Main);
        Assert.True(RecordImportCommand.Run(new(seed.Project, file, DraftName)).Succeeded);
        DraftRationale.Author(seed.Project, DraftName, "Create an entry", "The record file describes a new entry.");
        var finalized = ProposalCommands.Finalize(new(seed.Project, Version, DraftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);

        var dryRun = DryRunJobRunner.Run(seed.Project, Version, finalized.Value!.ProposalId);

        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);
        var previews = dryRun.Value!.Effects.Where(effect => effect.Preview is not null).Select(effect => effect.Preview!.Value.GetRawText()).ToList();
        var numbers = Assert.Single(previews, text => text.Contains("homographNumbers", StringComparison.Ordinal));
        Assert.Contains("\"isThisEntry\":true", numbers, StringComparison.Ordinal);
        Assert.Contains("\"isThisEntry\":false", numbers, StringComparison.Ordinal);
        Assert.Contains("existing entry", numbers, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOwnedObjectWhoseGuidTheProjectHasRefusesTheWholeCreate()
    {
        var seed = Seeded();
        Guid taken;
        using (var cache = new FwDataProjectLoader().LoadCache(seed.Project))
            taken = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(seed.Main).SensesOS[0].Guid;
        var file = NewFile(seed, seed.Main);
        var text = File.ReadAllText(file);
        var schema = Exported(new FwDataProjectLoader().LoadCache(seed.Project), seed.Main);
        var read = RecordFileReader.Read(text, schema.Schema.Schema, default);
        File.WriteAllText(file, text.Replace(Sense(read.Document!).Guid!.Value.ToString("D"), taken.ToString("D"), StringComparison.OrdinalIgnoreCase));

        var outcome = RecordImportCommand.Run(new(seed.Project, file, DraftName));

        Assert.False(outcome.Succeeded);
        Assert.Equal(RefusalCodes.RecordImportCreateShape, outcome.Refusal!.Code);
        Assert.Contains("GuidInUse", outcome.Refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AComputedFieldOnACreateIsRefusedWholeAsComputed()
    {
        var seed = Seeded();
        var file = NewFile(seed, seed.Main, document => document.Data["head-word"] = new RecordScalar("zzword"));

        var outcome = RecordImportCommand.Run(new(seed.Project, file, DraftName));

        Assert.False(outcome.Succeeded);
        Assert.Equal(RefusalCodes.RecordImportCreateComputedField, outcome.Refusal!.Code);
    }

    [Fact]
    public void AnEditFileThatAddsASenseWithAGlossAndAnAnalysisLinkLowersAndApplies()
    {
        var seed = Seeded();
        string file;
        Guid sense = Guid.NewGuid();
        using (var cache = new FwDataProjectLoader().LoadCache(seed.Project))
        {
            var (schema, document) = Exported(cache, seed.Main);
            var added = new RecordObject { Guid = sense };
            added["gloss"] = new RecordAlternatives().Add("en", new RecordScalar("zzsecond"));
            added["morpho-syntax-analysis"] = ((RecordObject)((RecordList)document.Data["senses"]!).Items[0])["morpho-syntax-analysis"];
            ((RecordList)document.Data["senses"]!).Add(added);
            file = Path.Combine(_folder, "edit.md");
            File.WriteAllText(file, RecordFileWriter.Write(document, schema.Schema));
        }

        var dry = RecordImportCommand.Run(new(seed.Project, file));

        Assert.True(dry.Succeeded, dry.Refusal?.Message);
        Assert.True(dry.Value!.RefusalCount == 0, string.Join("; ", dry.Value.Refusals?.Select(row => row.Code + " " + row.Field + " " + row.Reason) ?? []));
        Assert.True(dry.Value.OperationCount >= 1, dry.Value.OperationCount.ToString());
        StageFinalizeApply(seed.Project, file);
        using var reopened = new FwDataProjectLoader().LoadCache(seed.Project);
        var added2 = reopened.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(sense);
        Assert.Equal("zzsecond", added2.Gloss.get_String(reopened.DefaultAnalWs).Text);
        Assert.NotNull(added2.MorphoSyntaxAnalysisRA);
    }
}
