using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Records;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Runner.Apply;
using SIL.TextEditing.Project;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// The Notebook record's StText sections and a domain question's example sentences, edited in two writing systems with
/// a character-styled run, staged in the pending-changes Draft and applied to a project built at run time (ADR 0002),
/// then read back from the reopened project.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class RichTextFinishApplyTests(PristineProjectFixture pristine)
{
    private static readonly string[] NotebookSections =
        ["Conclusions", "Discussion", "ExternalMaterials", "FurtherQuestions", "Hypothesis", "PersonalNotes", "ResearchPlan"];

    private static readonly Guid RecordId = Guid.Parse("00000000-0000-4000-8000-0000000000a1");
    
    private static IStText? Section(IRnGenericRec record, string name) => name switch
    {
        "Conclusions" => record.ConclusionsOA,
        "Discussion" => record.DiscussionOA,
        "ExternalMaterials" => record.ExternalMaterialsOA,
        "FurtherQuestions" => record.FurtherQuestionsOA,
        "Hypothesis" => record.HypothesisOA,
        "PersonalNotes" => record.PersonalNotesOA,
        "ResearchPlan" => record.ResearchPlanOA,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private static (string Text, string Ws, string? Style)[] RunsOf(ITsString value, LcmCache cache) =>
        Enumerable.Range(0, value.RunCount).Select(index =>
        {
            var props = value.get_Properties(index);
            var ws = props.GetIntPropValues((int)FwTextPropType.ktptWs, out _);
            var style = props.GetStrPropValue((int)FwTextPropType.ktptNamedStyle);
            return (value.get_RunText(index), cache.WritingSystemFactory.GetStrFromWs(ws), string.IsNullOrEmpty(style) ? null : style);
        }).ToArray();

    [Fact]
    public void EveryNotebookSectionEditedInTwoWritingSystemsWithAStyledRunReadsBackAfterReopening()
    {
        string path;
        using (var cache = pristine.NewScratch())
        {
            SeededGdrs.Seed(cache);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var record = cache.ServiceLocator.GetInstance<IRnGenericRecFactory>().Create(RecordId);
                cache.LangProject.ResearchNotebookOA.RecordsOC.Add(record);
            });
            path = cache.ProjectId.Path;
            new FwDataProjectLoader().Save(cache);
        }
        Recaptured(path);

        foreach (var section in NotebookSections)
        {
            var paragraphs = new TextParagraphsPayload("RnGenericRec." + section, [null],
                [new TextParagraphChange(0, null, "Normal",
                    [new TextRun(section + " plain ", "en"), new TextRun("emphasised", "en", "Emphasis"), new TextRun("ar-" + section, SeededProject.RightToLeftTag)])],
                [], new Dictionary<string, TextParagraphExpected>());
            var staged = ProposalCommands.StageTextParagraphs(new StageTextParagraphsRequest(path, CanonicalId.FromGuid(RecordId).Value, paragraphs));
            Assert.True(staged.Succeeded, section + ": " + staged.Refusal?.Message);
        }
        Applied(path);

        using var reloaded = new FwDataProjectLoader().LoadCache(path);
        var after = reloaded.ServiceLocator.GetInstance<IRnGenericRecRepository>().GetObject(RecordId);
        foreach (var section in NotebookSections)
        {
            var paragraph = Assert.IsAssignableFrom<IStTxtPara>(Assert.Single(Section(after, section)!.ParagraphsOS));
            Assert.Equal([(section + " plain ", "en", null), ("emphasised", "en", "Emphasis"), ("ar-" + section, SeededProject.RightToLeftTag, null)],
                RunsOf(paragraph.Contents, reloaded));
        }
        Assert.Null(after.DescriptionOA);
    }

    [Fact]
    public void ADomainQuestionsExampleSentencesEditedInTwoWritingSystemsWithAStyledRunReadAfterReopening()
    {
        string path;
        Guid questionId = Guid.Empty;
        using (var cache = pristine.NewScratch())
        {
            SeededGdrs.Seed(cache);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                cache.LangProject.SemanticDomainListOA ??= cache.ServiceLocator.GetInstance<ICmPossibilityListFactory>().Create();
                var domain = cache.ServiceLocator.GetInstance<ICmSemanticDomainFactory>().Create();
                cache.LangProject.SemanticDomainListOA.PossibilitiesOS.Add(domain);
                var question = cache.ServiceLocator.GetInstance<ICmDomainQFactory>().Create();
                domain.QuestionsOS.Add(question);
                questionId = question.Guid;
            });
            path = cache.ProjectId.Path;
            new FwDataProjectLoader().Save(cache);
        }
        Recaptured(path);

        var target = CanonicalId.FromGuid(questionId).Value;
        foreach (var (ws, runs) in new (string, TextRun[])[]
                 {
                     ("en", [new TextRun("plain ", "en"), new TextRun("emphasised", "en", "Emphasis")]),
                     (SeededProject.RightToLeftTag, [new TextRun("ar-x", SeededProject.RightToLeftTag)]),
                 })
        {
            var staged = ProposalCommands.StageTextRichString(new StageTextRichStringRequest(path, target,
                new TextRichStringPayload("CmDomainQ.ExampleSentences", ws, runs, new("", TextRunsDigest.Of([])))));
            Assert.True(staged.Succeeded, ws + ": " + staged.Refusal?.Message);
        }
        Applied(path);

        using var reloaded = new FwDataProjectLoader().LoadCache(path);
        var question = reloaded.ServiceLocator.GetInstance<ICmDomainQRepository>().GetObject(questionId);
        var en = reloaded.WritingSystemFactory.GetWsFromStr("en");
        var ar = reloaded.WritingSystemFactory.GetWsFromStr(SeededProject.RightToLeftTag);
        Assert.Equal([("plain ", "en", null), ("emphasised", "en", "Emphasis")], RunsOf(question.ExampleSentences.get_String(en), reloaded));
        Assert.Equal([("ar-x", SeededProject.RightToLeftTag, null)], RunsOf(question.ExampleSentences.get_String(ar), reloaded));
    }

    [Fact]
    public void TwoEditsToOneSectionKeepOnlyTheLastAndTwoSectionsOfOneRecordBothSurvive()
    {
        var path = ProjectWithRecord();
        var record = CanonicalId.FromGuid(RecordId).Value;
        TextParagraphsPayload Edit(string section, string text) => new("RnGenericRec." + section, [null],
            [new TextParagraphChange(0, null, "Normal", [new TextRun(text, "en")])], [], new Dictionary<string, TextParagraphExpected>());

        Assert.True(ProposalCommands.StageTextParagraphs(new StageTextParagraphsRequest(path, record, Edit("Conclusions", "first"))).Succeeded);
        var third = ProposalCommands.StageTextParagraphs(new StageTextParagraphsRequest(path, record, Edit("Conclusions", "second")));
        Assert.True(third.Succeeded, third.Refusal?.Message);
        Assert.True(ProposalCommands.StageTextParagraphs(new StageTextParagraphsRequest(path, record, Edit("Hypothesis", "kept"))).Succeeded);
        Applied(path);

        using var reloaded = new FwDataProjectLoader().LoadCache(path);
        var after = reloaded.ServiceLocator.GetInstance<IRnGenericRecRepository>().GetObject(RecordId);
        Assert.Equal("second", Assert.IsAssignableFrom<IStTxtPara>(Assert.Single(after.ConclusionsOA!.ParagraphsOS)).Contents.Text);
        Assert.Equal("kept", Assert.IsAssignableFrom<IStTxtPara>(Assert.Single(after.HypothesisOA!.ParagraphsOS)).Contents.Text);
    }

    [Fact]
    public void ARecordFileWithTwoSectionsEditedStagesBothAppliesAndReadsBackWithOthersUntouched()
    {
        string path, exported;
        using (var cache = pristine.NewScratch())
        {
            SeededGdrs.Seed(cache);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var record = cache.ServiceLocator.GetInstance<IRnGenericRecFactory>().Create(RecordId);
                cache.LangProject.ResearchNotebookOA.RecordsOC.Add(record);
                void Section(Action<IStText> attach, params string[] texts)
                {
                    var stText = cache.ServiceLocator.GetInstance<IStTextFactory>().Create();
                    attach(stText);
                    foreach (var text in texts)
                    {
                        var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaFactory>().Create();
                        stText.ParagraphsOS.Add(paragraph);
                        paragraph.Contents = TsStringUtils.MakeString(text, cache.DefaultAnalWs);
                    }
                }
                Section(text => record.ConclusionsOA = text, "zzoldconclusion");
                Section(text => record.HypothesisOA = text, "zzoldhypothesis");
                Section(text => record.DiscussionOA = text, "zzkeepone", "zzkeeptwo");
            });
            path = cache.ProjectId.Path;
            new FwDataProjectLoader().Save(cache);
            var schema = ProjectRecordSchema.Generate(cache, ProjectRecordSchema.NotebookRecordOverlay);
            var exporter = new ProjectRecordExporter(cache, schema);
            exported = exporter.Export(RecordId).Text!;
            Assert.Contains("zzoldconclusion", exported);
            var edited = exported.Replace("zzoldconclusion", "zzconclusion [emph]{custom-style=Emphasis} [ar-x]{ws=ar}", StringComparison.Ordinal)
                .Replace("zzoldhypothesis", "zzhypothesis [emph]{custom-style=Emphasis} [ar-y]{ws=ar}", StringComparison.Ordinal);
            var plan = new ProjectRecordImporter(exporter).Plan(edited, new CatalogWritableFields(schema));
            Assert.Empty(plan.ChangeSet.Refusals);
            Assert.Equal(2, plan.ChangeSet.Changes.Count);
            Assert.True(ProposalCommands.New(new(path, "0.1.0", "readback", "Record import")).Succeeded);
            var staged = ProposalCommands.StageRecordImport(path, "readback", schema, plan);
            Assert.True(staged.Succeeded, staged.Refusal?.Message);
            Assert.Empty(staged.Value!.Lowering.Refusals);
            Assert.Equal(2, staged.Value.Lowering.Operations.Count(operation => operation.Kind == "text/setParagraphs"));
        }

        DraftRationale.Author(path, "readback", "Imported record", "The record file was edited and imported.");
        var finalized = ProposalCommands.Finalize(new(path, "0.1.0", "readback"));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        Assert.True(DryRunJobRunner.Run(path, "0.1.0", finalized.Value!.ProposalId).Succeeded);
        var applied = ProposalCommands.Apply(new(path, "0.1.0", finalized.Value.ProposalId, Force: true));
        Assert.True(applied.Succeeded, applied.Refusal?.Message);

        using var reloaded = new FwDataProjectLoader().LoadCache(path);
        var after = reloaded.ServiceLocator.GetInstance<IRnGenericRecRepository>().GetObject(RecordId);
        Assert.Equal([("zzconclusion ", "en", null), ("emph", "en", "Emphasis"), (" ", "en", null), ("ar-x", SeededProject.RightToLeftTag, null)],
            RunsOf(Assert.IsAssignableFrom<IStTxtPara>(Assert.Single(after.ConclusionsOA!.ParagraphsOS)).Contents, reloaded));
        Assert.Equal("ar-y", RunsOf(Assert.IsAssignableFrom<IStTxtPara>(Assert.Single(after.HypothesisOA!.ParagraphsOS)).Contents, reloaded)[^1].Text);
        Assert.Equal(["zzkeepone", "zzkeeptwo"], after.DiscussionOA!.ParagraphsOS.Cast<IStTxtPara>().Select(paragraph => paragraph.Contents.Text));
        var schemaAfter = ProjectRecordSchema.Generate(reloaded, ProjectRecordSchema.NotebookRecordOverlay);
        var importer = new ProjectRecordImporter(new ProjectRecordExporter(reloaded, schemaAfter));
        Assert.True(importer.Plan(new ProjectRecordExporter(reloaded, schemaAfter).Export(RecordId).Text!, new CatalogWritableFields(schemaAfter)).IsEmpty);
    }

    private string ProjectWithRecord()
    {
        using var cache = pristine.NewScratch();
        SeededGdrs.Seed(cache);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            cache.LangProject.ResearchNotebookOA.RecordsOC.Add(cache.ServiceLocator.GetInstance<IRnGenericRecFactory>().Create(RecordId)));
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        Recaptured(path);
        return path;
    }

    private static void Recaptured(string path) =>
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), RootFor(path)).Succeeded);

    private static void Applied(string path)
    {
        var runner = new InProcessRunnerLauncher(IsolatedRunner.Options(RootFor(path)));
        try
        {
            var applied = PendingChangesWorkflow.Apply(new ApplyPendingRequest(path, null, null),
                dryRunTimeout: TimeSpan.FromMinutes(2), runnerLauncher: runner);
            Assert.True(applied.Succeeded, applied.Refusal?.Code + ": " + applied.Refusal?.Message);
            Assert.True(applied.Value!.Applied, applied.Value.Summary);
        }
        finally
        {
            runner.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        Recaptured(path);
    }

    private static string RootFor(string projectPath)
    {
        var root = Path.Combine(Path.GetDirectoryName(projectPath)!, "rich-finish-root");
        Directory.CreateDirectory(root);
        return root;
    }
}
