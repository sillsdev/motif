using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Media;
using Avalonia.Automation;
using Avalonia.Input;
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
            var compare = CompareViewModelTests.LostWords();
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

                var addUnknown = Action("add-candidate");
                var incorrectSpelling = Action("incorrect-spelling");
                Assert.False(addUnknown.IsEffectivelyVisible);
                Assert.False(incorrectSpelling.IsEffectivelyVisible);
                compare.Words.First().IsChecked = true;
                window.UpdateLayout();
                Assert.True(addUnknown.IsEffectivelyVisible);
                Assert.True(incorrectSpelling.IsEffectivelyVisible);
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
                Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "FieldWorks");
                Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "PanGloss");
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
    public void AnOpenedCardShowsTheApprovedAnalysisPanGlossMissed_WhenItBuiltAnotherApprovedOne()
    {
        avalonia.Invoke(() =>
        {
            var a = CompareViewModelTests.Approved("a", "kit", "abu");
            var b = CompareViewModelTests.Approved("b", "ki", "tabu");
            var word = CompareViewModelTests.CardWord(stored: [a, b], built: [a], missed: [b]);
            var compare = new CompareViewModel();
            compare.Load([new AssessWordRowViewModel(word.Source)]);
            WithPanel(compare, 1400, window =>
            {
                compare.Words.Single().IsExpanded = true;
                window.UpdateLayout();
                var card = Assert.Single(window.GetVisualDescendants().OfType<WordRowCard>());
                Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == "NOT BUILT");
                var notBuilt = card.GetVisualDescendants().OfType<MorphemeRow>().Last(row => row.IsEffectivelyVisible);
                Assert.Equal(["ki", "tabu"], notBuilt.Morphs!.Select(morph => morph.Form));
            });
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
                var glyph = Assert.Single(head.GetVisualDescendants().OfType<MarkGlyph>());
                var sign = Assert.IsAssignableFrom<Control>(glyph.Child);
                if (MarkGlyphs.IconDataFor(column.OutcomeMark) is not null)
                    Assert.IsType<PathIcon>(sign);
                else
                {
                    var text = Assert.IsType<TextBlock>(sign);
                    Assert.Equal(column.OutcomeMark.Glyph, text.Text);
                    Assert.Contains("outcomeMark", text.Classes);
                    Assert.Contains(column.OutcomeMark.Value, text.Classes);
                }
                Assert.Contains(head.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == column.Label);
            }
            var approvedRow = window.GetLogicalDescendants().OfType<Button>()
                .Single(button => button.DataContext is CompareRowViewModel { Row: WordProjectStatus.Approved });
            Assert.Equal(OpinionMarkKind.Approved, Assert.Single(approvedRow.GetVisualDescendants().OfType<OpinionMark>()).Kind);
        }));
    }

    [Fact]
    public void ACellNamesItsWordAndPlaceCountsWithoutRepeatingThem()
    {
        avalonia.Invoke(() => WithPanel(CompareViewModelTests.LostWords(), 1000, window =>
        {
            var lost = window.GetVisualDescendants().OfType<MatrixCell>().Single(cell =>
                cell.DataContext is CompareCellViewModel { Row: WordProjectStatus.Approved, Column: CompareColumnKind.NoParse });
            var texts = lost.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).ToArray();
            var count = Assert.Single(texts, text => text.Classes.Contains("matrixCellCount"));
            Assert.Equal("4 words", count.Text);
            Assert.Contains(texts, text => text.Classes.Contains("matrixCellPlaces") && text.Text == "7 places");
            Assert.Contains(texts, text => text.Text == "Lost");
            Assert.Equal("You approved these in FieldWorks; PanGloss builds nothing for them.", ToolTip.GetTip(lost));

            var same = window.GetVisualDescendants().OfType<MatrixCell>().Single(cell =>
                cell.DataContext is CompareCellViewModel { Row: WordProjectStatus.Approved, Column: CompareColumnKind.Match });
            Assert.Contains(same.GetVisualDescendants().OfType<TextBlock>(), text =>
                text.Classes.Contains("matrixCellCount") && text.Text == "1 word");
            Assert.Contains(same.GetVisualDescendants().OfType<TextBlock>(), text =>
                text.Classes.Contains("matrixCellPlaces") && text.Text == "2 places");

            var empty = window.GetVisualDescendants().OfType<MatrixCell>().Single(cell =>
                cell.DataContext is CompareCellViewModel { Row: WordProjectStatus.Rejected, Column: CompareColumnKind.Match });
            Assert.Contains(empty.GetVisualDescendants().OfType<TextBlock>(), text =>
                text.Classes.Contains("matrixCellPlaces") && text.Text == "0 places" && text.IsEffectivelyVisible);
        }));
    }

    [Fact]
    public void MatrixResultTextWrapsInsideItsCell()
    {
        avalonia.Invoke(() => WithPanel(MatrixListsWindowWordsTests.Compare(MatrixListsWindowWordsTests.EveryKindOfWord),
            1000, window =>
            {
                var cell = window.GetVisualDescendants().OfType<MatrixCell>().Single(candidate =>
                    candidate.DataContext is CompareCellViewModel
                    {
                        Row: WordProjectStatus.Candidate,
                        Column: CompareColumnKind.Match,
                    });
                var label = Assert.Single(cell.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.Classes.Contains("matrixCellLabel"));

                Assert.Equal("Parses; nothing in FieldWorks yet", label.Text);
                Assert.Equal(TextWrapping.Wrap, label.TextWrapping);
            }));
    }

    [Fact]
    public void NotInFieldWorksRowLabelWrapsInsideItsColumn()
    {
        avalonia.Invoke(() => WithPanel(MatrixListsWindowWordsTests.Compare(MatrixListsWindowWordsTests.EveryKindOfWord),
            988, window =>
            {
                var row = window.GetVisualDescendants().OfType<Button>().Single(button =>
                    button.DataContext is CompareRowViewModel { Row: WordProjectStatus.NotPresent });
                var label = Assert.Single(row.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.Text == "Not in FieldWorks");

                Assert.Equal(TextWrapping.Wrap, label.TextWrapping);
                Assert.True(label.TextLayout.TextLines.Count > 1, "The row label wraps across its narrow column.");
                Assert.True(label.TextLayout.Width <= label.Bounds.Width + 0.5,
                    $"The row label needs {label.TextLayout.Width:0.#} px but has {label.Bounds.Width:0.#}.");
            }));
    }

    [Fact]
    public void AnEmptyCellsZeroIsItsMeaningsSizeAndMuted_WhileACellWithWordsAndTheDashCellKeepTheBigNumber()
    {
        avalonia.Invoke(() => WithPanel(CompareViewModelTests.LostWords(), 1000, window =>
        {
            TextBlock CountOf(WordProjectStatus row, CompareColumnKind column) => window.GetVisualDescendants().OfType<MatrixCell>()
                .Single(cell => cell.DataContext is CompareCellViewModel cellModel && cellModel.Row == row && cellModel.Column == column)
                .GetVisualDescendants().OfType<TextBlock>().Single(text => text.Classes.Contains("matrixCellCount"));
            double Resource(string key) => (double)Application.Current!.FindResource(key)!;

            var full = CountOf(WordProjectStatus.Approved, CompareColumnKind.NoParse);
            var empty = CountOf(WordProjectStatus.Rejected, CompareColumnKind.Match);
            var dash = window.GetVisualDescendants().OfType<MatrixCell>().Single(cell => cell.DataContext is CompareCellViewModel { IsNone: true })
                .GetVisualDescendants().OfType<TextBlock>().Single(text => text.Classes.Contains("matrixCellCount"));
            Assert.Equal("0 words", empty.Text);
            Assert.Equal("0 words", empty.Text);
            Assert.Equal(Resource("Intent.Type.Title"), empty.FontSize);
            Assert.Equal(Resource("Intent.Type.Title"), full.FontSize);
            Assert.Equal(Resource("Intent.Type.Title"), dash.FontSize);
            var muted = (IBrush)Application.Current!.FindResource(ThemeVariant.Light, "Intent.TextMuted")!;
            Assert.Equal(muted, empty.Foreground);
        }));
    }

    [Fact]
    public void TheChosenCellsWordsHideTheMeaningTheyShare_AndAllWordsShowIt()
    {
        avalonia.Invoke(() =>
        {
            var compare = CompareViewModelTests.LostWords();
            WithPanel(compare, 1000, window =>
            {
                Assert.Contains("Meaning", MatrixHeads(window));
                Assert.All(MatrixRows(window), row => Assert.True(row.ShowsMeaning));

                compare.Toggle(compare.Cells.Single(cell => cell.Row == WordProjectStatus.Approved &&
                    cell.Column == CompareColumnKind.NoParse), additive: false);
                window.UpdateLayout();
                Assert.DoesNotContain("Meaning", MatrixHeads(window));
                Assert.NotEmpty(MatrixRows(window));
                Assert.All(MatrixRows(window), row =>
                {
                    Assert.False(row.ShowsMeaning);
                    Assert.DoesNotContain(row.GetVisualDescendants().OfType<MarkChip>(),
                        chip => chip.IsEffectivelyVisible && chip.Mark?.Kind == MarkKind.Meaning);
                });
            });
        });
    }

    private static Grid MatrixList(Window window) => (Grid)window.GetVisualDescendants().OfType<ListBox>()
        .Single(list => AutomationProperties.GetName(list) == "Words in the chosen cells").Parent!;

    private static WordRow[] MatrixRows(Window window) => MatrixList(window).GetVisualDescendants().OfType<WordRow>().ToArray();

    // The matrix list's visible column heads in reading order; Fix these first keeps its own header.
    private static string?[] MatrixHeads(Window window) =>
        MatrixList(window).Children.OfType<WordRowHeader>().Single().GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && text.Classes.Contains("wordRowHeading") && text.Text is { Length: > 1 })
            .OrderBy(text => text.TranslatePoint(default, window)!.Value.X).Select(text => text.Text).ToArray();

    [Fact]
    public void TheChosenCellsPanelShowsWhatItsWordsShare_AboveOneRowOfControls()
    {
        avalonia.Invoke(() =>
        {
            var compare = CompareViewModelTests.LostWords(new AssessmentWordResult("polepole", "timed-out", true,
                "Search stopped at its time limit", 10, null) { ProjectStanding = ProjectStanding.NotPresent });
            var linkedWords = compare.Words.Select(word => word.Source with
            {
                StoredAnalyses = word.Source.StoredAnalyses.Select(analysis => analysis with
                {
                    Morphs = analysis.Morphs.Select(morph => morph with
                    {
                        FieldWorksLink = "silfw://localhost/link",
                    }).ToArray(),
                }).ToArray(),
            }).ToArray();
            compare.Load(linkedWords.Select(word => new AssessWordRowViewModel(word)));
            compare.Toggle(compare.Cells.Single(cell => cell.Row == WordProjectStatus.Approved &&
                cell.Column == CompareColumnKind.NoParse), additive: false);
            WithPanel(compare, 1000, window =>
            {
                var strip = Assert.Single(window.GetVisualDescendants().OfType<Border>(), border =>
                    border.Classes.Contains("matrixShared"));
                Assert.True(strip.IsEffectivelyVisible);
                var stripTexts = strip.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
                Assert.Contains("What these words share:", stripTexts);
                Assert.Contains("kat", stripTexts);
                Assert.Contains("cut", stripTexts);
                Assert.Contains("in 2", stripTexts);
                Assert.Contains(strip.GetVisualDescendants().OfType<Control>(), control =>
                    AutomationProperties.GetName(control) == "kat cut: 2 of these words use it");
                var sharedMorphs = strip.GetVisualDescendants().OfType<MorphemeRow>().ToArray();
                Assert.NotEmpty(sharedMorphs);
                var kat = sharedMorphs.SelectMany(row => row.Children.OfType<Border>())
                    .Single(block => block.Tag is ParserReadingMorphViewModel { Form: "kat" });
                Assert.True(kat.Focusable);
                Assert.Contains("hoverReveal", kat.Classes);
                var link = Assert.Single(kat.GetVisualDescendants().OfType<HyperlinkButton>());
                Assert.Contains("revealLink", link.Classes);
                Assert.Equal(0, link.Opacity);
                Assert.False(link.IsHitTestVisible);
                var katMorph = Assert.IsType<ParserReadingMorphViewModel>(kat.Tag);
                var requested = new List<InspectorSubject>();
                window.AddHandler(InspectLink.RequestedEvent, (_, e) => requested.Add(e.Subject));
                kat.RaiseEvent(new KeyEventArgs
                    { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = kat });
                var subject = Assert.Single(requested);
                Assert.Equal(katMorph.InspectSubject, subject);

                var heading = Assert.Single(window.GetVisualDescendants().OfType<Control>(), control =>
                    control.Classes.Contains("matrixChosenHeading"));
                Assert.Contains(heading.GetVisualDescendants().OfType<OpinionMark>(), mark => mark.Kind == OpinionMarkKind.Approved);
                Assert.Contains(heading.GetVisualDescendants().OfType<MarkChip>(), chip => chip.Text == "Lost");
                Assert.Contains(heading.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "4 words · 7 places");
                Assert.DoesNotContain(heading.GetVisualDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "AI Handoff for the listed words");
                Assert.True(strip.Bounds.Top >= heading.Bounds.Bottom - 1);

                compare.Words.First().IsChecked = true;
                window.UpdateLayout();

                Control Named(string name) => Assert.Single(window.GetVisualDescendants().OfType<Control>(), control =>
                    AutomationProperties.GetName(control) == name && control.IsEffectivelyVisible);
                var controls = new[]
                {
                    Named("Order the listed words"), Named("Search the listed words"),
                    Named("AI Handoff for the listed words"),
                    Named("Add checked words as Unknown"), Named("Mark checked words as incorrect spelling"),
                    Named("Parse the stopped and unparsed words again"),
                };
                var line = controls.Select(control => control.TranslatePoint(new Point(0, control.Bounds.Height / 2), window)!.Value.Y)
                    .ToArray();
                Assert.True(line.Max() - line.Min() < 4, "The list's controls share one row: " + string.Join(", ", line));
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text is "Approve one analysis at a time, in the text.");
            });
        });
    }

    [Fact]
    public void AtTheNarrowWindowTheChosenCellsColumnHeadsStayApart_WithTheMeaningColumnOrWithout()
    {
        avalonia.Invoke(() =>
        {
            var compare = CompareViewModelTests.LostWords();
            // A 1040 px window leaves the Matrix about this wide beside the collapsed sidebar.
            WithPanel(compare, 988, window =>
            {
                void HeadsStayApart(string?[] expected)
                {
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    var header = MatrixList(window).Children.OfType<WordRowHeader>().Single();
                    var heads = header.GetVisualDescendants().OfType<TextBlock>()
                        .Where(text => text.IsEffectivelyVisible && !string.IsNullOrEmpty(text.Text))
                        .OrderBy(text => text.TranslatePoint(default, window)!.Value.X).ToArray();
                    Assert.Equal(expected, heads.Select(text => text.Text).Where(text => text!.Length > 1).Take(4));
                    foreach (var (left, right) in heads.Zip(heads.Skip(1)))
                    {
                        var end = left.TranslatePoint(default, window)!.Value.X + left.TextLayout.WidthIncludingTrailingWhitespace;
                    Assert.True(end + 4 <= right.TranslatePoint(default, window)!.Value.X,
                        $"'{left.Text}' ends at {end:0.#} but '{right.Text}' starts at {right.TranslatePoint(default, window)!.Value.X:0.#}.");
                    }
                }

                HeadsStayApart(["Word", "FieldWorks", "PanGloss", "Meaning"]);
                compare.Toggle(compare.Cells.Single(cell => cell.Row == WordProjectStatus.Approved &&
                    cell.Column == CompareColumnKind.NoParse), additive: false);
                HeadsStayApart(["Word", "FieldWorks", "PanGloss", "Places"]);
            });
        });
    }

    [Fact]
    public void TheMatrixFitsAllFiveColumnsAt1240Pixels()
    {
        avalonia.Invoke(() => WithPanel(CompareViewModelTests.LostWords(), 1036, window =>
        {
            var columns = window.GetVisualDescendants().OfType<Button>()
                .Where(button => button.DataContext is CompareColumnViewModel)
                .OrderBy(button => button.TranslatePoint(default, window)!.Value.X).ToArray();
            Assert.Equal(5, columns.Length);
            Assert.All(columns, column => Assert.True(column.IsEffectivelyVisible));
            var last = columns[^1].TranslatePoint(new Point(columns[^1].Bounds.Width, 0), window)!.Value.X;
            Assert.True(last <= window.Bounds.Width,
                $"The Not parsed column ends at {last:0.#} px in a {window.Bounds.Width:0.#} px window.");
            var notInFieldWorks = Assert.Single(window.GetVisualDescendants().OfType<Button>(), button =>
                AutomationProperties.GetName(button) == "Choose the words not in FieldWorks");
            var rowLabel = Assert.Single(notInFieldWorks.GetVisualDescendants().OfType<TextBlock>(), text =>
                text.Text == "Not in FieldWorks");
            var availableWidth = rowLabel.Bounds.Width;
            rowLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.True(rowLabel.DesiredSize.Width <= availableWidth,
                $"The '{rowLabel.Text}' row label needs {rowLabel.DesiredSize.Width:0.#} px but has {availableWidth:0.#} px.");
        }));
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
