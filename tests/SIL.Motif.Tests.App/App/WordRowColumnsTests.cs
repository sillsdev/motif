using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;
using RowFacts = SIL.Motif.Contract.Responses.WordRow;
using WordRow = SIL.Motif.App.Views.WordRow;

namespace SIL.Motif.Tests.App;

/// <summary>
/// A page may hide the word row's columns it does not need, but never moves one: what is left keeps its order and
/// closes up, the header follows the same choice, and the three next steps stay on every row.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WordRowColumnsTests(AvaloniaHeadlessFixture avalonia)
{
    private const WordRowColumns OpinionOutcomeAndTime = WordRowColumns.FieldWorks | WordRowColumns.PanGloss |
        WordRowColumns.Time;

    [Fact]
    public void HiddenColumnsLeaveNoWidthOrGap_AndTheShownOnesKeepTheirOrder()
    {
        avalonia.Invoke(() =>
        {
            var (header, row, window) = Show(new WordRow { Row = new WordRowViewModel(Alikula()), Columns = OpinionOutcomeAndTime },
                OpinionOutcomeAndTime);
            try
            {
                foreach (var hidden in new[] { "wordRowTick", "wordRowMeaning", "wordRowWarnings", "wordRowPlaces", "wordRowRead" })
                    Assert.False(Part(row, hidden).IsEffectivelyVisible, $"{hidden} still shows.");
                var shown = new[] { "wordRowWord", "wordRowFieldWorks", "wordRowPanGloss", "wordRowTime", "wordRowNextColumn" };
                var lefts = shown.Select(part => BoundsIn(Part(row, part), row).X).ToArray();
                Assert.Equal(lefts.Order(), lefts);
                Assert.Equal(["Open in text", "Try a Word", "Word Analyses ↗"], NextSteps(row).Select(step => step.Content as string));

                // With the morphemes hidden, the outcome says its word, and only one column gap sits between cells.
                var texts = VisibleTexts(row);
                Assert.Contains("Different", texts);
                Assert.DoesNotContain("ku-", texts);
                Assert.DoesNotContain("Built something else", texts);
                var panGloss = BoundsIn(Part(row, "wordRowPanGloss"), row);
                var time = BoundsIn(Part(row, "wordRowTime"), row);
                Assert.InRange(time.X - panGloss.Right, 0, 8.5);
                // No room is kept for morphemes the list hides, so the row fits beside a page's side panel.
                Assert.True(panGloss.Width < 110, $"The outcome alone takes {panGloss.Width:0.#} px.");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void AHeaderGivenTheSameColumnsPutsEachHeadOverItsCell()
    {
        avalonia.Invoke(() =>
        {
            var (header, row, window) = Show(new WordRow { Row = new WordRowViewModel(Alikula()), Columns = OpinionOutcomeAndTime },
                OpinionOutcomeAndTime);
            try
            {
                var heads = header.GetVisualDescendants().OfType<TextBlock>()
                    .Where(text => text.IsEffectivelyVisible && !string.IsNullOrEmpty(text.Text))
                    .OrderBy(text => BoundsIn(text, window).X).ToArray();
                Assert.Equal(["WORD", "FieldWorks", "PanGloss", "TIME", "NEXT"], heads.Select(text => text.Text));
                var cells = new[] { "wordRowWord", "wordRowFieldWorks", "wordRowPanGloss", "wordRowTime", "wordRowNextColumn" };
                foreach (var (head, cell) in heads.Zip(cells))
                {
                    var cellBounds = BoundsIn(Part(row, cell), window);
                    var headBounds = BoundsIn(head, window);
                    Assert.True(headBounds.X >= cellBounds.X - 0.5 && headBounds.X < cellBounds.Right,
                        $"'{head.Text}' starts at {headBounds.X:0.#}, outside its cell {cellBounds.X:0.#}–{cellBounds.Right:0.#}.");
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheWordListUsesTheSharedRowAndKeepsFullEngineHeadingsAtNarrowWidths()
    {
        avalonia.Invoke(() =>
        {
            var columns = WordRowColumnSets.WordList;
            var (_, row, window) = Show(new WordRow
            {
                Row = new WordRowViewModel(Alikula()),
                Columns = columns,
            }, columns, width: 900);
            try
            {
                Assert.True((columns & WordRowColumns.Meaning) != 0);
                Assert.True((columns & WordRowColumns.Time) != 0);
                Assert.True(Part(row, "wordRowMeaning").IsEffectivelyVisible);
                Assert.True(Part(row, "wordRowTime").IsEffectivelyVisible);
                Assert.Contains("FieldWorks", VisibleTexts(window));
                Assert.Contains("PanGloss", VisibleTexts(window));
                Assert.DoesNotContain("FW", VisibleTexts(window));
                Assert.DoesNotContain("PG", VisibleTexts(window));
                Assert.Equal("3 places", ToolTip.GetTip(Part(row, "wordRowPlaces")));

                row.FocusRow();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var dataCells = new[] { "wordRowWord", "wordRowFieldWorks", "wordRowPanGloss", "wordRowMeaning", "wordRowTime" }
                    .Select(part => BoundsIn(Part(row, part), row)).ToArray();
                var nextSteps = BoundsIn(Part(row, "wordRowNext"), row);
                Assert.True(nextSteps.Top >= dataCells.Max(cell => cell.Bottom) - 0.5,
                    $"Next steps begin at {nextSteps.Top:0.#}, before the data ends at {dataCells.Max(cell => cell.Bottom):0.#}.");

                row.Row = new WordRowViewModel(new RowFacts("one", WordRowOutcome.Same, "Kept", WordRowTone.Fine)
                { Places = 1 });
                window.UpdateLayout();
                Assert.Equal("1 place", ToolTip.GetTip(Part(row, "wordRowPlaces")));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void OutcomesStayColouredTextWhileMeaningsKeepTheirFill()
    {
        avalonia.Invoke(() =>
        {
            var columns = WordRowColumns.PanGloss | WordRowColumns.Meaning;
            var (_, row, window) = Show(new WordRow
            {
                Row = new WordRowViewModel(Alikula()),
                Columns = columns,
            }, columns);
            try
            {
                Assert.True(window.TryFindResource("Intent.Clear", ThemeVariant.Light, out var clear));
                var expectedClear = Assert.IsAssignableFrom<ISolidColorBrush>(clear).Color;
                var outcome = row.FindControl<MarkChip>("OutcomeAlone")!;
                var meaning = row.GetVisualDescendants().OfType<MarkChip>()
                    .Single(chip => chip.Mark?.Kind == MarkKind.Meaning && chip.IsEffectivelyVisible);

                Assert.Equal(MarkKind.Outcome, outcome.Mark?.Kind);
                Assert.Equal(expectedClear, Assert.IsAssignableFrom<ISolidColorBrush>(outcome.Background).Color);
                Assert.NotEqual(expectedClear, Assert.IsAssignableFrom<ISolidColorBrush>(meaning.Background).Color);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void FieldWorksMorphemesStayOnOneLineWhenTheRowRunsOutOfWidth()
    {
        avalonia.Invoke(() =>
        {
            var morphs = Enumerable.Range(1, 10).Select(index => new ParserReadingMorph(
                $"morpheme{index}", $"gloss{index}", "n", null, false, null)).ToArray();
            var facts = new RowFacts("word", WordRowOutcome.Same, "Kept", WordRowTone.Fine)
            {
                FieldWorksMorphemes = morphs,
            };
            var columns = WordRowColumns.FieldWorks | WordRowColumns.FieldWorksMorphemes;
            var (_, row, window) = Show(new WordRow
            {
                Row = new WordRowViewModel(facts),
                Columns = columns,
            }, columns, width: 520);
            try
            {
                var morphsInRow = Part(row, "wordRowFieldWorks").GetVisualDescendants().OfType<Border>()
                    .Where(border => border.Classes.Contains("wordRowMorph") && border.IsEffectivelyVisible).ToArray();
                Assert.Equal(10, morphsInRow.Length);
                var tops = morphsInRow.Select(morph => BoundsIn(morph, row).Top).Distinct().ToArray();
                Assert.Single(tops);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void AListSharingOneMeaningHidesItsColumn_OnlyWhereThePageCanShowIt()
    {
        avalonia.Invoke(() =>
        {
            var (header, row, window) = Show(new WordRow { Row = new WordRowViewModel(Alikula()), ShowsMeaning = false },
                WordRowColumns.All);
            header.ShowsMeaning = false;
            window.UpdateLayout();
            try
            {
                Assert.False(Part(row, "wordRowMeaning").IsEffectivelyVisible);
                Assert.DoesNotContain("MEANING", VisibleTexts(header));
                // The hidden meaning gives up its column gap as well as its width.
                var panGloss = BoundsIn(Part(row, "wordRowPanGloss"), row);
                Assert.InRange(BoundsIn(Part(row, "wordRowWarnings"), row).X - panGloss.Right, 0, 8.5);

                row.ShowsMeaning = header.ShowsMeaning = true;
                window.UpdateLayout();
                Assert.True(Part(row, "wordRowMeaning").IsEffectivelyVisible);
                Assert.Contains("MEANING", VisibleTexts(header));

                // A page whose columns leave the meaning out keeps it hidden whatever the list's words share.
                row.Columns = header.Columns = WordRowColumns.All & ~WordRowColumns.Meaning;
                window.UpdateLayout();
                Assert.False(Part(row, "wordRowMeaning").IsEffectivelyVisible);
                Assert.DoesNotContain("MEANING", VisibleTexts(header));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void APagesOwnTimeTakesTheTimeCell_AndTheCellWidensToHoldIt()
    {
        avalonia.Invoke(() =>
        {
            var (_, row, window) = Show(new WordRow
            {
                Row = new WordRowViewModel(Alikula()),
                Columns = OpinionOutcomeAndTime,
                TimeText = "180 ms of its 700 ms",
            }, OpinionOutcomeAndTime);
            try
            {
                var texts = VisibleTexts(row);
                Assert.Contains("180 ms of its 700 ms", texts);
                Assert.DoesNotContain("12 ms", texts);
                var time = row.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "180 ms of its 700 ms");
                Assert.True(time.TextLayout.WidthIncludingTrailingWhitespace <= time.Bounds.Width + 0.5,
                    $"The page's time needs {time.TextLayout.WidthIncludingTrailingWhitespace:0.#} px but has {time.Bounds.Width:0.#}.");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void WarningsRowsShowMarksAndPlacesWithoutImplyingParserMorphemesWereRead()
    {
        var columns = WordRowColumnSets.Warnings;
        Assert.Equal(WordRowColumns.FieldWorks | WordRowColumns.PanGloss | WordRowColumns.Places, columns);

        avalonia.Invoke(() =>
        {
            var (_, row, window) = Show(new WordRow
            {
                Row = new WordRowViewModel(Alikula()),
                Columns = columns,
            }, columns);
            try
            {
                var visible = VisibleTexts(row);
                Assert.Contains("A", visible);
                Assert.Contains("≠", visible);
                Assert.Contains("Different", visible);
                Assert.Contains("×3", visible);
                Assert.DoesNotContain("ku-", visible);
                Assert.DoesNotContain("kul", visible);
                Assert.Contains("Different", visible);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ThePagesActionsShowUnderTheRow_AndAClickOnOneLeavesTheCardShut()
    {
        avalonia.Invoke(() =>
        {
            var undone = 0;
            var undo = new Button { Content = "Undo" };
            undo.Click += (_, _) => undone++;
            var (_, row, window) = Show(new WordRow
            {
                Row = new WordRowViewModel(Alikula()),
                Card = new TextBlock { Text = "the card" },
                Actions = undo,
            }, WordRowColumns.All);
            try
            {
                Assert.True(undo.IsEffectivelyVisible);
                Assert.True(BoundsIn(undo, row).Y >= BoundsIn(Part(row, "wordRowBody"), row).Bottom - 0.5,
                    "The page's actions should sit under the row's line.");

                HeadlessClick.Click(window, undo, "Undo");

                Assert.Equal(1, undone);
                Assert.False(row.IsOpen);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private const string AnalysesLink = "silfw://localhost/link?database%3dp%26tool%3dAnalyses";

    private static readonly ParserReadingMorph Kul = new("kul", "cut", "v", null, false, null)
        { AllomorphId = "form-kul", GrammaticalInfoId = "msa-kul" };

    private static RowFacts Alikula() => new("alikula", WordRowOutcome.Different, "Built something else", WordRowTone.Problem)
    {
        Gloss = "3SG PST cut FV",
        Opinion = ProjectStanding.Approved,
        FieldWorksMorphemes = [Kul],
        PanGlossMorphemes = [new("ku-", "INF", "v", null, false, null), new("l", "eat", "v", null, false, null), Kul],
        DifferingPositions = [1, 2],
        PanGlossReadingCount = 2,
        Places = 3,
        ElapsedMs = 12,
        WordAnalysesLink = AnalysesLink,
    };

    private static (WordRowHeader Header, WordRow Row, Window Window) Show(WordRow row, WordRowColumns columns,
        double width = 1240)
    {
        var header = new WordRowHeader { Columns = columns };
        var host = new StackPanel { Children = { header, row } };
        Grid.SetIsSharedSizeScope(host, true);
        var window = new Window { Content = host, Width = width, Height = 400, RequestedThemeVariant = ThemeVariant.Light };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return (header, row, window);
    }

    private static string?[] VisibleTexts(Control root) =>
        root.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text)
            .ToArray();

    private static Rect BoundsIn(Control control, Visual root) =>
        new(control.TranslatePoint(default, root)!.Value, control.Bounds.Size);

    private static Control Part(WordRow row, string part) =>
        row.GetVisualDescendants().OfType<Control>().First(control => control.Classes.Contains(part));

    private static Button[] NextSteps(WordRow row) =>
        Part(row, "wordRowNext").GetVisualDescendants().OfType<Button>().ToArray();
}
