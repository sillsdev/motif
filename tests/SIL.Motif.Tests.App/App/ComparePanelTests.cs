using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Automation;
using SIL.Motif.Commands.Queries;
using SIL.Motif.App.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ComparePanelTests(AvaloniaHeadlessFixture avalonia)
{
    [Fact]
    public void MatrixRendersFiveByFiveFiltersWithOpinionAndAgreementLegends()
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
                Assert.Equal(10, window.GetLogicalDescendants().OfType<OpinionMark>().Count());
                Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "Conflict");
                Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "Not assessed");
                var wordCell = window.GetLogicalDescendants().OfType<Border>()
                    .Single(border => border.Classes.Contains("matrixWordCell"));
                Assert.Equal("kitabu: Approved in FieldWorks, PanGloss found no parse.",
                    AutomationProperties.GetName(wordCell));
                Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == compare.Words.Single().Meaning);
                Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), text =>
                    text.Text == compare.Words.Single().Meaning && text.Classes.Contains("error"));
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
    public void CompactWordCellKeepsOneOpinionMarkPerStoredAnalysis()
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

                var wordCell = window.GetLogicalDescendants().OfType<Border>()
                    .Single(border => border.Classes.Contains("matrixWordCell"));
                Assert.Equal(["Approved", "Disapproved"], wordCell.GetLogicalDescendants()
                    .OfType<OpinionMark>().Select(AutomationProperties.GetName));
                Assert.Contains("Approved, Disapproved in FieldWorks", AutomationProperties.GetName(wordCell));
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
