using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;
using RowFacts = SIL.Motif.Contract.Responses.WordRow;
using WordRow = SIL.Motif.App.Views.WordRow;

namespace SIL.Motif.Tests.App;

/// <summary>
/// The one word row the Matrix's cell list, Fix these first and Lists all show: its columns in one order, the three
/// next steps on every row, and the word's card opening inside the row.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WordRowControlTests(AvaloniaHeadlessFixture avalonia)
{
    private const string MatrixList = "Words in the chosen cells";
    private const string FixFirstList = "Words to check first";
    private const string ListsList = "Words in the selected list";

    [Fact]
    public void EachListsRowsOfferOpenInTextTryAWordAndWordAnalyses_AndTryAWordOpensOnThatWord()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            foreach (var list in new[] { MatrixList, FixFirstList, ListsList })
            {
                var (workspace, window) = FakeComposedWindow.Create();
                try
                {
                    window.Width = 1400;
                    window.Height = 1100;
                    workspace.Assess.Result = Assessment();
                    workspace.Context.OpenTexts(list == ListsList ? TextsTab.Lists : TextsTab.Matrix);
                    window.Show();
                    if (list == ListsList)
                    {
                        var lists = workspace.PageModel<TextsPageModel>().TextsLists;
                        lists.SelectListCommand.Execute(lists.Lists.Single(item => item.Name == "Lost"));
                    }
                    window.UpdateLayout();

                    var row = RowIn(window, list, "kitabu");
                    Assert.Equal(["Open in text", "Try a Word", "Word Analyses ↗"], NextSteps(row).Select(Label));
                    Assert.Equal(["Open kitabu in Analyze texts", "Try kitabu in Try a Word", "Open kitabu in Word Analyses"],
                        NextSteps(row).Select(AutomationProperties.GetName));
                    Assert.Equal(new Uri(AnalysesLink), Assert.IsType<HyperlinkButton>(NextSteps(row)[2]).NavigateUri);

                    HeadlessClick.Click(window, NextSteps(row)[1], "Try a Word");

                    Assert.Equal(WorkspacePage.TryAWord, workspace.CurrentPage);
                    Assert.Equal("kitabu", workspace.PageModel<TryWordPageModel>().Trace.WordToTry);
                }
                finally
                {
                    window.Close();
                }
            }
            return Task.CompletedTask;
        }, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void OpenInTextOnARowOpensThatWordInAnalyzeTexts()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var (workspace, window) = FakeComposedWindow.Create();
            try
            {
                window.Width = 1400;
                window.Height = 1100;
                workspace.Assess.Result = Assessment();
                workspace.Context.OpenTexts(TextsTab.Matrix);
                window.Show();
                window.UpdateLayout();

                HeadlessClick.Click(window, NextSteps(RowIn(window, MatrixList, "kitabu"))[0], "Open in text");

                Assert.Equal(TextsTab.AnalyzeTexts, workspace.PageModel<TextsPageModel>().Tab);
            }
            finally
            {
                window.Close();
            }
            return Task.CompletedTask;
        }, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void TheColumnsRunInOneOrder()
    {
        avalonia.Invoke(() =>
        {
            var (row, window) = Show(new WordRowViewModel(Alikula()));
            try
            {
                var order = new[] { "wordRowTick", "wordRowWord", "wordRowFieldWorks", "wordRowPanGloss", "wordRowMeaning",
                    "wordRowWarnings", "wordRowPlaces", "wordRowTime", "wordRowRead", "wordRowNext" };
                var lefts = order.Select(part => Part(row, part).TranslatePoint(new Point(0, 0), row)!.Value.X).ToArray();

                Assert.Equal(lefts.Order(), lefts);
                Assert.Equal(lefts.Length, lefts.Distinct().Count());
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ARowShowsTheWordsMarksAndPanGlossMorphemesWithTheDifferingOnesMarked()
    {
        avalonia.Invoke(() =>
        {
            var (row, window) = Show(new WordRowViewModel(Alikula()));
            try
            {
                var texts = row.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible)
                    .Select(text => text.Text).ToArray();
                Assert.Contains("alikula", texts);
                Assert.Contains("3SG PST cut FV", texts);
                Assert.Contains("Built something else", texts);
                Assert.Contains("×3", texts);
                Assert.Contains("12 ms", texts);

                var panGloss = Part(row, "wordRowPanGloss");
                var chips = panGloss.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("wordRowMorph"))
                    .ToArray();
                Assert.Equal([("ku-", true), ("l", true), ("kul", false)], chips.Select(chip =>
                    (chip.GetVisualDescendants().OfType<TextBlock>().First().Text, chip.Classes.Contains("different"))));
                Assert.Equal(["kul"], Part(row, "wordRowFieldWorks").GetVisualDescendants().OfType<Border>()
                    .Where(border => border.Classes.Contains("wordRowMorph"))
                    .Select(chip => chip.GetVisualDescendants().OfType<TextBlock>().First().Text));
                Assert.Equal("alikula · Approved · PanGloss: Different · Built something else",
                    ToolTip.GetTip(Body(row)));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void EnterOpensTheCardInsideTheRow_AndEscapeClosesItAndKeepsFocusOnTheRow()
    {
        avalonia.Invoke(() =>
        {
            var card = new TextBlock { Text = "the card" };
            var (row, window) = Show(new WordRowViewModel(Alikula()), card);
            try
            {
                var body = Body(row);
                Assert.True(body.Focusable);
                Assert.False(Part(row, "wordRowCard").IsVisible);
                var closedHeight = row.Bounds.Height;
                Assert.True(closedHeight >= 40, $"A closed row is {closedHeight} high, shorter than its 40 px minimum.");
                body.Focus(NavigationMethod.Tab);

                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.UpdateLayout();

                Assert.True(row.IsOpen);
                Assert.True(card.IsEffectivelyVisible);
                Assert.Same(row, card.FindAncestorOfType<WordRow>());
                Assert.Contains("open", Part(row, "wordRowFrame").Classes);
                Assert.True(row.Bounds.Height > closedHeight + card.Bounds.Height - 1, "The opened row does not grow to hold its card.");

                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                window.UpdateLayout();

                Assert.False(row.IsOpen);
                Assert.False(Part(row, "wordRowCard").IsVisible);
                Assert.True(body.IsFocused);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void AClickOnTheWordOpensTheCard_AndAClickOnANextStepDoesNot()
    {
        avalonia.Invoke(() =>
        {
            var tried = new List<string>();
            var (row, window) = Show(new WordRowViewModel(Alikula(), new WordRowRoutes { TryWord = tried.Add }),
                new TextBlock { Text = "the card" });
            try
            {
                HeadlessClick.Click(window, NextSteps(row)[1], "Try a Word");
                Assert.False(row.IsOpen);
                Assert.Equal(["alikula"], tried);

                HeadlessClick.Click(window, Body(row), "alikula");
                Assert.True(row.IsOpen);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void DownAndUpMoveFocusFromRowToRow()
    {
        avalonia.Invoke(() =>
        {
            var compare = new CompareViewModel();
            compare.Load(new[] { "kitabu", "watoto", "chakula" }.Select(Lost));
            var window = new Window { Content = new ComparePanel(compare), Width = 1400, Height = 1000 };
            try
            {
                window.Show();
                window.UpdateLayout();
                var rows = Rows(window, MatrixList);
                Body(rows[0]).Focus(NavigationMethod.Tab);

                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                window.UpdateLayout();
                Assert.True(Body(Rows(window, MatrixList)[1]).IsFocused);

                window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
                window.UpdateLayout();
                Assert.True(Body(Rows(window, MatrixList)[0]).IsFocused);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TabReachesTheRowThenItsTickAndEachNextStep()
    {
        avalonia.Invoke(() =>
        {
            var compare = new CompareViewModel();
            compare.Load([Lost("kitabu")]);
            var window = new Window { Content = new ComparePanel(compare), Width = 1400, Height = 1000 };
            try
            {
                window.Show();
                window.UpdateLayout();
                var row = Rows(window, MatrixList).Single();
                Body(row).Focus(NavigationMethod.Tab);

                var reached = new List<string?> { Focused(window) };
                for (var press = 0; press < 4; press++)
                {
                    window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                    reached.Add(Focused(window));
                }

                Assert.Equal(["row", "tick", "open-in-text", "try-a-word", "word-analyses"],
                    reached.Select(id => id?.Replace("motif-word-row-matrix-kitabu-", string.Empty, StringComparison.Ordinal)));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheFocusRingSpansTheWholeRow_FromTheTickToTheNextSteps()
    {
        avalonia.Invoke(() =>
        {
            var (row, window) = Show(new WordRowViewModel(Alikula()));
            try
            {
                Body(row).Focus(NavigationMethod.Tab);
                window.UpdateLayout();

                var ring = Assert.IsAssignableFrom<Control>(ControlContracts.ComponentStateContractCases.RingAround(Body(row)));
                var around = BoundsIn(ring, window);
                var parts = new List<Control> { Part(row, "wordRowTick") };
                parts.AddRange(NextSteps(row));
                Assert.All(parts, part => Assert.True(around.Contains(BoundsIn(part, window)),
                    $"The focus ring {around} does not surround {AutomationProperties.GetName(part) ?? part.GetType().Name} " +
                    $"at {BoundsIn(part, window)}."));

                row.IsOpen = true;
                window.UpdateLayout();
                var frame = BoundsIn(Part(row, "wordRowEdge"), window);
                Assert.All(parts, part => Assert.True(frame.Contains(BoundsIn(part, window))));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void AtTheNarrowWindowEveryColumnHeadFitsItsColumn_AndNoneTouchesTheNext()
    {
        avalonia.Invoke(() =>
        {
            var header = new WordRowHeader();
            var row = new WordRow { Row = new WordRowViewModel(Alikula()), List = "matrix" };
            var host = new StackPanel { Children = { header, row } };
            Grid.SetIsSharedSizeScope(host, true);
            // A 1040 px window leaves the list about this wide, beside the collapsed sidebar and the page's insets.
            var window = new Window { Content = host, Width = 940, Height = 300, RequestedThemeVariant = ThemeVariant.Light };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var heads = header.GetVisualDescendants().OfType<TextBlock>().Where(text => !string.IsNullOrEmpty(text.Text))
                    .OrderBy(text => BoundsIn(text, window).X).ToArray();
                Assert.Equal(["WORD", "FIELDWORKS", "PANGLOSS", "MEANING", "PLACES", "TIME", "NEXT"],
                    heads.Select(text => text.Text!).Where(text => text.All(char.IsLetter)));
                Assert.All(heads, text => Assert.True(text.TextLayout.WidthIncludingTrailingWhitespace <= text.Bounds.Width + 0.5,
                    $"'{text.Text}' needs {text.TextLayout.WidthIncludingTrailingWhitespace:0.#} px but has {text.Bounds.Width:0.#}."));
                foreach (var (left, right) in heads.Zip(heads.Skip(1)))
                {
                    var end = BoundsIn(left, window).X + left.TextLayout.WidthIncludingTrailingWhitespace;
                    Assert.True(end + 4 <= BoundsIn(right, window).X, $"'{left.Text}' runs into '{right.Text}'.");
                }
                var tops = heads.Select(text => BoundsIn(text, window).Y).ToArray();
                Assert.True(tops.Max() - tops.Min() < 1, "The column heads do not share one line.");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void InTheOpenCardEachMorphemesFormIsItsLinkToFieldWorks()
    {
        avalonia.Invoke(() =>
        {
            var compare = new CompareViewModel();
            compare.Load([new AssessWordRowViewModel(new AssessmentWordResult("kitabu", "analysed", false, "Search completed", 3, null)
            {
                ProjectStanding = ProjectStanding.NotPresent,
                Readings = [new ParserReading([new ParserReadingMorph("ki-", "7", "n", null, false, LexiconLink),
                    new ParserReadingMorph("tabu", "book", "n", null, false, LexiconLink)])],
                Morphology = new ParseWordEvidence("v1", 0, "kitabu", 3, false, false, false, [new ParseAnalysis([])], []),
            })]);
            var window = new Window { Content = new ComparePanel(compare), Width = 1400, Height = 1000 };
            try
            {
                window.Show();
                compare.Words.Single().IsExpanded = true;
                window.UpdateLayout();

                var card = Assert.Single(window.GetVisualDescendants().OfType<WordRowCard>());
                var links = card.GetVisualDescendants().OfType<HyperlinkButton>().Where(link => link.IsEffectivelyVisible).ToArray();
                Assert.Equal(["ki-", "tabu"], links.Select(link => link.GetVisualDescendants().OfType<TextBlock>().First().Text));
                Assert.All(links, link =>
                {
                    Assert.Contains("morphLink", link.Classes);
                    Assert.Equal(new Uri(LexiconLink), link.NavigateUri);
                });
                Assert.Equal(["Open ki- in Lexicon Edit", "Open tabu in Lexicon Edit"], links.Select(link => ToolTip.GetTip(link)));
                var morphs = compare.Words.Single().Readings.Single().Morphs;
                Assert.Equal(morphs.Select(morph => morph.LinkName), links.Select(AutomationProperties.GetName));
                Assert.DoesNotContain(card.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("Lexicon Edit") == true);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void RowPartsCarryStableAutomationIdsNamingTheirList()
    {
        avalonia.Invoke(() =>
        {
            var (row, window) = Show(new WordRowViewModel(Alikula()), list: "lists");
            try
            {
                Assert.Equal("motif-word-row-lists-alikula-row", AutomationProperties.GetAutomationId(Body(row)));
                Assert.Equal(["motif-word-row-lists-alikula-open-in-text", "motif-word-row-lists-alikula-try-a-word",
                        "motif-word-row-lists-alikula-word-analyses"],
                    NextSteps(row).Select(AutomationProperties.GetAutomationId));
                Assert.Equal("motif-word-row-lists-alikula-tick", AutomationProperties.GetAutomationId(
                    Part(row, "wordRowTick").GetVisualDescendants().OfType<CheckBox>().Single()));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void WordRowIdsAreSafeForAnyFormAndDistinctPerList()
    {
        var turkish = AutomationIds.ForWordRowPart("matrix", "günler", "row");

        Assert.Matches("^motif-word-row-matrix-[a-z0-9-]+-row$", turkish);
        Assert.Equal(turkish, AutomationIds.ForWordRowPart("matrix", "günler", "row"));
        Assert.NotEqual(turkish, AutomationIds.ForWordRowPart("fix-first", "günler", "row"));
        Assert.NotEqual(AutomationIds.ForWordRowPart("matrix", "a-b", "row"), AutomationIds.ForWordRowPart("matrix", "a b", "row"));
        Assert.Throws<ArgumentException>(() => AutomationIds.ForWordRowPart("matrix", "günler", "elsewhere"));
    }

    [Fact]
    public void AWordWithoutAWordformSaysWhyWordAnalysesCannotOpen()
    {
        avalonia.Invoke(() =>
        {
            var (row, window) = Show(new WordRowViewModel(Alikula() with { WordAnalysesLink = null }));
            try
            {
                var link = NextSteps(row)[2];
                Assert.False(link.IsEnabled);
                Assert.Equal("FieldWorks has no wordform spelled alikula", ToolTip.GetTip(link));
                Assert.True(ToolTip.GetShowOnDisabled(link));
            }
            finally
            {
                window.Close();
            }
        });
    }

    private const string LexiconLink = "silfw://localhost/link?database%3dp%26tool%3dlexiconEdit";

    private static string? Focused(Window window) =>
        AutomationProperties.GetAutomationId((Control)window.FocusManager!.GetFocusedElement()!);

    private static Rect BoundsIn(Control control, Visual root) =>
        new(control.TranslatePoint(default, root)!.Value, control.Bounds.Size);

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

    private static (WordRow Row, Window Window) Show(WordRowViewModel model, Control? card = null, string list = "matrix")
    {
        var row = new WordRow { Row = model, List = list, Card = card };
        var host = new StackPanel { Children = { row } };
        Grid.SetIsSharedSizeScope(host, true);
        var window = new Window { Content = host, Width = 1400, Height = 400, RequestedThemeVariant = ThemeVariant.Light };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return (row, window);
    }

    private static Control Part(WordRow row, string part) =>
        row.GetVisualDescendants().OfType<Control>().First(control => control.Classes.Contains(part));

    private static Border Body(WordRow row) => (Border)Part(row, "wordRowBody");

    private static Button[] NextSteps(WordRow row) =>
        Part(row, "wordRowNext").GetVisualDescendants().OfType<Button>().ToArray();

    private static string? Label(Button button) => button.Content as string;

    private static WordRow[] Rows(Window window, string list) =>
        window.GetLogicalDescendants().OfType<ListBox>().Single(box => AutomationProperties.GetName(box) == list)
            .GetVisualDescendants().OfType<WordRow>().ToArray();

    private static WordRow RowIn(Window window, string list, string word) =>
        Rows(window, list).Single(row => row.Row?.Word == word);

    private static AssessWordRowViewModel Lost(string form) => new(LostResult(form));

    private static AssessmentWordResult LostResult(string form)
    {
        var approved = new ParserReading([new ParserReadingMorph(form, "gloss", "n", null, false, null)])
        {
            StoredAnalysisId = $"analysis-{form}",
            StoredAnalysisOpinion = ReadingGrade.Approved,
        };
        return new AssessmentWordResult(form, "no-analysis", false, "Search completed", 10, null)
        {
            ProjectStanding = ProjectStanding.Approved,
            ExpectedAnalysis = approved,
            MissedApproved = [approved],
            StoredAnalyses = [approved],
            OccurrenceCount = 2,
            TryWordLink = AnalysesLink,
            FixFirst = new FixFirstPriority(FixFirstCategory.ApprovedNoParse, 1, "Approved, not parsed",
                "Expected form missed, not built."),
        };
    }

    private static AssessCommandResponse Assessment() => new(
        new BaselineCaptureResponse(
            new BaselineToken("project", "sha256:" + new string('a', 64), "1", "2026-09-01T00:00:00Z",
                "sha256:" + new string('b', 64)),
            "project.fwdata", DateTimeOffset.UtcNow, false, false),
        new SelectionProjection([], []), [], "1 search completed")
    {
        Words = [LostResult("kitabu")],
    };
}
