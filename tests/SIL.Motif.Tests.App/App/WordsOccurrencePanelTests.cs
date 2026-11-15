using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Words;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>A word opened from the Words page shows the same places panel as the Passage and stages through it.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "Unit")]
public sealed class WordsOccurrencePanelTests
{
    [Fact]
    public void OpeningAWordFromWordsShowsTheSharedPanelAndApprovingStagesTheTickedPlaces()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await AnalyzeTextsLayoutTests.OpenAnalyzeTexts(seedReaderToken: false);
            try
            {
                var page = workspace.PageModel<WordsPageModel>();
                page.Words.SearchText = "mwalimu";
                workspace.CurrentPage = WorkspacePage.Words;
                PageScreenshots.Settle(window);
                await page.Words.SettleVisibleDetailsAsync();
                var row = Assert.Single(page.Words.Rows, candidate => candidate.Form == "mwalimu");
                row.Listed.IsOpen = true;
                await page.Words.CardPending;
                await page.PanelPending;
                PageScreenshots.Settle(window);

                var model = Assert.IsType<WordOccurrencesViewModel>(page.OccurrencePanel);
                Assert.Equal("mwalimu", model.Form);
                var view = Assert.Single(window.GetVisualDescendants().OfType<WordOccurrencesPanel>());
                Assert.Same(model, view.Model);
                Assert.True(model.TotalCount > 0);
                if (model.ChosenReading is null && model.Readings.Count > 0) model.ChosenReading = model.Readings[0];
                model.CheckShownGaps();
                var selected = model.SelectedCount;
                Assert.True(selected > 0, "A guessed word has a place to keep.");

                var approve = Assert.Single(view.GetVisualDescendants().OfType<Button>(), button =>
                    Avalonia.Automation.AutomationProperties.GetAutomationId(button) == AutomationIds.WordOccurrencesApprove);
                HeadlessClick.Click(window, approve, MotifAppStrings.OccurrencesApproveOne);
                await Wait.ForAsync(() => workspace.Context.Changes.Items.Count >= selected, Wait.Default,
                    () => PageScreenshots.Settle(window));

                var staged = workspace.Context.Changes.Items.Where(change => change.Kind == ChangeKinds.Approve).ToArray();
                Assert.Equal(selected, staged.Length);
                Assert.All(staged, change => Assert.NotNull(change.Occurrence));
                Assert.Equal(selected, model.StagedAnchors.Count);

                row.Listed.IsOpen = false;
                await page.PanelPending;
                PageScreenshots.Settle(window);
                Assert.Null(page.OccurrencePanel);
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ChoosingACurrentTextLeavesTheWordsTotalsAsTheWholeContextHasThem()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await AnalyzeTextsLayoutTests.OpenAnalyzeTexts(seedReaderToken: false);
            try
            {
                var page = workspace.PageModel<WordsPageModel>();
                await workspace.Context.SelectionReads.Pending;
                var all = page.Words.AllCount;
                var places = page.Words.OccurrenceCount;
                Assert.True(all > 0);

                var reader = workspace.PageModel<TextsPageModel>().ResultsInText;
                reader.ShowReader();
                var anchor = workspace.Context.SelectionReads.Summary!.SourcePositions.Last().Location.Anchor;
                reader.SelectOccurrenceText(anchor);
                await AnalyzeTextsLayoutTests.SettleReaderAsync(workspace, window);

                Assert.Equal(all, page.Words.AllCount);
                Assert.Equal(places, page.Words.OccurrenceCount);
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void AWordTheAssessmentReadTwoWaysShowsItsReadingCountOnTheWordsRow()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await AnalyzeTextsLayoutTests.OpenAnalyzeTexts(seedReaderToken: false);
            try
            {
                var page = workspace.PageModel<WordsPageModel>();
                await workspace.Context.SelectionReads.Pending;
                page.Words.SearchText = "mwalimu";
                workspace.CurrentPage = WorkspacePage.Words;
                PageScreenshots.Settle(window);
                await page.Words.SettleVisibleDetailsAsync();
                var producing = Assert.Single(page.Words.Rows, candidate => candidate.Form == "mwalimu").Summary?.Assessment?.AssessmentId;
                Assert.NotNull(producing);

                var result = new AssessmentWordResult("mwalimu", "analysed", false, "Search completed", 0, "|mwalimu;|mwalimu")
                {
                    Origin = new WordMeasurementOrigin(producing, null, DateTimeOffset.UnixEpoch),
                    Readings = [Reading("noun"), Reading("adjective")],
                    Morphology = new ParseWordEvidence("fieldworks-parse-analysis/v1", 0, "mwalimu", 0, false, false, false,
                        [new ParseAnalysis([new ParseMorph("form", "noun", null, null)]),
                         new ParseAnalysis([new ParseMorph("form", "adjective", null, null)])], []),
                };
                page.Words.ShowAssessmentSource(word => word == "mwalimu" ? result : null);

                await page.Words.SettleVisibleDetailsAsync();
                var row = Assert.Single(page.Words.Rows, candidate => candidate.Form == "mwalimu");
                Assert.Contains("2 analyses", Assert.IsType<AssessWordRowViewModel>(row.LastResult).WordRow.ReadingCountText);
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromMinutes(3));
    }

    private static ParserReading Reading(string category) => new(
        [new ParserReadingMorph("mwalimu", "teacher", category, null, false, null)]);
}
