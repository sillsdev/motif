using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
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
        ["Conclusions", "Discussion", "ExternalMaterials", "FurtherQuestions", "Hypothesis", "PersonalNotes", "ResearchPlan", "VersionHistory"];

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
        "VersionHistory" => record.VersionHistoryOA,
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
