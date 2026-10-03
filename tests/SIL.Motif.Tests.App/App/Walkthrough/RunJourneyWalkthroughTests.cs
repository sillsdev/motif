using System.Diagnostics;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class RunJourneyWalkthroughTests(PristineProjectFixture pristine)
{
    [Fact]
    public void LexiconOnlyProjectOpensIntoSetupAndRunsFromPastedWords()
    {
        using var project = new LexiconOnlyWalkthroughProject(pristine);
        var parser = FakeParser.Copy(project.ManagedRoot);
        var words = new[]
        {
            SeededProject.FirstForm,
            "chat", "chats", "lion", "lions", "lionne", "chien", "chiens", "soleil", "eau",
            "manger", "mangeons", "mangé", "pleuvoir", "pleut", "nuage", "nuages", "fenêtre",
            "fenêtres", "parler", "parlons", "courir", "trois", "maintenant", "chaton", "chatons",
            "arc-en-ciel", "froid", "froide", "très", "souris",
        };
        var deadline = Stopwatch.GetTimestamp() + 180 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using (var walkthrough = new WalkthroughWindow(
                       project.ManagedRoot, project.FwDataPath, parserPath: parser))
            {
                walkthrough.Show();
                walkthrough.OpenProjectMenu();
                walkthrough.ChooseNewProject();
                walkthrough.WaitUntil(
                    () => walkthrough.Workspace.Baseline.HasBaseline &&
                        walkthrough.Workspace.Context.Setup?.IsOpen == true &&
                        walkthrough.Workspace.Context.Setup.ConfigurationLoadTask?.IsCompleted != false,
                    WalkthroughSteps.Remaining(deadline), "the first open did not capture a Baseline and open setup");

                Assert.Null(walkthrough.Workspace.Baseline.ShownRefusal);
                Assert.False(walkthrough.Workspace.PageModel<OverviewPageModel>().SelectionIsUnresolved);
                Assert.Equal("This Baseline has no Texts.", walkthrough.Workspace.Selection.TextsEmptyMessage);
                Assert.Empty(walkthrough.Workspace.Selection.Texts);

                SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: texts");
                var pasted = string.Join(Environment.NewLine, words);
                walkthrough.Type("Words to add", pasted);
                var wordsBox = walkthrough.Find<TextBox>("Words to add");
                Assert.Equal(140, wordsBox.MaxHeight);
                Assert.True(wordsBox.Bounds.Height <= wordsBox.MaxHeight);
                Assert.Equal(ScrollBarVisibility.Auto, ScrollViewer.GetVerticalScrollBarVisibility(wordsBox));
                var next = SetupWalkthroughActions.FindSetupButton(walkthrough, "Next: limits");
                Assert.True(next.IsEffectivelyVisible);
                Assert.True(next.IsEffectivelyEnabled);

                SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: limits");
                SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: first run");
                walkthrough.SetFakeParserBehavior(new
                {
                    subcommands = new Dictionary<string, object>
                    {
                        ["batch"] = new { words = words.Select(word => new { word, outcome = "complete" }).ToArray() },
                    },
                });
                walkthrough.Click("Start first run");
                walkthrough.WaitUntil(
                    () => walkthrough.Workspace.Assess.State == RunState.Completed &&
                        walkthrough.Workspace.Context.EvidencePublication.IsCompleted,
                    WalkthroughSteps.Remaining(deadline), "the first Assessment did not finish");

                Assert.Equal(words.Length, walkthrough.Workspace.Selection.PastedWordEntries.Count);
                var assessment = Assert.IsType<AssessCommandResponse>(walkthrough.Workspace.Assess.Result);
                Assert.Contains(words[0], assessment.Words.Select(word => word.Word));
                var texts = walkthrough.Workspace.PageModel<TextsPageModel>();
                Assert.False(texts.ResultsInText.NeedsTexts);
                Assert.Equal("This Baseline has no Texts.", texts.ResultsInText.Message);
                walkthrough.ShowPage(WorkspacePage.Texts);
                walkthrough.ShowTextsTab(TextsTab.AnalyzeTexts);
                var chooseTexts = walkthrough.Window.GetLogicalDescendants().OfType<Button>().Single(button =>
                    AutomationProperties.GetName(button) == "Choose texts to analyze");
                Assert.False(chooseTexts.IsEffectivelyVisible);
                walkthrough.Workspace.Context.OpenWord(words[0]);
                walkthrough.WaitUntil(
                    () => texts.ShowTextReader && texts.ResultsInText.SelectedToken?.Form == words[0],
                    TimeSpan.FromSeconds(10), "opening a word without Texts did not show its result");
                Assert.Equal(words[0], texts.ResultsInText.SelectedToken?.Form);
            }

            using var reopened = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: parser);
            reopened.Show();
            reopened.WaitUntil(
                () => reopened.Workspace.PageModel<OverviewPageModel>().HasKnownProjects,
                TimeSpan.FromSeconds(10), "the Choose a project page did not load Known projects");
            var chooser = Assert.Single(reopened.Window.GetLogicalDescendants().OfType<OverviewPage>());
            Assert.True(chooser.Page.ShowChooseProject);
            var recent = chooser.GetLogicalDescendants().OfType<Button>().Single(button =>
                AutomationProperties.GetName(button) == "Open MotifTestProj");
            Assert.True(recent.Focusable);
            recent.Focus();
            Assert.True(recent.IsFocused);
            reopened.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            reopened.WaitUntil(
                () => reopened.Workspace.Context.ProjectPath == project.FwDataPath &&
                    reopened.Workspace.Baseline.HasBaseline &&
                    !reopened.Workspace.Context.IsOpeningProject,
                TimeSpan.FromSeconds(15), "the keyboard-accessible recent project did not reopen");
            Assert.False(chooser.Page.ShowChooseProject);
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }
}
