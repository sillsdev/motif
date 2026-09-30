using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.App.ViewModels;
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
        var deadline = Stopwatch.GetTimestamp() + 360 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
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
                walkthrough.WaitUntil(() => File.Exists(parserStartedPath), TimeSpan.FromSeconds(5),
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
            walkthrough.WaitUntil(() => inText.Texts.SelectMany(text => text.Lines)
                    .SelectMany(line => line.Tokens).Any(token => token.Form == SeededProject.FirstForm),
                WalkthroughSteps.Remaining(deadline), "Analyze texts did not load the seeded sentence");
            var token = inText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
                .Single(candidate => candidate.Form == SeededProject.FirstForm);
            var expectedAnchor = new OccurrenceAnchor(project.Text.TextId, project.Text.FirstParagraphId,
                project.Text.FirstSegmentId, 0);
            Assert.Equal(expectedAnchor, token.Occurrence);
            Assert.True(token.Readings.Count > 0,
                string.Join("; ", walkthrough.Workspace.Assess.Result?.Words.Select(word =>
                    $"{word.Word}: {word.Outcome}, {word.Morphology?.Analyses.Count ?? 0} analyses") ?? []));
            var strip = walkthrough.Window.GetLogicalDescendants().OfType<Border>()
                .Single(control => control.Name == "WordStrip" && ReferenceEquals(control.Tag, token));
            var fixMenu = strip.GetLogicalDescendants().OfType<Expander>().Single();
            HeadlessClick.Click(walkthrough.Window, fixMenu, "Fix actions from the word strip");
            Assert.True(fixMenu.IsExpanded);
            var approveChoice = strip.GetLogicalDescendants().OfType<Button>().Single(button =>
                Equals(Avalonia.Automation.AutomationProperties.GetName(button), "Add as Approved"));
            Assert.Null(inText.SelectedToken);
            Assert.Same(token.StageMarkingChoiceForTokenCommand, approveChoice.Command);
            Assert.True(approveChoice.Command?.CanExecute(approveChoice.CommandParameter),
                $"Selected token: {inText.SelectedToken?.Form}; parameter: {approveChoice.CommandParameter}; " +
                $"choices: {string.Join(", ", token.Marking.FixChoices.Select(choice => choice.Label))}");
            Assert.False(token.IsCardOpen, "Opening Fix actions also opened the word comparison card.");
            walkthrough.Click("Add as Approved");
            walkthrough.WaitUntil(() => walkthrough.Workspace.Context.Changes.Items.Count == 1 && token.IsPending &&
                    token.Marking.StagedTransitions.Any(transition =>
                        transition.Text == "Not in FieldWorks → Approved"),
                WalkthroughSteps.Remaining(deadline), "the Analyze texts action did not become pending");
            Assert.False(token.IsCardOpen);
            Assert.NotEmpty(FakeParser.Invocations(parserPath));
            Assert.True(token.IsPending);
            Assert.Contains(token.StagedChanges,
                change => change.Transition == "Not in FieldWorks → Approved");
            Assert.Equal(SeededProject.FirstForm,
                walkthrough.Workspace.Context.Changes.Items.Single().Word);
            Assert.Equal(expectedAnchor, ReadOccurrenceAnchor(project.FwDataPath));

            fixMenu.IsExpanded = false;
            Dispatcher.UIThread.RunJobs();
            walkthrough.Window.UpdateLayout();
            Assert.False(fixMenu.IsExpanded);

            walkthrough.ShowPage(WorkspacePage.Review);
            var review = walkthrough.Workspace.PageModel<ReviewPageModel>();
            Assert.Single(review.Changes.Items);
            var checkNumbers = walkthrough.Find<Button>("Check what applying does to the numbers");
            Assert.True(checkNumbers.IsEffectivelyVisible);
            Assert.True(checkNumbers.IsEffectivelyEnabled);
            walkthrough.Click("Check what applying does to the numbers");
            walkthrough.WaitUntil(() => !review.IsMeasuring && review.ApplyCommand.CanExecute(null),
                WalkthroughSteps.Remaining(deadline), "checking the confirmed change did not enable Apply");
            walkthrough.Click("Apply to FieldWorks project");
            walkthrough.WaitUntil(() => review.HasReceipt && review.Changes.Items.Count == 0,
                WalkthroughSteps.Remaining(deadline), "Apply did not finish after the uncertain decision was confirmed");
            return Task.CompletedTask;
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
        var deadline = Stopwatch.GetTimestamp() + 60 * Stopwatch.Frequency;

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
            walkthrough.ShowPage(WorkspacePage.Review);
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Context.Changes.Items.Count == 1,
                StepFor(deadline), "the pending change did not appear in Review");

            var review = walkthrough.Workspace.PageModel<ReviewPageModel>();
            Assert.False(review.CanApply);
            Assert.False(review.ApplyCommand.CanExecute(null));
            Assert.False(string.IsNullOrWhiteSpace(review.ApplyBlockReason));
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
        var deadline = Stopwatch.GetTimestamp() + 60 * Stopwatch.Frequency;
        var parser = FakeParser.Copy(project.ManagedRoot);
        var prompt = "See what applying does to the numbers.";
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
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

            walkthrough.ShowPage(WorkspacePage.Review);
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Context.Changes.Items.Count == 1,
                StepFor(deadline), "the pending change did not appear in Review");

            var review = walkthrough.Workspace.PageModel<ReviewPageModel>();
            var applyButton = walkthrough.Find<Button>("Apply to FieldWorks project");
            var checkNumbers = walkthrough.Find<Button>("Check what applying does to the numbers");
            Assert.True(checkNumbers.IsEffectivelyVisible);
            Assert.True(checkNumbers.IsEffectivelyEnabled);
            walkthrough.Click("Check what applying does to the numbers");
            Assert.False(walkthrough.Workspace.Context.Setup?.IsOpen);
            walkthrough.WaitUntil(
                () => !review.IsMeasuring && review.NumbersText != prompt,
                StepFor(deadline), "the pending change was not checked");
            Assert.Null(review.MeasurementRefusal);
            Assert.False(applyButton.IsEffectivelyEnabled);
            Assert.False(string.IsNullOrWhiteSpace(review.ApplyBlockReason));

            held.Dispose();
            walkthrough.Window.Hide();
            walkthrough.Window.Show();
            walkthrough.Window.Activate();
            Dispatcher.UIThread.RunJobs();
            walkthrough.WaitUntil(
                () => !walkthrough.Workspace.Baseline.FieldWorksHeldProject,
                StepFor(deadline), "the window did not clear the held-project status after release");
            var releasedCheckNumbers = walkthrough.Find<Button>("Check what applying does to the numbers");
            Avalonia.Rect? priorBounds = null;
            var stableLayouts = 0;
            walkthrough.WaitUntil(() =>
            {
                if (walkthrough.Workspace.Context.Setup?.IsOpen == true || walkthrough.SetupDialogIsShown ||
                    !releasedCheckNumbers.IsEffectivelyVisible || !releasedCheckNumbers.IsEffectivelyEnabled)
                {
                    stableLayouts = 0;
                    return false;
                }

                if (priorBounds == releasedCheckNumbers.Bounds)
                    stableLayouts++;
                else
                {
                    priorBounds = releasedCheckNumbers.Bounds;
                    stableLayouts = 1;
                }

                return stableLayouts >= 3;
            }, StepFor(deadline), "the Review action did not settle after FieldWorks was released");
            walkthrough.Click("Check what applying does to the numbers");
            Assert.True(review.IsMeasuring);
            walkthrough.WaitUntil(
                () => !review.IsMeasuring && review.ApplyCommand.CanExecute(null),
                StepFor(deadline), "the released project changes were not checked again");
            Assert.Null(review.MeasurementRefusal);
            Assert.True(review.ApplyCommand.CanExecute(null), review.ApplyBlockReason);
            Assert.True(applyButton.IsEffectivelyEnabled);
            walkthrough.Click("Apply to FieldWorks project");
            walkthrough.WaitUntil(
                () => review.HasReceipt && review.Changes.Items.Count == 0,
                StepFor(deadline), "Apply did not show its Receipt and clear the pending change");
            var receipt = walkthrough.Window.GetLogicalDescendants().OfType<CopyableTextBlock>()
                .Single(text => text.Text == review.ReceiptText);
            Assert.True(receipt.IsEffectivelyVisible);
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(60));
    }

    private static TimeSpan StepFor(long deadline) => TimeSpan.FromTicks(Math.Min(
        WalkthroughSteps.Remaining(deadline).Ticks, TimeSpan.FromSeconds(30).Ticks));
}
