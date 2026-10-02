using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class FirstRunSetupWalkthroughTests(PristineProjectFixture pristine)
{
    [Fact]
    public void FirstRunSetupAdvancesAndSavesTheSelection()
    {
        using var project = new TwoTextWalkthroughProject(pristine);
        var deadline = Stopwatch.GetTimestamp() + 180 * Stopwatch.Frequency;
        var parser = FakeParser.Copy(project.ManagedRoot);
        var heartbeat = Path.Combine(project.ManagedRoot, "first-run-heartbeat");
        var grammarStarted = Path.Combine(project.ManagedRoot, "grammar-health-started");
        var releaseGrammar = Path.Combine(project.ManagedRoot, "release-grammar-health");

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var walkthrough = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: parser);
            SetupWalkthroughActions.SelectProject(walkthrough, project.FwDataPath);
            SetupWalkthroughActions.CaptureBaselineAndWaitForSetup(
                walkthrough, 2, WalkthroughSteps.Remaining(deadline));
            var heldBehavior = new
            {
                subcommands = new Dictionary<string, object>
                {
                    ["grammar-health"] = new { startedPath = grammarStarted, holdUntilPath = releaseGrammar },
                    ["batch"] = new { heartbeatPath = heartbeat },
                },
            };
            walkthrough.SetFakeParserBehavior(heldBehavior);

            var setup = walkthrough.Workspace.Context.Setup!;
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: texts");
            Assert.Equal(1, setup.Step);
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Back");
            Assert.Equal(0, setup.Step);
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: texts");
            SetupWalkthroughActions.SetSetupTextChecked(walkthrough, SeededProject.TextTitle, true);
            SetupWalkthroughActions.SetSetupTextChecked(walkthrough, TwoTextWalkthroughProject.SecondTextTitle, true);
            SetupWalkthroughActions.SetSetupTextChecked(walkthrough, TwoTextWalkthroughProject.SecondTextTitle, false);
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: limits");
            SetupWalkthroughActions.TypeSetupLimit(walkthrough, "Parser step limit per word", "3100");
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: first run");
            var grammar = walkthrough.Workspace.PageModel<WarningsPageModel>().Grammar;
            var grammarCheck = walkthrough.Workspace.PageModel<WarningsPageModel>()
                .CheckGrammarCommand.ExecuteAsync(null);
            walkthrough.WaitUntil(() => grammar.IsLoading, TimeSpan.FromSeconds(30),
                "the held grammar check did not start before the first run");
            walkthrough.WaitUntil(() => File.Exists(grammarStarted), TimeSpan.FromSeconds(30),
                "the fake grammar check did not reach its hold");
            Assert.True(grammar.IsLoading);
            AssessmentStage? stageWhenSetupClosed = null;
            setup.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SetupViewModel.IsOpen) && !setup.IsOpen)
                    stageWhenSetupClosed = walkthrough.Workspace.Assess.Progress?.Stage;
            };
            walkthrough.Click("Start first run");
            Assert.True(grammar.IsLoading, "the first-run click should be queued behind the held grammar check");
            Assert.True(setup.IsOpen, "setup should stay open until the queued first run reports progress");
            File.WriteAllText(releaseGrammar, string.Empty);
            walkthrough.WaitUntil(() => File.Exists(heartbeat) ||
                walkthrough.Workspace.Assess.State is RunState.Completed or RunState.Cancelled or RunState.Refused,
                TimeSpan.FromSeconds(30), "the first run neither reached nor completed the fake parser");
            var parserInvocations = Directory.GetFiles(project.ManagedRoot, "_pangloss-argv.json", SearchOption.AllDirectories)
                .Select(path => $"{Path.GetDirectoryName(path)}: {File.ReadAllText(path)}");
            Assert.True(File.Exists(heartbeat),
                $"the first run did not reach the held fake parser; behavior='{Path.GetDirectoryName(parser)}', " +
                $"state='{walkthrough.Workspace.Assess.State}', refusal='{walkthrough.Workspace.Assess.ShownRefusal?.Sentence}', " +
                $"invocations='{string.Join(" | ", parserInvocations)}'");
            Assert.True(walkthrough.Workspace.ShowsParseAllWordsProgress);
            Assert.False(setup.IsOpen);
            Assert.Equal(AssessmentStage.Capturing, stageWhenSetupClosed);
            Assert.False(walkthrough.SetupDialogIsShown);
            Assert.Equal(RunState.Running, walkthrough.Workspace.Assess.State);
            Assert.Equal(WorkspacePage.Texts, walkthrough.Workspace.Context.CurrentPage);
            Assert.True(walkthrough.Find<ProgressBar>("Parse all words progress").IsEffectivelyVisible);
            walkthrough.Click("Cancel parsing all words");
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Assess.State == RunState.Cancelled,
                TimeSpan.FromSeconds(30), "the held first run did not cancel");
            Assert.False(setup.IsOpen);
            walkthrough.WaitUntil(() => !grammar.IsLoading, TimeSpan.FromSeconds(30),
                "the released grammar check did not finish");
            await grammarCheck;

            walkthrough.SetFakeParserBehavior(new
            {
                subcommands = new Dictionary<string, object>
                {
                    ["batch"] = new { words = new[] { new { word = "motifa", outcome = "complete" } } },
                },
            });
            SetupWalkthroughActions.ClickParseAllWordsFromTexts(walkthrough);
            walkthrough.WaitUntil(
                () => !setup.IsOpen && walkthrough.Workspace.Assess.State == RunState.Completed &&
                    walkthrough.Workspace.Context.EvidencePublication.IsCompleted,
                WalkthroughSteps.Remaining(deadline), "Finish did not close setup and complete its first run");

            var selectedId = Assert.Single(walkthrough.Workspace.Selection.ChosenTextIds);
            Assert.Equal($"1 text, step limit {3100:N0}", walkthrough.Workspace.Selection.SummaryText);
            walkthrough.ShowPage(WorkspacePage.Texts);
            walkthrough.ShowTextsTab(TextsTab.AnalyzeTexts);
            Assert.Contains(walkthrough.Window.GetLogicalDescendants().OfType<TextBlock>(), text =>
                text.Text == walkthrough.Workspace.Selection.SummaryText && text.IsVisible);
            Assert.Equal(1m, walkthrough.Workspace.Selection.PerWordTimeLimitSeconds);
            Assert.True(walkthrough.Find<Button>("Parse all words in the Selection").IsEffectivelyEnabled);

            var commands = Assert.IsType<CommandClient>(walkthrough.Workspace.Context.Commands);
            var stored = await commands.ReadDefaultSelectionAsync(
                new ReadDefaultSelectionRequest(project.FwDataPath), CancellationToken.None);
            Assert.True(stored.Succeeded, stored.Refusal?.Message);
            Assert.Equal(selectedId, Assert.Single(stored.Value!.Selection!.TextIds));
            Assert.Equal(1000, stored.Value.Selection.PerWordLimitMs);
            Assert.Equal(3100, stored.Value.Selection.PerWordStepLimit!.Steps);

            SetupWalkthroughActions.OpenConfigure(walkthrough);
            walkthrough.WaitUntil(
                () => setup.StepLimitEstimateText.Contains("parser statistics from the last parse", StringComparison.Ordinal),
                WalkthroughSteps.Remaining(deadline), "Configure did not load the parser rate from the completed run");
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: texts");
            Assert.True(walkthrough.Workspace.Context.Setup!.Selection.Texts
                .Single(text => text.Title == SeededProject.TextTitle).IsChecked);
            Assert.False(walkthrough.Workspace.Context.Setup.Selection.Texts
                .Single(text => text.Title == TwoTextWalkthroughProject.SecondTextTitle).IsChecked);
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: limits");
            Assert.Contains("At this limit a word takes up to about", setup.StepLimitEstimateText, StringComparison.Ordinal);
            Assert.Equal(3100m, walkthrough.Find<NumericUpDown>("Parser step limit per word").Value);
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: first run");
            walkthrough.Click("Use this Selection");
            walkthrough.WaitUntil(
                () => !setup.IsOpen,
                WalkthroughSteps.Remaining(deadline), "using the saved Selection did not close setup");
            return;
        }, WalkthroughSteps.Remaining(deadline));
    }

    [Fact]
    public void SkippingSetupWithoutASavedSelectionOffersConfigureInsteadOfParse()
    {
        using var project = new WalkthroughProject(pristine);
        var deadline = Stopwatch.GetTimestamp() + 180 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var parser = FakeParser.Copy(project.ManagedRoot);
            using var walkthrough = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: parser);
            WalkthroughSteps.ChooseProjectAndCaptureBaseline(walkthrough, deadline);

            Assert.True(walkthrough.Workspace.Context.NeedsAssessment);
            Assert.False(walkthrough.Workspace.Context.Setup!.CanRunDefaultSelection);
            var configureButtons = walkthrough.Window.GetLogicalDescendants().OfType<Button>()
                .Where(button => button.Content?.ToString() == "Choose what to parse" && button.IsEffectivelyVisible)
                .ToArray();
            Assert.NotEmpty(configureButtons);
            Assert.All(configureButtons, button =>
            {
                Assert.True(button.IsEffectivelyEnabled);
                Assert.Same(walkthrough.Workspace.ConfigureCommand, button.Command);
            });
            var topAction = walkthrough.Window.FindControl<Button>("ChooseWhatToParseButton");
            Assert.NotNull(topAction);
            Assert.True(topAction.IsEffectivelyVisible);
            Assert.True(topAction.IsEffectivelyEnabled);
            Assert.Same(walkthrough.Workspace.ConfigureCommand, topAction.Command);
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }

}
