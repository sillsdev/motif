using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;
using WordRow = SIL.Motif.App.Views.WordRow;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Every other place a word is listed shows the same word row as the Matrix and Lists, with its three next steps:
/// Timing's slowest words and a rule's costliest words, Review changes, What changed and Analyze texts' Word list.
/// Try a Word on any of them opens Try a Word on that word.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WordRowEverywhereTests
{
    [Fact]
    public void TimingsSlowestWordsOpenTryAWordOnTheirWord() =>
        TryAWordOpensFrom("timing-words", "mwalimu", async (workspace, window) =>
        {
            workspace.CurrentPage = WorkspacePage.Timing;
            var timing = workspace.PageModel<TimingPageModel>();
            await Until(window, () => timing.SlowestWordRows.Count > 0, "Timing's slowest words");
        });

    [Fact]
    public void ARulesCostliestWordsOpenTryAWordOnTheirWord() =>
        TryAWordOpensFrom("timing-rule-words", "hawajafika", async (workspace, window) =>
        {
            workspace.CurrentPage = WorkspacePage.Timing;
            var timing = workspace.PageModel<TimingPageModel>();
            await Until(window, () => timing.RuleRows.Count > 0, "Timing's rules");
            if (timing.CostliestRuleWordRows.Count == 0)
                await timing.ChooseRuleCommand.ExecuteAsync(timing.RuleRows[0].Row);
            await Until(window, () => timing.CostliestRuleWordRows.Count > 0, "the chosen rule's costliest words");
        });

    [Fact]
    public void TheOverviewSlowestWordsOpenTheirTimingRowsInTryAWord() =>
        TryAWordOpensFrom("timing-words", "hawajafika", async (workspace, window) =>
        {
            workspace.CurrentPage = WorkspacePage.Overview;
            var overview = workspace.PageModel<OverviewPageModel>();
            await Until(window, () => overview.HasSlowestWordSummary, "the Overview's slowest-word summary");
            overview.OpenTimingCommand.Execute(null);
            await Until(window, () => workspace.CurrentPage == WorkspacePage.Timing &&
                workspace.PageModel<TimingPageModel>().SlowestWordRows.Count > 0, "Timing's slowest words");
        });

    [Fact]
    public void AChangeInReviewChangesOpensTryAWordOnItsWord() =>
        TryAWordOpensFrom("review", "chakula", async (workspace, window) =>
        {
            var texts = workspace.PageModel<TextsPageModel>();
            texts.Tab = TextsTab.AnalyzeTexts;
            workspace.CurrentPage = WorkspacePage.Texts;
            await Until(window, () => Token(texts, "chakula") is not null, "chakula in Analyze texts");
            var chakula = Token(texts, "chakula")!;
            var add = chakula.Marking.FixChoices.Single(choice => choice.Label == "Add as Approved");
            await chakula.StageMarkingChoiceForTokenCommand!.ExecuteAsync(add);
            await Until(window, () => workspace.Context.Changes.HasItems, "the staged change on chakula");
            workspace.CurrentPage = WorkspacePage.Review;
        });

    [Fact]
    public void AWordInWhatChangedOpensTryAWordOnItsWord() =>
        TryAWordOpensFrom("what-changed", "kitabu", async (workspace, window, client) =>
        {
            var first = workspace.Assess.Result!;
            // The second run loses kitabu's analysis, so kitabu moves from Kept to Lost.
            client.AssessCompletesWith(first with
            {
                Words = [.. first.Words.Select(word => word.Word != "kitabu" ? word : word with
                {
                    Outcome = "no-analysis",
                    Morphology = null,
                    Readings = [],
                    ReadingGrades = [],
                })],
            });
            await workspace.Assess.RunCommand.ExecuteAsync(null);
            var difference = workspace.Assess.Difference;
            await Until(window, () => difference.Moves.Count > 0, "a move between the two runs");
            difference.SelectedMove = difference.Moves.First(move => move.Words.Any(word => word.Word == "kitabu"));
            workspace.PageModel<TextsPageModel>().Tab = TextsTab.WhatChanged;
            workspace.CurrentPage = WorkspacePage.Texts;
        });

    [Fact]
    public void WordListKeepsControlsAndColumnsUsableAcrossSizes()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData();
            try
            {
                var texts = workspace.PageModel<TextsPageModel>();
                texts.Tab = TextsTab.AnalyzeTexts;
                texts.ShowAnalyzeViewCommand.Execute(AnalyzeTextsView.WordList);
                workspace.CurrentPage = WorkspacePage.Texts;
                await Until(window, () => texts.Words.Rows.Count > 0, "the Word list's words");
                PageScreenshots.Settle(window);
                var textWordsPanel = window.GetVisualDescendants().OfType<TextWordsPanel>().Single();
                var handoff = Assert.Single(textWordsPanel.GetVisualDescendants().OfType<Button>(),
                    button => ReferenceEquals(button.Command, texts.Words.HandOffCheckedWordsCommand));
                Assert.Equal("AI Handoff", handoff.Content);
                Assert.False(handoff.IsEffectivelyEnabled);
                Assert.Equal("Tick words first.", ToolTip.GetTip(handoff));
                texts.Words.Rows[0].IsChecked = true;
                Assert.Equal("AI Handoff for this word", handoff.Content);
                foreach (var (width, height) in new[] { (1040, 780), (1240, 780), (1040, 1000), (1240, 1000) })
                {
                    window.Width = width;
                    window.Height = height;
                    try { PageScreenshots.Settle(window); }
                    catch (InvalidOperationException exception)
                    {
                        throw new InvalidOperationException($"Word list at {width} × {height}: {exception.Message}", exception);
                    }
                    var panel = window.GetVisualDescendants().OfType<TextWordsPanel>().Single();
                    var summary = panel.GetVisualDescendants().OfType<CopyableTextBlock>()
                        .Single(block => block.Text == texts.Words.SummaryText);
                    var search = panel.GetVisualDescendants().OfType<TextBox>().Single();
                    var summaryRight = summary.TranslatePoint(new Point(summary.Bounds.Width, 0), panel)!.Value.X;
                    var searchLeft = search.TranslatePoint(default, panel)!.Value.X;
                    Assert.True(summaryRight <= searchLeft, "The status summary must leave room for Search words.");
                }
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void AWordInTheWordListOpensTryAWordOnItsWord() =>
        TryAWordOpensFrom("word-list", "kitabu", async (workspace, window) =>
        {
            var texts = workspace.PageModel<TextsPageModel>();
            texts.Tab = TextsTab.AnalyzeTexts;
            texts.ShowAnalyzeViewCommand.Execute(AnalyzeTextsView.WordList);
            workspace.CurrentPage = WorkspacePage.Texts;
            await Until(window, () => texts.Words.Rows.Any(row => row.Form == "kitabu"), "kitabu in the Word list");
        });

    private static void TryAWordOpensFrom(string list, string word,
        Func<WorkspaceShellViewModel, MainWindow, Task> reach) =>
        TryAWordOpensFrom(list, word, (workspace, window, _) => reach(workspace, window));

    private static void TryAWordOpensFrom(string list, string word,
        Func<WorkspaceShellViewModel, MainWindow, FakeCommandClient, Task> reach)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            FakeCommandClient? client = null;
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(configure: (fake, assessment) =>
            {
                OverviewTimingScreenshots.ReadOverviewAndTiming(fake, assessment);
                client = fake;
            });
            try
            {
                window.Width = 1240;
                window.Height = 1600;
                await reach(workspace, window, client!);
                var row = await RowIn(window, list, word);
                row.FocusRow();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var steps = row.GetVisualDescendants().OfType<Control>().First(part => part.Classes.Contains("wordRowNext"))
                    .GetVisualDescendants().OfType<Button>().ToArray();
                Assert.Equal(["Open in text", "Try a Word", "Word Analyses ↗"], steps.Select(step => step.Content as string));

                HeadlessClick.Click(window, steps[1], "Try a Word");

                Assert.Equal(WorkspacePage.TryAWord, workspace.CurrentPage);
                Assert.Equal(word, workspace.PageModel<TryWordPageModel>().Trace.WordToTry);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(1));
    }

    private static async Task<WordRow> RowIn(MainWindow window, string list, string word)
    {
        WordRow? Find() => window.GetVisualDescendants().OfType<WordRow>()
            .FirstOrDefault(row => row.IsEffectivelyVisible && row.List == list && row.Row?.Word == word);
        await Until(window, () => Find() is not null, $"{word}'s row in the {list} list");
        return Find()!;
    }

    private static ResultsTokenViewModel? Token(TextsPageModel texts, string form) =>
        texts.ResultsInText.VisibleLines.SelectMany(line => line.Tokens).FirstOrDefault(token => token.Form == form);

    private static async Task Until(MainWindow window, Func<bool> done, string what)
    {
        for (var pass = 0; pass < 50 && !done(); pass++)
        {
            await Task.Yield();
            PageScreenshots.Settle(window);
        }
        Assert.True(done(), $"The window never showed {what}.");
    }
}
