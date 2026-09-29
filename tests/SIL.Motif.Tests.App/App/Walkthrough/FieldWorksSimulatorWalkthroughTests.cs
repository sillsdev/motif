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
    public void AnUncertainAnalyzeTextsDecisionCanBeReconfirmedAndAppliedThroughTheWindow()
    {
        using var project = new WalkthroughProject(pristine);
        PrepareUnparsedSentence(project);
        var parserPath = FakeParser.Copy(project.ManagedRoot);
        var deadline = Stopwatch.GetTimestamp() + 360 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: parserPath);
            WalkthroughSteps.ChooseProjectAndCaptureBaseline(walkthrough, deadline);
            ConfigureFakeReading(project, parserPath);
            walkthrough.Check(SeededProject.TextTitle);
            WalkthroughSteps.RunAssessmentOverPastedWords(walkthrough, deadline);

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
            inText.SelectToken(token);
            Assert.True(token.Readings.Count > 0,
                string.Join("; ", walkthrough.Workspace.Assess.Result?.Words.Select(word =>
                    $"{word.Word}: {word.Outcome}, {word.Morphology?.Analyses.Count ?? 0} analyses") ?? []));
            token.SelectedReading = token.Readings[0];
            walkthrough.Click("Approve the selected parser reading");
            walkthrough.WaitUntil(() => walkthrough.Workspace.Context.Changes.Items.Count == 1,
                WalkthroughSteps.Remaining(deadline), "the Analyze texts approval did not become pending");
            Assert.Equal(SeededProject.FirstForm,
                walkthrough.Workspace.Context.Changes.Items.Single().Word);
            Assert.Equal(expectedAnchor, ReadOccurrenceAnchor(project.FwDataPath));

            EditOtherWord(project, "changedword");
            walkthrough.Click("Refresh the project");
            walkthrough.WaitUntil(() => !walkthrough.Workspace.RefreshCommand.IsRunning,
                WalkthroughSteps.Remaining(deadline), "Refresh did not finish");
            var change = Assert.Single(walkthrough.Workspace.Context.Changes.Items);
            var refreshedWords = string.Join(" ", inText.Texts.SelectMany(text => text.Lines)
                .SelectMany(line => line.Tokens).Select(item => item.Text));
            Assert.True(change.IsUncertain,
                $"Expected an uncertain fit; status='{change.Fit?.Status}', " +
                $"reasons='{string.Join("; ", change.Fit?.Reasons ?? [])}', " +
                $"anchor='{ReadOccurrenceAnchor(project.FwDataPath)}', " +
                $"Analyze texts words='{refreshedWords}', " +
                $"fit summary='{string.Join("; ", walkthrough.Workspace.Context.Changes.Snapshot.FitSummary.Select(fit => fit.Status))}'.");
            walkthrough.ShowPage(WorkspacePage.Review);
            var review = walkthrough.Workspace.PageModel<ReviewPageModel>();
            Assert.False(review.ApplyCommand.CanExecute(null));
            Assert.Contains("sentence changed", review.ApplyBlockReason, StringComparison.Ordinal);
            Assert.Contains(walkthrough.Window.GetLogicalDescendants().OfType<TextBlock>(),
                text => text.Text == "Uncertain — check again");
            Assert.NotNull(walkthrough.Find<HyperlinkButton>($"Check again: {SeededProject.FirstForm}"));
            Assert.NotNull(walkthrough.Find<HyperlinkButton>($"Undo: {SeededProject.FirstForm}"));
            Assert.Contains(review.UncertainChanges.Single().AfterWords,
                word => word.Form == "changedword" && word.IsChanged);

            walkthrough.Click($"Check again: {SeededProject.FirstForm}");
            walkthrough.WaitUntil(() => !review.HasUncertainChanges &&
                    review.Changes.Items.Single().Fit?.Status == "fits",
                WalkthroughSteps.Remaining(deadline), "Check again did not clear uncertainty");
            walkthrough.Click("Check what applying does to the numbers");
            walkthrough.WaitUntil(() => !review.IsMeasuring && review.ApplyCommand.CanExecute(null),
                WalkthroughSteps.Remaining(deadline), "checking the confirmed change did not enable Apply");
            walkthrough.Click("Apply to FieldWorks project");
            walkthrough.WaitUntil(() => review.HasReceipt && review.Changes.Items.Count == 0,
                WalkthroughSteps.Remaining(deadline), "Apply did not finish after the uncertain decision was confirmed");
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }

    private static void ConfigureFakeReading(WalkthroughProject project, string parserPath)
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

    private static void EditOtherWord(WalkthroughProject project, string replacementForm) =>
        new FieldWorksSimulator(project.FwDataPath).SaveEdit(cache =>
        {
            var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaRepository>()
                .GetObject(project.Text.FirstParagraphId);
            var segment = cache.ServiceLocator.GetInstance<ISegmentRepository>()
                .GetObject(project.Text.FirstSegmentId);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                segment.AnalysesRS.RemoveAt(1);
                segment.AnalysesRS.Insert(1, cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString(replacementForm, cache.DefaultVernWs)));
                paragraph.Contents = TsStringUtils.MakeString(
                    $"{SeededProject.FirstForm} {replacementForm}.", cache.DefaultVernWs);
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

            walkthrough.ShowPage(WorkspacePage.Review);
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Context.Changes.Items.Count == 1,
                StepFor(deadline), "the pending change did not appear in Review");

            var review = walkthrough.Workspace.PageModel<ReviewPageModel>();
            var applyButton = walkthrough.Find<Button>("Apply to FieldWorks project");
            walkthrough.Click("Check what applying does to the numbers");
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
            var freshnessCheck = walkthrough.Workspace.CheckFreshnessAsync();
            var concurrentFreshnessCheck = walkthrough.Workspace.CheckFreshnessAsync();
            var checksShareTask = ReferenceEquals(freshnessCheck, concurrentFreshnessCheck);
            await Task.WhenAll(freshnessCheck, concurrentFreshnessCheck);
            walkthrough.WaitUntil(
                () => !walkthrough.Workspace.Baseline.FieldWorksHeldProject,
                StepFor(deadline), "the window did not clear the held-project status after release");
            Assert.True(checksShareTask, "Concurrent activation checks should share one freshness read.");
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
        }, TimeSpan.FromSeconds(60));
    }

    private static TimeSpan StepFor(long deadline) => TimeSpan.FromTicks(Math.Min(
        WalkthroughSteps.Remaining(deadline).Ticks, TimeSpan.FromSeconds(30).Ticks));
}
