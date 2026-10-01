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
                        lists.SelectListCommand.Execute(lists.Lists.Single(item => item.Name == "Approved, not parsed"));
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
                body.Focus(NavigationMethod.Tab);

                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.UpdateLayout();

                Assert.True(row.IsOpen);
                Assert.True(card.IsEffectivelyVisible);
                Assert.Same(row, card.FindAncestorOfType<WordRow>());
                Assert.Contains("open", Part(row, "wordRow").Classes);

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
    public void TabReachesTheTickTheRowAndEachNextStep()
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
                var tick = Assert.IsType<CheckBox>(Part(row, "wordRowTick").GetVisualDescendants().OfType<CheckBox>().Single());
                tick.Focus(NavigationMethod.Tab);

                var reached = new List<string?>();
                for (var press = 0; press < 3; press++)
                {
                    window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                    var focused = (Control)window.FocusManager!.GetFocusedElement()!;
                    reached.Add(AutomationProperties.GetAutomationId(focused) ?? focused.GetType().Name);
                }

                Assert.Equal(["motif-word-row-matrix-kitabu-row", "motif-word-row-matrix-kitabu-open-in-text",
                    "motif-word-row-matrix-kitabu-try-a-word"], reached);
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
