using System.Diagnostics;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Requests;
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
                // Spelt only in defined phonemes, so the c and h of the typed words occur nowhere in the grammar.
                services.GetInstance<IMoFormRepository>().GetObject(pristine.Seed.FirstLexemeFormId)
                    .Form.set_String(cache.DefaultVernWs, "lait");
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
        var deadline = WalkthroughSteps.DeadlineIn(180);
        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                using var walkthrough = new WalkthroughWindow(root, projectPath, Path.Combine(root, "handoff"));
                SetupWalkthroughActions.SelectProject(walkthrough, projectPath);
                SetupWalkthroughActions.CaptureBaselineAndWaitForSetup(walkthrough, 0, WalkthroughSteps.Remaining(deadline));
                SetupWalkthroughActions.ClickSetupButton(walkthrough, SIL.Motif.App.MotifAppStrings.SetupNextTexts);
                walkthrough.Type("Words to add", "chat\nchats\nlion\nlions\nfenêtre");
                SetupWalkthroughActions.ClickSetupButton(walkthrough, SIL.Motif.App.MotifAppStrings.SetupNextLimits);
                SetupWalkthroughActions.ClickSetupButton(walkthrough, SIL.Motif.App.MotifAppStrings.SetupNextFirstRun);
                walkthrough.Click(SIL.Motif.App.MotifAppStrings.StartFirstRun);
                walkthrough.WaitUntil(() => walkthrough.Workspace.Assess.State == RunState.Completed &&
                    walkthrough.Workspace.Context.EvidencePublication.IsCompleted,
                    WalkthroughSteps.Remaining(deadline), "the added-word parse did not finish",
                    () => walkthrough.Workspace.Assess.Refusal?.Message ?? "No command refusal");

                var workspace = walkthrough.Workspace;
                Assert.Contains("3 refused", workspace.Assess.Result!.CompletionSummary);
                Assert.Contains("phonemes", workspace.Assess.Result.CompletionSummary);
                walkthrough.WaitUntil(() => workspace.Context.Health.Overview is not null,
                    WalkthroughSteps.Remaining(deadline), "project health did not load");
                var rows = workspace.Assess.Words.AllRows;
                var refusedRows = rows.Where(word => word.WordRow.HasRefusalReason).ToArray();
                Assert.Equal(3, refusedRows.Length);
                Assert.All(refusedRows, word => Assert.Contains("phonemes", word.WordRow.MeaningDetail));

                var evidence = workspace.Context.Evidence;
                var timing = await workspace.Context.Commands.TimingAsync(new TimingRequest(projectPath,
                    evidence.ParseTimeAssessmentId, By: "kind", OverrideAssessmentIds: evidence.TimingOverrideAssessmentIds),
                    CancellationToken.None);
                Assert.True(timing.Succeeded, timing.Refusal?.Message);
                Assert.Equal(3, timing.Value!.Words.Count(word => word.Completion == TimingCompletion.Refused));

                walkthrough.ShowPage(WorkspacePage.Settings);
                walkthrough.Click("Write the AI Handoff folder");
                var handoff = workspace.PageModel<SettingsPageModel>().Handoff.Handoff;
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

                var page = workspace.Context.WordTraceSession;
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
