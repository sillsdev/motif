using System.Text.RegularExpressions;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
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

/// <summary>A folder of new-record files that refer to each other is staged as one Proposal, applied by a person, and reads back as the files.</summary>
[Collection(LcmCacheParallelCollections.Group2)]
public sealed class RecordBatchImportTests(PristineProjectFixture pristine) : IDisposable
{
    private const string Version = "0.1.0";
    private const string DraftName = "batch";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-batch-import-" + Guid.NewGuid().ToString("N"));
    private string Folder => Path.Combine(_root, "records");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private sealed record Seed(string Project, Guid Main, Guid Variant, Guid VariantType, Guid Complex);

    private Seed Seeded()
    {
        Directory.CreateDirectory(Folder);
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
            sense.Definition.set_String(cache.DefaultAnalWs, "zzdefinition");
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

    private static (ProjectRecordSchemaResult Schema, ProjectRecordExporter Exporter) Tools(LcmCache cache)
    {
        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
        return (schema, new ProjectRecordExporter(cache, schema));
    }

    private static RecordObject Ref(RecordDocument d) => (RecordObject)((RecordList)d.Data["entry-refs"]!).Items[0];

    private static RecordObject Sense(RecordDocument d) => (RecordObject)((RecordList)d.Data["senses"]!).Items[0];

    // Writes the template's export as a new-record file, edited by the callbacks; returns the root.
    private Guid Write(Seed seed, string name, Guid template, Func<string, string>? text = null, Action<RecordDocument>? edit = null, Guid? root = null)
    {
        Directory.CreateDirectory(Folder);
        var made = root ?? Guid.NewGuid();
        using var cache = new FwDataProjectLoader().LoadCache(seed.Project);
        var (schema, exporter) = Tools(cache);
        var read = RecordFileReader.Read(exporter.Export(template).Text!, schema.Schema, exporter.CreateReadOptions());
        Assert.False(read.HasErrors);
        var document = read.Document!;
        document.Base = null;
        document.Data["head-word"] = null;
        document.Data["homograph-number"] = null;
        edit?.Invoke(document);
        var written = RecordCreatePlanCommandTests.WriteRenamed(document, schema, made);
        File.WriteAllText(Path.Combine(Folder, name), text?.Invoke(written) ?? written);
        return made;
    }

    private static string Replace(string text, Guid from, Guid to) => text.Replace(from.ToString("D"), to.ToString("D"), StringComparison.OrdinalIgnoreCase);

    /// <summary>The GUID of the first sense that a written file gives its entry.</summary>
    private Guid SenseOf(Seed seed, string name)
    {
        using var cache = new FwDataProjectLoader().LoadCache(seed.Project);
        var (schema, exporter) = Tools(cache);
        var read = RecordFileReader.Read(File.ReadAllText(Path.Combine(Folder, name)), schema.Schema, exporter.CreateReadOptions());
        return Sense(read.Document!).Guid!.Value;
    }

    private RecordImportRequest Dry(Seed seed, string? draft = null) =>
        new(seed.Project, null, draft, 1, false, Folder, true, null, null);

    private Dictionary<string, RecordImportFileRow> Rows(Seed seed)
    {
        var outcome = RecordImportCommand.Run(Dry(seed));
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        return outcome.Value!.Files!.ToDictionary(row => row.Path);
    }

    private void StageFinalizeApply(Seed seed)
    {
        var staged = RecordImportCommand.Run(Dry(seed, DraftName));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        DraftRationale.Author(seed.Project, DraftName, "Create entries", "The record files describe new entries.");
        var finalized = ProposalCommands.Finalize(new(seed.Project, Version, DraftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var dryRun = DryRunJobRunner.Run(seed.Project, Version, finalized.Value!.ProposalId);
        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);
        var applied = ProposalCommands.Apply(new(seed.Project, Version, finalized.Value.ProposalId, Force: true));
        Assert.True(applied.Succeeded, applied.Refusal?.Message);
    }

    private static string Normalized(string text, ProjectRecordSchemaResult schema, ProjectRecordExporter exporter)
    {
        var read = RecordFileReader.Read(text, schema.Schema, exporter.CreateReadOptions());
        Assert.False(read.HasErrors);
        read.Document!.Base = null;
        read.Document.Data["head-word"] = null;
        read.Document.Data["homograph-number"] = null;
        // A link's label and path are computed display text; its guid is the link.
        return Regex.Replace(RecordFileWriter.Write(read.Document, schema.Schema), @"\[[^\]]*\]\([^{]*?\)(?=\{[^}]*guid=)", "[](/)");
    }

    /// <summary>Stages, applies as a person, reopens, and requires each record to export as its file.</summary>
    private void RoundTrips(Seed seed, params (string Name, Guid Root)[] files)
    {
        var rows = Rows(seed);
        Assert.All(files, file => Assert.True(rows[file.Name].Status == "created", file.Name + " " + rows[file.Name].Status + " " + rows[file.Name].Code + " " + rows[file.Name].Reason));
        StageFinalizeApply(seed);
        using var reopened = new FwDataProjectLoader().LoadCache(seed.Project);
        var (schema, exporter) = Tools(reopened);
        foreach (var (name, root) in files)
            Assert.Equal(Normalized(File.ReadAllText(Path.Combine(Folder, name)), schema, exporter), Normalized(exporter.Export(root).Text!, schema, exporter));
    }

    [Fact]
    public void ANewVariantOfANewMainEntryInAnotherFileAppliesAndReadsBackAsTheFiles()
    {
        var seed = Seeded();
        var main = Write(seed, "b-main.fw.md", seed.Main);
        var variant = Write(seed, "a-variant.fw.md", seed.Variant, text => Replace(text, seed.Main, main));

        RoundTrips(seed, ("a-variant.fw.md", variant), ("b-main.fw.md", main));
    }

    [Fact]
    public void AVariantOfANewSenseOfANewEntryAppliesAndReadsBackAsTheFiles()
    {
        var seed = Seeded();
        var main = Write(seed, "b-main.fw.md", seed.Main);
        var sense = SenseOf(seed, "b-main.fw.md");
        var variant = Write(seed, "a-variant.fw.md", seed.Variant, text => Replace(text, seed.Main, sense));

        RoundTrips(seed, ("a-variant.fw.md", variant), ("b-main.fw.md", main));
    }

    [Fact]
    public void AComplexFormOfANewVariantOfANewMainEntryAppliesAndReadsBackAsTheFiles()
    {
        var seed = Seeded();
        var main = Write(seed, "c-main.fw.md", seed.Main);
        var variant = Write(seed, "b-variant.fw.md", seed.Variant, text => Replace(text, seed.Main, main));
        var complex = Write(seed, "a-complex.fw.md", seed.Complex, text => Replace(text, seed.Main, variant),
            document => Ref(document)["show-complex-forms-in"] = Ref(document)["primary-lexemes"]);

        RoundTrips(seed, ("a-complex.fw.md", complex), ("b-variant.fw.md", variant), ("c-main.fw.md", main));
    }

    // liblcm throws on the second add: LexEntryRef.ValidateAddObjectInternal.
    [Fact]
    public void AVariantAndAComplexFormThatNameEachOtherAreRefusedAsOneLoop()
    {
        var seed = Seeded();
        var variantRoot = Guid.NewGuid();
        var complex = Write(seed, "b-complex.fw.md", seed.Complex, text => Replace(text, seed.Main, variantRoot));
        Write(seed, "a-variant.fw.md", seed.Variant, text => Replace(text, seed.Main, complex), root: variantRoot);

        var rows = Rows(seed);

        Assert.Equal(("refused", RefusalCodes.RecordImportBatchCycle), (rows["a-variant.fw.md"].Status, rows["a-variant.fw.md"].Code));
        Assert.Equal(("refused", RefusalCodes.RecordImportBatchCycle), (rows["b-complex.fw.md"].Status, rows["b-complex.fw.md"].Code));
    }

    [Fact]
    public void AVariantCycleIsRefusedForEveryFileInTheCycleAndNotForAnUnrelatedFile()
    {
        var seed = Seeded();
        var first = Guid.NewGuid();
        var second = Write(seed, "b-two.fw.md", seed.Variant, text => Replace(text, seed.Main, first));
        Write(seed, "a-one.fw.md", seed.Variant, text => Replace(text, seed.Main, second), root: first);
        Write(seed, "c-free.fw.md", seed.Main);

        var rows = Rows(seed);

        Assert.Equal(("refused", RefusalCodes.RecordImportBatchCycle), (rows["a-one.fw.md"].Status, rows["a-one.fw.md"].Code));
        Assert.Equal(("refused", RefusalCodes.RecordImportBatchCycle), (rows["b-two.fw.md"].Status, rows["b-two.fw.md"].Code));
        Assert.Equal("created", rows["c-free.fw.md"].Status);
        Assert.Contains("a-one.fw.md", rows["b-two.fw.md"].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ARefusedFilesNewRecordRefusesEveryFileThatLinksToItAndStagesNoDanglingLink()
    {
        var seed = Seeded();
        var broken = Write(seed, "b-broken.fw.md", seed.Main, edit: document => Sense(document)["gloss"] = null);
        Write(seed, "a-variant.fw.md", seed.Variant, text => Replace(text, seed.Main, broken));
        Write(seed, "c-variant-of-variant.fw.md", seed.Variant, text => Replace(text, seed.Main, RootOf(seed, "a-variant.fw.md")));
        Write(seed, "d-free.fw.md", seed.Main);

        var rows = Rows(seed);

        Assert.Equal(RefusalCodes.RecordImportCreateShape, rows["b-broken.fw.md"].Code);
        Assert.Equal(("refused", RefusalCodes.RecordImportReferenceToRefused), (rows["a-variant.fw.md"].Status, rows["a-variant.fw.md"].Code));
        Assert.Contains("b-broken.fw.md", rows["a-variant.fw.md"].Reason, StringComparison.Ordinal);
        Assert.Equal("created", rows["d-free.fw.md"].Status);
        Assert.Equal(("refused", RefusalCodes.RecordImportReferenceToRefused), (rows["c-variant-of-variant.fw.md"].Status, rows["c-variant-of-variant.fw.md"].Code));
        var staged = RecordImportCommand.Run(Dry(seed, DraftName));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        Assert.Equal(1, staged.Value!.Dir!.Created);
    }

    private static RecordAlternatives LinkTo(Guid target) => new RecordAlternatives().Add("en", new RecordScalar("see [zzlink](/objects/" + target.ToString("D") + "){.nostyle guid=" + target.ToString("D") + "}"));

    [Fact]
    public void ARichTextLinkToANewEntryOfARefusedFileRefusesTheReferrerAndStagesNoDanglingLink()
    {
        var seed = Seeded();
        var broken = Write(seed, "b-broken.fw.md", seed.Main, edit: document => Sense(document)["gloss"] = null);
        Write(seed, "a-linker.fw.md", seed.Main, edit: document => Sense(document)["definition"] = LinkTo(broken));
        Write(seed, "c-free.fw.md", seed.Main);

        var rows = Rows(seed);

        Assert.Equal(RefusalCodes.RecordImportCreateShape, rows["b-broken.fw.md"].Code);
        Assert.Equal(("refused", RefusalCodes.RecordImportReferenceToRefused), (rows["a-linker.fw.md"].Status, rows["a-linker.fw.md"].Code));
        Assert.Contains("b-broken.fw.md", rows["a-linker.fw.md"].Reason, StringComparison.Ordinal);
        Assert.Equal("created", rows["c-free.fw.md"].Status);
        var staged = RecordImportCommand.Run(Dry(seed, DraftName));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);
        Assert.Equal(1, staged.Value!.Dir!.Created);
    }

    [Fact]
    public void TwoNewEntriesWhoseDefinitionsLinkToEachOtherApplyAndReadBackAsTheFilesWithTheEntryTool()
    {
        var seed = Seeded();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        Write(seed, "a-first.fw.md", seed.Main, edit: document => Sense(document)["definition"] = LinkTo(second), root: first);
        Write(seed, "b-second.fw.md", seed.Main, edit: document => Sense(document)["definition"] = LinkTo(first), root: second);

        RoundTrips(seed, ("a-first.fw.md", first), ("b-second.fw.md", second));

        using var reopened = new FwDataProjectLoader().LoadCache(seed.Project);
        var entry = reopened.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(first);
        var definition = entry.SensesOS[0].Definition.get_String(reopened.DefaultAnalWs);
        var data = Enumerable.Range(0, definition.RunCount).Select(run => definition.get_Properties(run).GetStrPropValue((int)FwTextPropType.ktptObjData)).Single(text => text != null);
        Assert.Contains("lexiconEdit", data, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileThatDependsOnAFileOfARefusedCycleIsRefusedNamingTheCauseAndNotCreated()
    {
        var seed = Seeded();
        var first = Guid.NewGuid();
        var second = Write(seed, "b-two.fw.md", seed.Variant, text => Replace(text, seed.Main, first));
        Write(seed, "a-one.fw.md", seed.Variant, text => Replace(text, seed.Main, second), root: first);
        Write(seed, "c-on-cycle.fw.md", seed.Variant, text => Replace(text, seed.Main, first));
        Write(seed, "d-links-cycle.fw.md", seed.Main, edit: document => Sense(document)["definition"] = LinkTo(second));
        Write(seed, "e-free.fw.md", seed.Main);

        var rows = Rows(seed);

        Assert.Equal(RefusalCodes.RecordImportBatchCycle, rows["a-one.fw.md"].Code);
        foreach (var name in new[] { "c-on-cycle.fw.md", "d-links-cycle.fw.md" })
            Assert.Equal(("refused", RefusalCodes.RecordImportReferenceToRefused), (rows[name].Status, rows[name].Code));
        Assert.Contains("a-one.fw.md", rows["c-on-cycle.fw.md"].Reason, StringComparison.Ordinal);
        Assert.Contains("b-two.fw.md", rows["d-links-cycle.fw.md"].Reason, StringComparison.Ordinal);
        Assert.Equal("created", rows["e-free.fw.md"].Status);
    }

    private Guid RootOf(Seed seed, string name)
    {
        using var cache = new FwDataProjectLoader().LoadCache(seed.Project);
        var (schema, exporter) = Tools(cache);
        return RecordFileReader.Read(File.ReadAllText(Path.Combine(Folder, name)), schema.Schema, exporter.CreateReadOptions()).Document!.Data.Guid!.Value;
    }

    [Fact]
    public void AComplexFormWhoseComponentsAreOneNewAndOneExistingEntryAppliesAndReadsBackAsTheFile()
    {
        var seed = Seeded();
        var main = Write(seed, "b-main.fw.md", seed.Main);
        var complex = Write(seed, "a-complex.fw.md", seed.Complex, text => SecondComponent(text, seed.Main, main), document =>
        {
            var components = (RecordList)Ref(document)["component-lexemes"]!;
            components.Add(components.Items[0]);
            Ref(document)["show-complex-forms-in"] = Ref(document)["primary-lexemes"];
        });

        RoundTrips(seed, ("a-complex.fw.md", complex), ("b-main.fw.md", main));
    }

    /// <summary>Points the last item of the component-lexemes list at another object.</summary>
    private static string SecondComponent(string text, Guid existing, Guid replacement)
    {
        var lines = text.Split('\n');
        var start = Array.FindIndex(lines, line => line.TrimStart().StartsWith("component-lexemes:", StringComparison.Ordinal));
        var last = start;
        for (var index = start + 1; index < lines.Length && lines[index].TrimStart().StartsWith("- ", StringComparison.Ordinal); index++) last = index;
        lines[last] = Replace(lines[last], existing, replacement);
        return string.Join('\n', lines);
    }
}
