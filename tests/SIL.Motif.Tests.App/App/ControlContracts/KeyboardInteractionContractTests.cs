using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using System.Text.RegularExpressions;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class KeyboardInteractionContractTests(AvaloniaHeadlessFixture avalonia)
{
    [Fact]
    public void EveryCurrentInteractiveDeclarationHasAnAuthoredContract()
    {
        var repository = RepositoryRoot();
        var viewRoot = Path.Combine(repository, "src", "SIL.Motif.App", "Views");
        var actual = new Dictionary<(string Source, string MarkupType), int>();
        foreach (var path in Directory.EnumerateFiles(viewRoot, "*.axaml", SearchOption.AllDirectories))
        {
            var source = Path.GetRelativePath(repository, path).Replace('\\', '/');
            var markup = File.ReadAllText(path);
            foreach (var markupType in InteractiveControlManifest.MarkupFamilies.Keys)
            {
                var count = InteractiveControlManifest.CountDeclarations(markup, markupType);
                if (count > 0) actual.Add((source, markupType), count);
            }
        }

        var expected = InteractiveControlManifest.MarkupDeclarations.ToDictionary(
            declaration => (declaration.Source, declaration.MarkupType), declaration => declaration.Count);
        Assert.Equal(expected.Keys.OrderBy(key => key), actual.Keys.OrderBy(key => key));
        foreach (var (declaration, count) in expected)
        {
            Assert.True(InteractiveControlManifest.MarkupFamilies.ContainsKey(declaration.MarkupType),
                $"{declaration.Source} declares an interactive type with no family: {declaration.MarkupType}.");
            Assert.Equal(count, actual[declaration]);
        }

        var elementPattern = new Regex("<(?<name>[A-Za-z_][A-Za-z0-9_.:-]*)\\b(?<attributes>[^>]*)>",
            RegexOptions.Singleline);
        var actionAttributes = new Regex(
            "\\b(Command|Click|KeyDown|PointerPressed)\\s*=|\\b(Focusable|IsTabStop)\\s*=\\s*['\\\"]True['\\\"]",
            RegexOptions.IgnoreCase);
        foreach (var path in Directory.EnumerateFiles(viewRoot, "*.axaml", SearchOption.AllDirectories))
        {
            var source = Path.GetRelativePath(repository, path).Replace('\\', '/');
            foreach (Match element in elementPattern.Matches(File.ReadAllText(path)))
            {
                var markupType = element.Groups["name"].Value;
                if (!actionAttributes.IsMatch(element.Groups["attributes"].Value)) continue;
                Assert.True(InteractiveControlManifest.MarkupFamilies.ContainsKey(markupType),
                    $"{source} has an action or focusable declaration with no authored family: {markupType}.");
            }
        }

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
                ReportedAppGaps.AssertStillReproduces(
                    nameof(PointerAndKeyboardSelectionRespectTheSameCellGuards), model.IsSelected);
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
    public void WordCardArrowsRespectOccurrenceBoundariesAndChildInputs()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await AnalyzeTextsLayoutTests.OpenAnalyzeTexts();
            try
            {
                var inText = workspace.PageModel<TextsPageModel>().ResultsInText;
                var panel = AnalyzeTextsLayoutTests.Panel(window);
                var occurrences = AnalyzeTextsLayoutTests.Strips(panel)
                    .Select(strip => Assert.IsType<ResultsTokenViewModel>(strip.Tag)).ToArray();
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
                var readingPicker = AnalyzeTextsLayoutTests.Named<ComboBox>(
                    AnalyzeTextsLayoutTests.OpenCard(window), "Choose a PanGloss reading for this word");
                Assert.True(readingPicker.Focus(), "The reading picker can receive keyboard focus.");

                window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
                AnalyzeTextsLayoutTests.Settle(window);
                ReportedAppGaps.AssertStillReproduces(
                    nameof(WordCardArrowsRespectOccurrenceBoundariesAndChildInputs),
                    !ReferenceEquals(readingOccurrence, inText.SelectedToken));
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
