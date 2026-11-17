using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Threading;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class FieldWorksSimulatorWalkthroughTests(PristineProjectFixture pristine)
{
    [Fact]
    public void AnalyzeTextsActionUpdatesTheWordStripBeforeReviewAndApply()
    {
        using var project = new WalkthroughProject(pristine);
        PrepareUnparsedSentence(project);
        var parserPath = FakeParser.CopyRecordingInvocations(project.ManagedRoot);
        var parserStartedPath = Path.Combine(project.ManagedRoot, "analysis-parser-started");
        var releaseParserPath = Path.Combine(project.ManagedRoot, "analysis-parser-release");
        var deadline = WalkthroughSteps.DeadlineIn(360);

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var walkthrough = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: parserPath);
            WalkthroughSteps.ChooseProjectAndCaptureBaseline(walkthrough, deadline);
            walkthrough.Check(SeededProject.TextTitle);
            ConfigureFakeReading(project, parserPath, parserStartedPath, releaseParserPath);
            Assert.Contains(project.Text.TextId, walkthrough.Workspace.Selection.ChosenTextIds);
            WalkthroughSteps.StartAssessmentOverPastedWords(walkthrough, deadline);
            try
            {
                walkthrough.WaitUntil(() => File.Exists(parserStartedPath), StepFor(deadline),
                    "the Assessment did not reach the held parser");
                Assert.Equal(RunState.Running, walkthrough.Workspace.Assess.State);
                Assert.False(File.Exists(releaseParserPath));
            }
            finally
            {
                File.WriteAllText(releaseParserPath, string.Empty);
            }
            walkthrough.WaitUntil(() => walkthrough.Workspace.Assess.State == RunState.Completed &&
                    walkthrough.Workspace.Context.EvidencePublication.IsCompleted,
                WalkthroughSteps.Remaining(deadline), "the Assessment did not finish after releasing the parser");

            walkthrough.ShowPage(WorkspacePage.Texts);
            walkthrough.ShowTextsTab(TextsTab.AnalyzeTexts);
            var inText = walkthrough.Workspace.PageModel<TextsPageModel>().ResultsInText;
            walkthrough.WaitUntil(() => AnalyzeTextsLayoutTests.RealizedLines(inText)
                    .SelectMany(line => line.Tokens).Any(token => token.Form == SeededProject.FirstForm),
                WalkthroughSteps.Remaining(deadline), "Analyze texts did not load the seeded sentence");
            var token = AnalyzeTextsLayoutTests.RealizedLines(inText).SelectMany(line => line.Tokens)
                .Single(candidate => candidate.Form == SeededProject.FirstForm);
            var expectedAnchor = new OccurrenceAnchor(project.Text.TextId, project.Text.FirstParagraphId,
                project.Text.FirstSegmentId, 0);
            Assert.Equal(expectedAnchor, token.Occurrence);
            Assert.True(token.Readings.Count > 0,
                string.Join("; ", walkthrough.Workspace.Assess.Result?.Words.Select(word =>
                    $"{word.Word}: {word.Outcome}, {word.Morphology?.Analyses.Count ?? 0} analyses") ?? []));
            var strip = walkthrough.Window.GetLogicalDescendants().OfType<Border>()
                .Single(control => control.Name == "WordStrip" && SIL.Motif.App.Views.ResultsInTextPanel.TokenOf(control)?.Occurrence == token.Occurrence);
            Assert.IsType<WordStripToken>(strip).FocusWord();
            PageScreenshots.Settle(walkthrough.Window);
            var chip = strip.GetLogicalDescendants().OfType<Button>().Single(button =>
                Equals(Avalonia.Automation.AutomationProperties.GetName(button), WordDispositionButtons.ReadingsButtonName));
            Assert.Null(chip.Flyout);
            HeadlessClick.Click(walkthrough.Window, chip, WordDispositionButtons.ReadingsButtonName);
            PageScreenshots.Settle(walkthrough.Window);
            var panel = Assert.IsType<SIL.Motif.App.ViewModels.WordOccurrencesViewModel>(inText.OccurrencePanel);
            Assert.Equal(SeededProject.FirstForm, panel.Form);
            Assert.Equal(expectedAnchor, inText.SelectedToken?.Occurrence);
            walkthrough.WaitUntil(() => panel.TotalCount > 0 && panel.Page.Count > 0,
                WalkthroughSteps.Remaining(deadline), "the word panel did not list the word's places");
            panel.ChosenReading ??= panel.Readings[0];
            panel.CheckShownGaps();
            PageScreenshots.Settle(walkthrough.Window);
            var approve = walkthrough.Window.GetVisualDescendants().OfType<Button>().Single(button =>
                button.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetAutomationId(button) ==
                    SIL.Motif.App.AutomationIds.WordOccurrencesApprove);
            HeadlessClick.Click(walkthrough.Window, approve, panel.ApproveLabel);
            walkthrough.WaitUntil(() => walkthrough.Workspace.Context.Changes.Items.Count == 1,
                WalkthroughSteps.Remaining(deadline), "the word panel's Approve did not become pending");
            Assert.NotEmpty(FakeParser.Invocations(parserPath));
            Assert.Equal(SeededProject.FirstForm,
                walkthrough.Workspace.Context.Changes.Items.Single().Word);
            Assert.Equal(expectedAnchor, ReadOccurrenceAnchor(project.FwDataPath));

            walkthrough.ShowPage(WorkspacePage.Proposals);
            var context = walkthrough.Workspace.Context;
            Assert.Single(context.Changes.Items);
            await context.PendingMeasurement.MeasureCommand.ExecuteAsync(null);
            Assert.True(context.ProposalApply.CanApply(null, null, null, null));
            await context.ProposalApply.ApplyCurrentAsync(null);
            Assert.NotNull(context.ProposalApply.Receipt);
            Assert.Empty(context.Changes.Items);
        }, WalkthroughSteps.Remaining(deadline));
    }

    private static void ConfigureFakeReading(WalkthroughProject project, string parserPath,
        string parserStartedPath, string releaseParserPath)
    {
        var reading = new
        {
            morphs = new[]
            {
                new
                {
                    form = project.Seed.FirstLexemeFormId.ToString("D"),
                    msa = project.FirstMsaId.ToString("D"),
                    inflType = (string?)null,
                    guessedString = (string?)null,
                },
            },
        };
        FakeParser.BehaveBesideExecutable(parserPath, new
        {
            subcommands = new Dictionary<string, object>
            {
                ["batch"] = new
                {
                    startedPath = parserStartedPath,
                    holdUntilPath = releaseParserPath,
                    words = new object[]
                    {
                        new
                        {
                            word = SeededProject.FirstForm,
                            outcome = "complete",
                            signature = "authored-reading",
                            analyses = new[] { reading },
                        },
                        new { word = SeededProject.SecondForm, outcome = "no-analysis", signature = "-" },
                    },
                },
            },
        });
    }

    private static OccurrenceAnchor ReadOccurrenceAnchor(string fwDataPath)
    {
        using var database = ProjectMotifDatabase.Open(fwDataPath);
        var draft = new ProposalRepository(database).GetDraft("pending-changes");
        using var document = JsonDocument.Parse(draft!.ProposalJson!);
        var occurrence = document.RootElement.GetProperty("operations")[0]
            .GetProperty("extensions").GetProperty("changeFit").GetProperty("occurrence").GetProperty("anchor");
        return JsonSerializer.Deserialize<OccurrenceAnchor>(occurrence.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private static void PrepareUnparsedSentence(WalkthroughProject project) =>
        new FieldWorksSimulator(project.FwDataPath).SaveEdit(cache =>
        {
            var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaRepository>()
                .GetObject(project.Text.FirstParagraphId);
            var segment = cache.ServiceLocator.GetInstance<ISegmentRepository>()
                .GetObject(project.Text.FirstSegmentId);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                while (segment.AnalysesRS.Count > 0) segment.AnalysesRS.RemoveAt(0);
                segment.AnalysesRS.Add(cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString(SeededProject.FirstForm, cache.DefaultVernWs)));
                segment.AnalysesRS.Add(cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString(SeededProject.SecondForm, cache.DefaultVernWs)));
                var punctuation = cache.ServiceLocator.GetInstance<IPunctuationFormFactory>().Create();
                punctuation.Form = TsStringUtils.MakeString(".", cache.DefaultVernWs);
                segment.AnalysesRS.Add(punctuation);
                paragraph.Contents = TsStringUtils.MakeString(
                    $"{SeededProject.FirstForm} {SeededProject.SecondForm}.", cache.DefaultVernWs);
                paragraph.ParseIsCurrent = true;
            });
        });

    [Fact]
    public void FieldWorksHoldingTheProjectShowsHeldAndBlocksApply()
    {
        using var project = new WalkthroughProject(pristine);
        PendingChangeFixture.AddIncorrectSpelling(
            project.FwDataPath, project.ManagedRoot, "held-change-word");
        using var held = new FieldWorksSimulator(project.FwDataPath).Hold();
        var deadline = WalkthroughSteps.DeadlineIn(60);

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath);
            walkthrough.Show();
            walkthrough.ChooseNewProject();
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Baseline.FieldWorksHeldProject &&
                    walkthrough.Workspace.Selection.Texts.Count == 1 &&
                    walkthrough.Workspace.Context.Setup?.IsOpen == true,
                StepFor(deadline), "the window did not show the held project and its setup");
            walkthrough.SkipSetup();
            walkthrough.ShowPage(WorkspacePage.Proposals);
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Context.Changes.Items.Count == 1,
                StepFor(deadline), "the pending change did not appear in Review");

            var apply = walkthrough.Workspace.Context.ProposalApply;
            Assert.False(apply.CanApply(null, null, null, null));
            Assert.Contains(apply.Blockers(null, null, null, null),
                blocker => blocker.Kind == ApplyBlockerKind.FieldWorksHoldsProject);
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void ReleasingFieldWorksAllowsApplyAndShowsTheReceipt()
    {
        using var project = new WalkthroughProject(pristine);
        PendingChangeFixture.AddIncorrectSpelling(
            project.FwDataPath, project.ManagedRoot, "held-retry-word");
        using var held = new FieldWorksSimulator(project.FwDataPath).Hold();
        var deadline = WalkthroughSteps.DeadlineIn(60);
        var parser = FakeParser.Copy(project.ManagedRoot);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var walkthrough = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: parser);
            walkthrough.Show();
            walkthrough.ChooseNewProject();
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Baseline.FieldWorksHeldProject &&
                    walkthrough.Workspace.Selection.Texts.Count == 1 &&
                    walkthrough.Workspace.Context.Setup?.IsOpen == true,
                StepFor(deadline), "the window did not show the held project and its setup");
            walkthrough.SkipSetup();

            walkthrough.ShowPage(WorkspacePage.Proposals);
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Context.Changes.Items.Count == 1,
                StepFor(deadline), "the pending change did not appear in Review");

            var context = walkthrough.Workspace.Context;
            await context.PendingMeasurement.MeasureCommand.ExecuteAsync(null);
            Assert.Null(context.PendingMeasurement.Refusal);
            Assert.False(context.ProposalApply.CanApply(null, null, null, null));

            held.Dispose();
            walkthrough.Window.Hide();
            walkthrough.Window.Show();
            walkthrough.Window.Activate();
            Dispatcher.UIThread.RunJobs();
            var freshnessCheck = walkthrough.Workspace.CheckFreshnessAsync();
            var concurrentFreshnessCheck = walkthrough.Workspace.CheckFreshnessAsync();
            var checksShareTask = ReferenceEquals(freshnessCheck, concurrentFreshnessCheck);
            await Task.WhenAll(freshnessCheck, concurrentFreshnessCheck);
            walkthrough.WaitUntil(
                () => !walkthrough.Workspace.Baseline.FieldWorksHeldProject,
                StepFor(deadline), "the window did not clear the held-project status after release");
            Assert.True(checksShareTask, "Concurrent activation checks should share one freshness read.");
            await context.PendingMeasurement.MeasureCommand.ExecuteAsync(null);
            Assert.Null(context.PendingMeasurement.Refusal);
            Assert.True(context.ProposalApply.CanApply(null, null, null, null));
            await context.ProposalApply.ApplyCurrentAsync(null);
            Assert.Null(context.ProposalApply.ApplyRefusal);
            Assert.Empty(context.Changes.Items);
            Assert.NotNull(context.ProposalApply.Receipt);
        }, TimeSpan.FromSeconds(60));
    }

    private static TimeSpan StepFor(long deadline) => TimeSpan.FromTicks(Math.Min(
        WalkthroughSteps.Remaining(deadline).Ticks, TimeSpan.FromSeconds(30).Ticks));

}
