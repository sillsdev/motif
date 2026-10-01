using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Styling;
using Avalonia.Automation;
using SIL.Motif.Commands.Queries;
using SIL.Motif.App.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using Xunit;
using WordRow = SIL.Motif.App.Views.WordRow;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ComparePanelTests(AvaloniaHeadlessFixture avalonia)
{
    [Fact]
    public void MatrixBulkActionsUseFieldWorksWordsAndOfferNoOpinion()
    {
        avalonia.Invoke(() =>
        {
            var panel = new ComparePanel(new CompareViewModel());
            var window = new Window
            {
                Content = panel,
                RequestedThemeVariant = ThemeVariant.Light,
                Width = 1400,
                Height = 900,
            };

            try
            {
                window.Show();
                window.UpdateLayout();

                var addUnknown = Action("add-candidate");
                Assert.True(addUnknown.IsVisible);
                Assert.Equal("Add as Unknown", addUnknown.Content);
                Assert.Equal("Add checked words as Unknown", AutomationProperties.GetName(addUnknown));
                Assert.DoesNotContain(window.GetLogicalDescendants().OfType<Button>(), button =>
                    button.CommandParameter is "approve" or "reject" or "candidate");

                Button Action(string kind) => Assert.Single(window.GetLogicalDescendants().OfType<Button>(), button =>
                    Equals(button.CommandParameter, kind));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void MatrixRendersFiveByFiveFiltersWithOpinionMarksAndNeutralColumnHeadings()
    {
        avalonia.Invoke(() =>
        {
            var approvedReading = new ParserReading([new ParserReadingMorph("kitabu", "book", "n", null, false, null)])
            {
                StoredAnalysisId = "analysis-1",
                StoredAnalysisOpinion = ReadingGrade.Approved,
            };
            var compare = new CompareViewModel();
            compare.Load([new AssessWordRowViewModel(new AssessmentWordResult(
                "kitabu", "no-analysis", false, "Search completed", 10, null)
            {
                ProjectStanding = ProjectStanding.Approved,
                ExpectedAnalysis = approvedReading,
                MissedApproved = [approvedReading],
                StoredAnalyses = [approvedReading],
            })]);
            compare.Toggle(compare.Cells.Single(cell => cell.Row == WordProjectStatus.Approved &&
                cell.Column == CompareColumnKind.NoParse), additive: false);
            var panel = new ComparePanel(compare);
            var window = new Window
            {
                Content = panel,
                RequestedThemeVariant = ThemeVariant.Light,
                Width = 1400,
                Height = 900,
            };

            try
            {
                window.Show();
                window.UpdateLayout();

                Assert.Equal(5, compare.Rows.Count);
                Assert.Equal(5, compare.Columns.Count);
                Assert.Equal(25, window.GetLogicalDescendants().OfType<MatrixCell>().Count());
                Assert.Equal(5, window.GetVisualDescendants().OfType<OpinionMark>().Count(mark => mark.IsEffectivelyVisible));
                Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "Different");
                Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "Not parsed");
                Assert.DoesNotContain(window.GetLogicalDescendants().OfType<TextBlock>(), text =>
                    text.Text is "FieldWorks" or "PanGloss");
                foreach (var (label, statusClass) in new[]
                         { ("Stopped", "capped"), ("Not parsed", "notAssessed") })
                {
                    var heading = Assert.Single(window.GetLogicalDescendants().OfType<Button>(), button =>
                        button.Classes.Contains("header") && button.GetLogicalDescendants().OfType<TextBlock>()
                            .Any(text => text.Text == label));
                    var line = Assert.Single(heading.GetLogicalDescendants().OfType<Border>());
                    Assert.DoesNotContain(statusClass, line.Classes);
                }
                var row = Assert.Single(window.GetVisualDescendants().OfType<WordRow>());
                var body = row.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("wordRowBody"));
                Assert.Equal("kitabu · Approved · PanGloss: No parse · Lost", AutomationProperties.GetName(body));
                var meaning = row.GetVisualDescendants().OfType<MarkChip>().Single(chip => chip.Text == compare.Words.Single().Meaning);
                Assert.Contains("problem", meaning.Classes);
                var incorrectRow = window.GetLogicalDescendants().OfType<Button>().Single(button =>
                    AutomationProperties.GetName(button) == "Choose the Incorrect spelling row");
                Assert.All(incorrectRow.GetLogicalDescendants().OfType<OpinionMark>(), mark => Assert.False(mark.IsVisible));
                Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Choose the words not in FieldWorks");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ARowShowsItsAnalysisOpinion_AndItsCardNamesEveryStoredOpinion()
    {
        avalonia.Invoke(() =>
        {
            var approved = StoredReading("approved-id", ReadingGrade.Approved);
            var disapproved = StoredReading("disapproved-id", ReadingGrade.Disapproved);
            var compare = new CompareViewModel();
            compare.Load([new AssessWordRowViewModel(new AssessmentWordResult(
                "kitabu", "analysed", false, "Search completed", 10, null)
            {
                ProjectStanding = ProjectStanding.Approved,
                Readings = [new ParserReading([]), new ParserReading([])],
                StoredAnalyses = [approved, disapproved],
                ReadingGrades = [ReadingGrade.Approved, ReadingGrade.Disapproved],
                Morphology = new ParseWordEvidence("v1", 0, "kitabu", 10,
                    false, false, false, [new ParseAnalysis([]), new ParseAnalysis([])], []),
            })]);
            var panel = new ComparePanel(compare);
            var window = new Window
            {
                Content = panel,
                RequestedThemeVariant = ThemeVariant.Light,
                Width = 1400,
                Height = 900,
            };

            try
            {
                window.Show();
                window.UpdateLayout();

                var row = Assert.Single(window.GetVisualDescendants().OfType<WordRow>());
                Assert.Equal(["Approved"], row.GetVisualDescendants().OfType<OpinionMark>()
                    .Where(mark => mark.IsEffectivelyVisible).Select(AutomationProperties.GetName));

                compare.Words.Single().IsExpanded = true;
                window.UpdateLayout();

                var card = Assert.Single(row.GetVisualDescendants().OfType<WordRowCard>());
                Assert.True(card.IsEffectivelyVisible);
                Assert.Equal(["Approved", "Disapproved"], card.GetVisualDescendants().OfType<MarkChip>()
                    .Where(chip => chip.Mark?.Kind == MarkKind.Opinion).Select(chip => chip.Text));
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static ParserReading StoredReading(string id, string opinion) =>
        new([new ParserReadingMorph("kitabu", "book", "n", null, false, null)])
        {
            StoredAnalysisId = id,
            StoredAnalysisOpinion = opinion,
        };
}
