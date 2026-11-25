using SIL.LCModel;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Records;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Project;
using Xunit;

namespace SIL.Motif.Tests.Commands.Review;

/// <summary>Only a Proposal that Motif's own record import wrote whole is captured as exempt from the attested-gain gate.</summary>
[Collection(LcmCacheParallelCollections.Group0)]
public sealed class RecordImportGainExemptionTests(PristineProjectFixture pristine) : IDisposable
{
    private const string Version = "0.1.0";
    private readonly string _root = Directory.CreateTempSubdirectory("motif-import-gain-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private string SeededProject(out Guid entry)
    {
        using var cache = pristine.NewScratch();
        Guid made = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            var pos = services.GetInstance<IPartOfSpeechFactory>().Create();
            cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(pos);
            pos.Name.set_String(cache.DefaultAnalWs, "zznoun");
            var item = services.GetInstance<ILexEntryFactory>().Create();
            made = item.Guid;
            var form = services.GetInstance<IMoStemAllomorphFactory>().Create();
            item.LexemeFormOA = form;
            form.MorphTypeRA = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
            form.Form.set_String(cache.DefaultVernWs, "zzword");
            var msa = services.GetInstance<IMoStemMsaFactory>().Create();
            item.MorphoSyntaxAnalysesOC.Add(msa);
            msa.PartOfSpeechRA = pos;
            var sense = services.GetInstance<ILexSenseFactory>().Create();
            item.SensesOS.Add(sense);
            sense.MorphoSyntaxAnalysisRA = msa;
            sense.Gloss.set_String(cache.DefaultAnalWs, "zzgloss");
        });
        new FwDataProjectLoader().Save(cache);
        entry = made;
        return cache.ProjectId.Path;
    }

    private string EditedFile(string project, Guid entry)
    {
        using var cache = new FwDataProjectLoader().LoadCache(project);
        var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.LexiconEntryOverlay);
        var exporter = new ProjectRecordExporter(cache, schema);
        var read = RecordFileReader.Read(exporter.Export(entry).Text!, schema.Schema, exporter.CreateReadOptions());
        Assert.False(read.HasErrors);
        var sense = (RecordObject)((RecordList)read.Document!.Data["senses"]!).Items[0];
        ((RecordAlternatives)sense["gloss"]!).Add("en", new RecordScalar("zzedited"));
        var path = Path.Combine(_root, "entry.fw.md");
        File.WriteAllText(path, RecordFileWriter.Write(read.Document, schema.Schema));
        return path;
    }

    private bool CapturedAsImport(string project, string draftName)
    {
        var baseline = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project), _root);
        Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
        var locator = new ProjectLocator(project, Path.GetFileNameWithoutExtension(project));
        using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(locator), locator,
            MotifSchema.CurrentSchema, new Version(1, 0));
        var id = new ProposalRepository(database).GetDraft(draftName).ProposalId.Value;
        var frozen = JobCommands.FreezeTrialProposal(new EnqueueTrialRequest(project, Version, id));
        Assert.True(frozen.Succeeded, frozen.Refusal?.Message);
        var words = JobCommands.ResolveTrialWords(frozen.Value!);
        Assert.True(words.Succeeded, words.Refusal?.Message);
        var captured = ReviewTrialCapture.Capture(frozen.Value!, words.Value, null);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        return ReviewInputArtifacts.Read(database, captured.Value!.ReviewInputs!).Inputs.RecordImport;
    }

    [Fact]
    public void AProposalTheImportWroteWholeIsCapturedAsExemptAndOneWithAHandAuthoredOperationIsNot()
    {
        var project = SeededProject(out var entry);
        var staged = RecordImportCommand.Run(new RecordImportRequest(project, EditedFile(project, entry), "imported"));
        Assert.True(staged.Succeeded, staged.Refusal?.Message);

        Assert.True(CapturedAsImport(project, "imported"));

        var mixed = ProposalCommands.ComposeBatch(new(project, Version, "imported",
            """{"intents":[{"verb":"compose-author-phoneme","intent":{"name":"a","representations":["a"]}}]}"""));
        Assert.True(mixed.Succeeded, mixed.Refusal?.Message);
        Assert.False(CapturedAsImport(project, "imported"));
    }

    [Fact]
    public void NoVerbOrBatchCanNameTheImportComposerAndAnOrdinaryDraftIsNeverExempt()
    {
        var project = SeededProject(out _);
        Assert.True(ProposalCommands.New(new(project, Version, "plain", "Plain")).Succeeded);

        var forged = ProposalCommands.ComposeBatch(new(project, Version, "plain",
            """{"intents":[{"verb":"RecordImport","intent":{"recordFile":"x"}}]}"""));
        Assert.False(forged.Succeeded);
        Assert.Contains("not a verb a batch can run", forged.Refusal!.Message);
        Assert.Contains("discarded", forged.Refusal.Message);

        Assert.True(ProposalCommands.New(new(project, Version, "authored", "Authored")).Succeeded);
        var authored = ProposalCommands.ComposeBatch(new(project, Version, "authored",
            """{"intents":[{"verb":"compose-author-phoneme","intent":{"name":"a","representations":["a"]}}]}"""));
        Assert.True(authored.Succeeded, authored.Refusal?.Message);
        Assert.False(CapturedAsImport(project, "authored"));
    }

    [Fact]
    public void AnEditThatRewritesAnImportedOperationInPlaceMakesTheProposalMixed()
    {
        var project = SeededProject(out var entry);
        Assert.True(RecordImportCommand.Run(new RecordImportRequest(project, EditedFile(project, entry), "imported")).Succeeded);
        var locator = new ProjectLocator(project, Path.GetFileNameWithoutExtension(project));
        string sense;
        using (var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(locator), locator,
            MotifSchema.CurrentSchema, new Version(1, 0)))
        {
            var draft = System.Text.Json.Nodes.JsonNode.Parse(new ProposalRepository(database).GetDraft("imported").ProposalJson!)!;
            sense = draft["operations"]!.AsArray()
                .First(operation => operation!["kind"]!.GetValue<string>() == "lexical/lexSense/setGloss")!["target"]!.GetValue<string>();
        }
        Assert.True(CapturedAsImport(project, "imported"));

        var edited = ProposalCommands.AddSetGloss(new(project, Version, "imported", sense, "en", "hand written"));

        Assert.True(edited.Succeeded, edited.Refusal?.Message);
        Assert.True(edited.Value!.ReplacedPriorValue);
        Assert.False(CapturedAsImport(project, "imported"));
    }

    [Fact]
    public void ASplitImportKeepsTheExemptionInItsParts()
    {
        var project = SeededProject(out var entry);
        Assert.True(RecordImportCommand.Run(new RecordImportRequest(project, EditedFile(project, entry), "imported")).Succeeded);
        var locator = new ProjectLocator(project, Path.GetFileNameWithoutExtension(project));
        string id;
        string[] operations;
        using (var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(locator), locator,
            MotifSchema.CurrentSchema, new Version(1, 0)))
        {
            var repository = new ProposalRepository(database);
            var record = repository.GetDraft("imported");
            id = record.ProposalId.Value;
            operations = System.Text.Json.Nodes.JsonNode.Parse(record.ProposalJson!)!["operations"]!.AsArray()
                .Select(operation => operation!["operationId"]!.GetValue<string>()).ToArray();
            Assert.True(repository.SetRationale(record.ProposalId, "Updates one imported gloss."));
        }
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(project, Version, "imported"));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);

        var split = ProposalCommands.Split(new SplitRequest(project, Version, id, [new SplitGroup("part", operations)], Force: false));

        Assert.True(split.Succeeded, split.Refusal?.Message);
        Assert.True(CapturedAsImport(project, "part"));
    }
}
