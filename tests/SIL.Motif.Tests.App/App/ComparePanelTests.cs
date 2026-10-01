using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia;
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
                Assert.Equal(4, window.GetVisualDescendants().OfType<OpinionMark>().Count(mark => mark.IsEffectivelyVisible &&
                    mark.FindAncestorOfType<Button>()?.DataContext is CompareRowViewModel));
                Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "Different");
                Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "Not parsed");
                Assert.DoesNotContain(window.GetLogicalDescendants().OfType<TextBlock>(), text =>
                    text.Text is "FieldWorks" or "PanGloss");
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

    [Fact]
    public void ColumnHeadsCarryTheOutcomeMarks_RowHeadsTheOpinionMarks_AndNoCountToggleIsLeft()
    {
        avalonia.Invoke(() => WithPanel(CompareViewModelTests.LostWords(), 1000, window =>
        {
            Assert.Empty(window.GetLogicalDescendants().OfType<RadioButton>());
            var heads = window.GetLogicalDescendants().OfType<Button>()
                .Where(button => button.DataContext is CompareColumnViewModel).ToArray();
            Assert.Equal(5, heads.Length);
            foreach (var head in heads)
            {
                var column = (CompareColumnViewModel)head.DataContext!;
                var glyph = Assert.Single(head.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.Classes.Contains("markGlyph"));
                Assert.Equal(column.OutcomeMark.Glyph, glyph.Text);
                Assert.Contains("outcomeMark", glyph.Classes);
                Assert.Contains(column.OutcomeMark.Value, glyph.Classes);
                Assert.Contains(head.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == column.Label);
            }
            var approvedRow = window.GetLogicalDescendants().OfType<Button>()
                .Single(button => button.DataContext is CompareRowViewModel { Row: WordProjectStatus.Approved });
            Assert.Equal(OpinionMarkKind.Approved, Assert.Single(approvedRow.GetVisualDescendants().OfType<OpinionMark>()).Kind);
        }));
    }

    [Fact]
    public void ACellShowsItsWordsBigAndItsPlacesSmall_AndItsTooltipIsOneLine()
    {
        avalonia.Invoke(() => WithPanel(CompareViewModelTests.LostWords(), 1000, window =>
        {
            var lost = window.GetVisualDescendants().OfType<MatrixCell>().Single(cell =>
                cell.DataContext is CompareCellViewModel { Row: WordProjectStatus.Approved, Column: CompareColumnKind.NoParse });
            var texts = lost.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).ToArray();
            var count = Assert.Single(texts, text => text.Classes.Contains("matrixCellCount"));
            var places = Assert.Single(texts, text => text.Classes.Contains("matrixCellPlaces"));
            Assert.Equal("4", count.Text);
            Assert.Equal("7 places", places.Text);
            Assert.True(places.FontSize < count.FontSize);
            Assert.Contains(texts, text => text.Text == "Lost");
            Assert.Equal("You approved these in FieldWorks; the grammar builds nothing for them.", ToolTip.GetTip(lost));

            var empty = window.GetVisualDescendants().OfType<MatrixCell>().Single(cell =>
                cell.DataContext is CompareCellViewModel { Row: WordProjectStatus.Rejected, Column: CompareColumnKind.Match });
            Assert.DoesNotContain(empty.GetVisualDescendants().OfType<TextBlock>(), text =>
                text.Classes.Contains("matrixCellPlaces") && text.IsEffectivelyVisible);
        }));
    }

    [Fact]
    public void TheChosenCellsPanelShowsWhatItsWordsShare_AboveOneRowOfControls()
    {
        avalonia.Invoke(() =>
        {
            var compare = CompareViewModelTests.LostWords();
            compare.Toggle(compare.Cells.Single(cell => cell.Row == WordProjectStatus.Approved &&
                cell.Column == CompareColumnKind.NoParse), additive: false);
            WithPanel(compare, 1000, window =>
            {
                var strip = Assert.Single(window.GetVisualDescendants().OfType<Border>(), border =>
                    border.Classes.Contains("matrixShared"));
                Assert.True(strip.IsEffectivelyVisible);
                var stripTexts = strip.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
                Assert.Contains("What these words share", stripTexts);
                Assert.Contains("kat", stripTexts);
                Assert.Contains("cut", stripTexts);
                Assert.Contains("in 2", stripTexts);
                Assert.Contains(strip.GetVisualDescendants().OfType<Control>(), control =>
                    AutomationProperties.GetName(control) == "kat cut: 2 of these words use it");

                var heading = Assert.Single(window.GetVisualDescendants().OfType<Control>(), control =>
                    control.Classes.Contains("matrixChosenHeading"));
                Assert.Contains(heading.GetVisualDescendants().OfType<OpinionMark>(), mark => mark.Kind == OpinionMarkKind.Approved);
                Assert.Contains(heading.GetVisualDescendants().OfType<MarkChip>(), chip => chip.Text == "Lost");
                Assert.Contains(heading.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "4 words · 7 places");
                Assert.Contains(heading.GetVisualDescendants().OfType<Button>(), button =>
                    Equals(button.Content, "AI Handoff for these 4 words"));
                Assert.True(strip.Bounds.Top >= heading.Bounds.Bottom - 1);

                Control Named(string name) => Assert.Single(window.GetVisualDescendants().OfType<Control>(), control =>
                    AutomationProperties.GetName(control) == name && control.IsEffectivelyVisible);
                var controls = new[]
                {
                    Named("Order the listed words"), Named("Search the listed words"),
                    Named("Add checked words as Unknown"), Named("Mark checked words as incorrect spelling"),
                };
                var line = controls.Select(control => control.TranslatePoint(new Point(0, control.Bounds.Height / 2), window)!.Value.Y)
                    .ToArray();
                Assert.True(line.Max() - line.Min() < 4, "The list's controls share one row: " + string.Join(", ", line));
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text is "Approve one analysis at a time, in the text.");
            });
        });
    }

    private static void WithPanel(CompareViewModel compare, double width, Action<Window> check)
    {
        var window = new Window
        {
            Content = new ComparePanel(compare),
            RequestedThemeVariant = ThemeVariant.Light,
            Width = width,
            Height = 900,
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            check(window);
        }
        finally
        {
            window.Close();
        }
    }

    private static ParserReading StoredReading(string id, string opinion) =>
        new([new ParserReadingMorph("kitabu", "book", "n", null, false, null)])
        {
            StoredAnalysisId = id,
            StoredAnalysisOpinion = opinion,
        };
}
