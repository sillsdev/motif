using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Text.RegularExpressions;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class KeyboardInteractionContractTests(AvaloniaHeadlessFixture avalonia)
{
    [Fact]
    public void GeneratedControlFamiliesKeepTheirAuthoredSourceMarkers()
    {
        var repository = RepositoryRoot();
        foreach (var generated in InteractiveControlManifest.GeneratedFamilies)
        {
            var source = Path.Combine(repository, generated.Source.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(source), $"{generated.Key} source is missing: {generated.Source}.");
            Assert.Contains(generated.Marker, File.ReadAllText(source), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TimingKindBarIsDisplayOnlyAndDoesNotTakeKeyboardFocus()
    {
        var bar = new TimingKindBar();

        Assert.False(bar.Focusable);
        Assert.False(bar.IsTabStop);
    }

    [Fact]
    public async Task WarningsFilterToggleRespondsToSpace()
    {
        var warning = new GrammarWarning(GrammarDiagnosticLevel.Warning, "Finding", [], [], "warning: finding")
        {
            Title = "Finding",
            Description = "Finding description",
            Code = "finding",
        };
        var fake = new FakeCommandClient();
        fake.OnCheckGrammar((_, _) => Task.FromResult(CommandOutcome<GrammarCheckResponse>.Success(
            new GrammarCheckResponse([warning], HasBaseline: true))));
        var grammar = new GrammarViewModel(fake);
        await grammar.SetProjectAsync(@"C:\projects\sample.fwdata");

        avalonia.Invoke(() =>
        {
            var panel = new GrammarPanel(grammar);
            var window = new Window { Content = panel, Width = 1040, Height = 900 };
            try
            {
                window.Show();
                window.UpdateLayout();
                var toggle = Assert.Single(panel.GetVisualDescendants().OfType<ToggleButton>(),
                    button => AutomationProperties.GetName(button) == "Show findings with exact uses in your words");
                Assert.True(toggle.Focus());

                var topLevel = TopLevel.GetTopLevel(toggle)!;
                topLevel.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                topLevel.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                Dispatcher.UIThread.RunJobs();

                Assert.True(grammar.Warnings.TouchYourWords);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task WarningSortMenuCanBeOpenedAndChangedWithTheKeyboard()
    {
        var warning = new GrammarWarning(GrammarDiagnosticLevel.Warning, "Finding", [], [], "warning: finding")
        {
            Title = "Finding",
            Description = "Finding description",
            Code = "finding",
        };
        var fake = new FakeCommandClient();
        fake.OnCheckGrammar((_, _) => Task.FromResult(CommandOutcome<GrammarCheckResponse>.Success(
            new GrammarCheckResponse([warning], HasBaseline: true))));
        var grammar = new GrammarViewModel(fake);
        await grammar.SetProjectAsync(@"C:\projects\sample.fwdata");

        avalonia.Invoke(() =>
        {
            var panel = new GrammarPanel(grammar);
            var window = new Window { Content = panel, Width = 1040, Height = 900 };
            try
            {
                window.Show();
                window.UpdateLayout();
                var sort = Assert.Single(panel.GetVisualDescendants().OfType<Button>(),
                    button => AutomationProperties.GetName(button) == "Choose warning sort order");
                Assert.True(sort.Focus());
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                Dispatcher.UIThread.RunJobs();

                var flyout = Assert.IsAssignableFrom<Flyout>(sort.Flyout);
                Assert.True(flyout.IsOpen);
                var reportOrder = Assert.Single(((Control)flyout.Content!).GetVisualDescendants().OfType<Button>(),
                    button => AutomationProperties.GetName(button) == "Sort findings in report order");
                Assert.True(reportOrder.Focus());
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                Dispatcher.UIThread.RunJobs();

                Assert.False(grammar.Warnings.MostYourWordsFirst);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void PointerAndKeyboardSelectionRespectTheSameCellGuards()
    {
        avalonia.Invoke(() =>
        {
            var compare = LoadedCompare();
            var window = MatrixWindow(new ComparePanel(compare));
            try
            {
                window.Show();
                window.UpdateLayout();
                var cell = Assert.Single(window.GetLogicalDescendants().OfType<MatrixCell>(), control =>
                    control.Tag is CompareCellViewModel { IsEmptyImpossible: true });
                var model = Assert.IsType<CompareCellViewModel>(cell.Tag);

                Click(window, cell);
                Assert.False(model.IsSelected);

                Assert.True(cell.Focus(), "The matrix cell can receive keyboard focus.");
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
                Assert.False(model.IsSelected, "Enter cannot select an impossible empty matrix cell.");

                var ordinaryCell = window.GetLogicalDescendants().OfType<MatrixCell>().First(control =>
                    control.Tag is CompareCellViewModel { IsEmptyImpossible: false });
                var ordinaryModel = Assert.IsType<CompareCellViewModel>(ordinaryCell.Tag);
                Assert.True(ordinaryCell.Focus(), "An ordinary matrix cell can receive keyboard focus.");
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
                Assert.True(ordinaryModel.IsSelected, "Enter selects an ordinary matrix cell.");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void AReadOnlyMiniMatrixCannotChangeSelection()
    {
        avalonia.Invoke(() =>
        {
            var compare = LoadedCompare();
            var miniMatrix = new MiniMatrix { DataContext = compare, IsInteractive = false };
            var window = MatrixWindow(miniMatrix);
            try
            {
                window.Show();
                window.UpdateLayout();
                var cell = Assert.Single(window.GetLogicalDescendants().OfType<MatrixCell>(), control =>
                    control.Tag is CompareCellViewModel { Count: > 0 });
                var model = Assert.IsType<CompareCellViewModel>(cell.Tag);
                Assert.False(cell.IsEffectivelyEnabled);

                Click(window, cell);
                Assert.False(model.IsSelected);

                var key = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter };
                cell.RaiseEvent(key);
                Assert.False(key.Handled);
                Assert.False(model.IsSelected);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void MiniMatrixCannotSelectAnImpossibleCellByKeyboard()
    {
        avalonia.Invoke(() =>
        {
            var compare = LoadedCompare();
            var miniMatrix = new MiniMatrix { DataContext = compare, IsInteractive = true };
            var window = MatrixWindow(miniMatrix);
            try
            {
                window.Show();
                window.UpdateLayout();
                var impossibleCell = Assert.Single(window.GetLogicalDescendants().OfType<MatrixCell>(), control =>
                    control.Tag is CompareCellViewModel { IsEmptyImpossible: true });
                var model = Assert.IsType<CompareCellViewModel>(impossibleCell.Tag);
                Assert.True(impossibleCell.Focus(), "The matrix cell can receive keyboard focus.");

                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);

                Assert.False(model.IsSelected, "Enter cannot select an impossible empty matrix cell.");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void WordCardArrowsRespectOccurrenceBoundariesAndChildInputs()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await AnalyzeTextsLayoutTests.OpenAnalyzeTexts();
            try
            {
                var inText = workspace.PageModel<TextsPageModel>().ResultsInText;
                var occurrences = inText.SelectedText!.Lines.SelectMany(line => line.Tokens)
                    .Where(token => token.IsWord).ToArray();
                Assert.NotEmpty(occurrences);

                await inText.OpenTokenCardAsync(occurrences[0]);
                AnalyzeTextsLayoutTests.Settle(window);
                window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.None, null);
                AnalyzeTextsLayoutTests.Settle(window);
                Assert.Same(occurrences[0], inText.SelectedToken);

                await inText.OpenTokenCardAsync(occurrences[^1]);
                AnalyzeTextsLayoutTests.Settle(window);
                window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
                AnalyzeTextsLayoutTests.Settle(window);
                Assert.Same(occurrences[^1], inText.SelectedToken);

                var readingOccurrence = occurrences.FirstOrDefault(occurrence => occurrence.HasReadings);
                Assert.NotNull(readingOccurrence);
                await inText.OpenTokenCardAsync(readingOccurrence);
                AnalyzeTextsLayoutTests.Settle(window);
                var fixActions = Assert.Single(AnalyzeTextsLayoutTests.OpenCard(window)
                    .GetVisualDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Fix actions for this word");
                Assert.True(fixActions.Focus(), "The card's Fix actions button can receive keyboard focus.");

                window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
                AnalyzeTextsLayoutTests.Settle(window);
                Assert.True(ReferenceEquals(readingOccurrence, inText.SelectedToken),
                    "The card's arrow navigation must leave the selected occurrence alone while its reading picker has focus.");
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void ClosingTheCardReturnsFocusToItsOccurrence()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await AnalyzeTextsLayoutTests.OpenAnalyzeTexts();
            try
            {
                var inText = workspace.PageModel<TextsPageModel>().ResultsInText;
                var panel = AnalyzeTextsLayoutTests.Panel(window);
                var occurrence = Assert.IsType<ResultsTokenViewModel>(
                    AnalyzeTextsLayoutTests.Strips(panel).Last().Tag);
                await inText.OpenTokenCardAsync(occurrence);
                AnalyzeTextsLayoutTests.Settle(window);

                var close = AnalyzeTextsLayoutTests.Named<Button>(
                    AnalyzeTextsLayoutTests.OpenCard(window), "Close the word card");
                Click(window, close);
                AnalyzeTextsLayoutTests.Settle(window);

                Assert.Null(inText.SelectedToken);
                var strip = Assert.Single(AnalyzeTextsLayoutTests.Strips(panel), candidate =>
                    ReferenceEquals(candidate.Tag, occurrence));
                Assert.True(strip.IsFocused, "Closing the card returns focus to that word occurrence.");
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(60));
    }

    private static CompareViewModel LoadedCompare()
    {
        var approved = new ParserReading([new ParserReadingMorph("kitabu", "book", "n", null, false, null)])
        {
            StoredAnalysisId = "analysis-1",
            StoredAnalysisOpinion = ReadingGrade.Approved,
        };
        var compare = new CompareViewModel();
        compare.Load([new AssessWordRowViewModel(new AssessmentWordResult(
            "kitabu", "no-analysis", false, "Search completed", 10, null)
        {
            ProjectStanding = ProjectStanding.Approved,
            ExpectedAnalysis = approved,
            MissedApproved = [approved],
            StoredAnalyses = [approved],
        })]);
        return compare;
    }

    private static Window MatrixWindow(Control content) => new()
    {
        Content = content,
        RequestedThemeVariant = ThemeVariant.Light,
        Width = 1400,
        Height = 900,
    };

    private static void Click(Window window, Control control)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("The control is not placed in the window.");
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Motif.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("The Motif repository root was not found.");
    }
}
