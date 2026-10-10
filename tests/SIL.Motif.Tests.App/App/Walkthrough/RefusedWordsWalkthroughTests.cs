using System.Diagnostics;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Tests.Parser;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class RefusedWordsWalkthroughTests(PristineProjectFixture pristine)
{
    [RealParserFact]
    public void AddedWordsWithoutTextsKeepRefusalsReasonsAndEveryReading()
    {
        var root = Path.Combine(Path.GetTempPath(), "SIL.Motif.RefusedWordsWalkthrough", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string projectPath;
        using (var cache = pristine.NewScratch())
        {
            RealParserProject.PrepareForParsing(cache, "l", "i", "o", "n", "s", "a", "t");
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var services = cache.ServiceLocator;
                services.GetInstance<IMoFormRepository>().GetObject(pristine.Seed.FirstLexemeFormId)
                    .Form.set_String(cache.DefaultVernWs, "chat");
                services.GetInstance<IMoFormRepository>().GetObject(pristine.Seed.SecondLexemeFormId)
                    .Form.set_String(cache.DefaultVernWs, "lion");
                var entry = services.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.SecondEntryId);
                var category = services.GetInstance<IPartOfSpeechFactory>().Create();
                cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(category);
                category.Name.set_String(cache.DefaultAnalWs, "Adjective");
                category.Abbreviation.set_String(cache.DefaultAnalWs, "adj");
                var msa = services.GetInstance<IMoStemMsaFactory>().Create();
                entry.MorphoSyntaxAnalysesOC.Add(msa);
                msa.PartOfSpeechRA = category;
                var sense = services.GetInstance<ILexSenseFactory>().Create();
                entry.SensesOS.Add(sense);
                sense.MorphoSyntaxAnalysisRA = msa;
                sense.Gloss.set_String(cache.DefaultAnalWs, "second reading");
                cache.LangProject.MorphologicalDataOA.ParserParameters =
                    "<ParserParameters><HC><NoDefaultCompounding>false</NoDefaultCompounding><Strata /></HC></ParserParameters>";
            });
            Assert.Empty(cache.ServiceLocator.GetInstance<ITextRepository>().AllInstances());
            new FwDataProjectLoader().Save(cache);
            projectPath = cache.ProjectId.Path;
        }
        var deadline = Stopwatch.GetTimestamp() + 180 * Stopwatch.Frequency;
        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                using var walkthrough = new WalkthroughWindow(root, projectPath, Path.Combine(root, "handoff"));
                SetupWalkthroughActions.SelectProject(walkthrough, projectPath);
                SetupWalkthroughActions.CaptureBaselineAndWaitForSetup(walkthrough, 0, WalkthroughSteps.Remaining(deadline));
                SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: texts");
                walkthrough.Type("Words to add", "chat\nchats\nlion\nlions\nfenêtre");
                SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: limits");
                SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: first run");
                walkthrough.Click("Start first run");
                walkthrough.WaitUntil(() => walkthrough.Workspace.Assess.State == RunState.Completed &&
                    walkthrough.Workspace.Context.EvidencePublication.IsCompleted,
                    WalkthroughSteps.Remaining(deadline), "the added-word parse did not finish",
                    () => walkthrough.Workspace.Assess.Refusal?.Message ?? "No command refusal");

                var workspace = walkthrough.Workspace;
                Assert.Contains("3 refused", workspace.Assess.Result!.CompletionSummary);
                Assert.Contains("phonemes", workspace.Assess.Result.CompletionSummary);
                var overview = workspace.PageModel<OverviewPageModel>();
                walkthrough.WaitUntil(() => overview.Overview is not null, WalkthroughSteps.Remaining(deadline),
                    "the Overview did not load");
                Assert.Contains(overview.TextCoverageSegments, segment => segment.Mark == Mark.ParserRefusal && segment.Count == 3);
                var compare = workspace.Assess.Compare;
                compare.SelectCells([new TextsListCell(WordProjectStatus.NotPresent, CompareColumnKind.NoParse)]);
                Assert.Equal(4, compare.Words.Count);
                var refusedRows = compare.Words.Where(word => word.WordRow.HasRefusalReason).ToArray();
                Assert.Equal(3, refusedRows.Length);
                Assert.All(refusedRows, word => Assert.Contains("phonemes", word.WordRow.MeaningDetail));
                Assert.False(compare.CanRerun);
                var texts = workspace.PageModel<TextsPageModel>();
                workspace.CurrentPage = WorkspacePage.Texts;
                texts.Tab = TextsTab.Lists;
                texts.TextsLists.SelectedList = texts.TextsLists.Lists.Single(list => list.Name == "Nobody can analyze");
                Assert.False(texts.TextsLists.CanParseAgain);

                compare.SelectCells([new TextsListCell(WordProjectStatus.NotPresent, CompareColumnKind.NoMatch)]);
                texts.Tab = TextsTab.Matrix;
                PageScreenshots.Settle(walkthrough.Window);
                var lion = Assert.Single(compare.Words);
                Assert.Equal(2, lion.Readings.Count);
                Assert.Contains("2 analyses", lion.WordRow.ReadingCountText);

                workspace.CurrentPage = WorkspacePage.Timing;
                var timing = workspace.PageModel<TimingPageModel>();
                await timing.SelectWordSetCommand.ExecuteAsync("all");
                Assert.Equal(3, timing.KindTiming!.Words.Count(word => word.Completion == TimingCompletion.Refused));
                workspace.Context.OpenTiming(refusedRows.Select(word => word.Word).ToArray(), null);
                walkthrough.WaitUntil(() => timing.KindTiming?.Words.Count == 3 && !timing.IsLoadingTiming,
                    WalkthroughSteps.Remaining(deadline), "the refused-word timing selection did not load");
                Assert.False(timing.RerunWordsCommand.CanExecute(null));

                workspace.CurrentPage = WorkspacePage.AiHandoff;
                walkthrough.Click("Write the AI Handoff folder");
                var handoff = workspace.PageModel<AiHandoffPageModel>().Handoff;
                walkthrough.WaitUntil(() => handoff.State is RunState.Completed or RunState.Refused,
                    WalkthroughSteps.Remaining(deadline), "the Handoff did not finish");
                Assert.Equal(RunState.Completed, handoff.State);
                using var records = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "handoff", "parse-results.json")));
                Assert.Equal(3, records.RootElement.EnumerateArray().Count(word => word.GetProperty("outcome").GetString() == "refused"));
                var accented = records.RootElement.EnumerateArray().Single(word => word.GetProperty("word").GetString() == "fenêtre");
                Assert.Contains("phonemes", accented.GetProperty("refusal").GetProperty("reason").GetString());
                using var chosenTexts = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "handoff", "texts.json")));
                Assert.Empty(chosenTexts.RootElement.EnumerateArray());
                Assert.Contains("empty array", handoff.PastedHeader);

                workspace.CurrentPage = WorkspacePage.TryAWord;
                var page = workspace.PageModel<TryWordPageModel>();
                page.Trace.WordToTry = "chats";
                await page.Trace.TryCommand.ExecuteAsync(null);
                Assert.True(page.HasLastParseRefusal);
                Assert.Contains("phonemes", page.LastParseRefusalReason);
                Assert.Contains(ParserRefusals.Title, page.ResultOutcomeText);
                page.Trace.WordToTry = "lions";
                await page.Trace.TryCommand.ExecuteAsync(null);
                Assert.False(page.HasLastParseRefusal);
                Assert.True(page.Trace.HasNoParseReasons);
                Assert.Contains(page.Trace.NoParseReasons, reason => reason.Contains("'s'"));
                Assert.Contains("No affix-building step was recorded.", page.Trace.NoParseReasons);
            }, WalkthroughSteps.Remaining(deadline));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
