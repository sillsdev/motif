using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the Texts matrix in a real window while a Proposal is open: where focus goes when the chips above the
/// matrix hide, that the scroll position stays through every flip, and saves the moved outline and the Proposal view as images.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TextsProposalMatrixWindowTests
{
    private const string ProposalId = "proposal/sound";

    // The same words as today's, with two that the Proposal no longer parses.
    private static TrialRecord Trial(AssessCommandResponse today) => new("job/trial", ProposalId, "sha256:intent",
        PageScreenshots.Token(), null, new Dictionary<string, string> { [AssessmentKinds.ParseTime] = "trial-root" },
        [.. today.Words.Select(word => word.Word)], false, null, JobStatus.Completed,
        new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero), TrialFreshness.Current)
    {
        Label = "Sound change",
        WordResults = [.. today.Words.Select((word, index) => index >= today.Words.Count - 2
            ? word with { Outcome = "unassessed", Morphology = null, ReadingGrades = [] } : word)],
    };

    private static async Task<(WorkspaceShellViewModel Workspace, MainWindow Window)> OpenedAsync()
    {
        var today = PageScreenshots.Assessment();
        var (workspace, window) = await PageScreenshots.OpenOverSampleData(
            configure: (fake, _) => fake.ReadTrialCompletesWith(Trial(today)), capturedAssessment: today);
        workspace.CurrentPage = WorkspacePage.Texts;
        workspace.PageModel<TextsPageModel>().Tab = TextsTab.Matrix;
        await workspace.Context.OpenProposalAsync(ProposalId, CancellationToken.None);
        await Wait.UntilAsync(() => workspace.Assess.Compare.ProposalWordsMatch is not null,
            "The Matrix never compared the Proposal's words.", between: () => PageScreenshots.Settle(window));
        Assert.True(workspace.Assess.Compare.ProposalWordsMatch);
        PageScreenshots.Settle(window);
        return (workspace, window);
    }

    private static Control Part(MainWindow window, string name) =>
        window.GetVisualDescendants().OfType<Control>().First(control => control.Name == name);

    [Fact]
    public void FocusInTheChipsMovesToAMatrixCellWhenTheProposalViewHidesThem()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenedAsync();
            try
            {
                var chips = Part(window, "TodayWordsChips");
                var inChips = chips.GetVisualDescendants().OfType<InputElement>()
                    .First(element => element.Focusable && element.IsEffectivelyVisible && element.IsTabStop);
                Assert.True(inChips.Focus());

                workspace.Context.ShowProposalView(MarkupView.Proposal);
                await Wait.UntilAsync(
                    () => !chips.IsEffectivelyVisible && window.FocusManager!.GetFocusedElement() is Visual held &&
                        held.FindAncestorOfType<MatrixCell>(includeSelf: true) is not null,
                    "Focus never moved from the hidden Today chips to a Matrix cell.",
                    between: () => PageScreenshots.Settle(window));

                Assert.False(chips.IsEffectivelyVisible);
                var focused = Assert.IsAssignableFrom<Visual>(window.FocusManager!.GetFocusedElement());
                Assert.NotNull(focused.FindAncestorOfType<MatrixCell>(includeSelf: true));
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void FocusInTheChosenWordsStaysWhereItIsWhenTheProposalViewListsTheProposalsWords()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenedAsync();
            try
            {
                workspace.Assess.Compare.SelectCells([new(WordProjectStatus.Approved, CompareColumnKind.Match)]);
                PageScreenshots.Settle(window);
                var card = Part(window, "ChosenWordsCard");
                var search = Part(window, "MatrixWordSearch");
                Assert.True(search.Focus());

                workspace.Context.ShowProposalView(MarkupView.Proposal);
                PageScreenshots.Settle(window);
                await Task.Delay(50);
                PageScreenshots.Settle(window);

                Assert.True(card.IsEffectivelyVisible);
                Assert.Same(search, window.FocusManager!.GetFocusedElement());
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void FlippingThroughTodayThePairAndTheProposalKeepsTheScrollPosition()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenedAsync();
            try
            {
                window.Height = 420;
                workspace.Assess.Compare.SelectCells([new(WordProjectStatus.Approved, CompareColumnKind.Match)]);
                PageScreenshots.Settle(window);
                var scroll = (ScrollViewer)Part(window, "MatrixScroll");
                // Small enough to stay in range when the note above the page comes and goes with the view.
                Assert.True(scroll.Extent.Height - scroll.Viewport.Height > 20, "the page has nothing to scroll");
                scroll.Offset = new Vector(0, 10);
                PageScreenshots.Settle(window);
                Assert.True(scroll.Offset.Y > 0, "the page did not scroll");
                var offset = scroll.Offset;
                foreach (var view in new[] { MarkupView.Today, MarkupView.Proposal, MarkupView.TodayToProposal,
                    MarkupView.Proposal, MarkupView.Today })
                {
                    workspace.Context.ShowProposalView(view);
                    PageScreenshots.Settle(window);
                    Assert.True(scroll.Offset == offset, $"{view}: the page moved from {offset} to {scroll.Offset}");
                }
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void ReturningToTodayGivesTheWordListItsColumnWidthsBackAfterTheProposalViewChangedThem()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenedAsync();
            try
            {
                window.Width = 1240;
                window.Height = 1000;
                var compare = workspace.Assess.Compare;
                compare.SelectCells([new(WordProjectStatus.Approved, CompareColumnKind.NoMatch)]);
                workspace.Context.ShowProposalView(MarkupView.Today);
                PageScreenshots.Settle(window);
                string Widths()
                {
                    var layout = window.GetVisualDescendants().OfType<WordListSlot>()
                        .First(slot => slot.Schema == compare.WordListSchema).CurrentLayout!;
                    return string.Join(" ", layout.Schema.OrderedColumns(layout.ShowsMeaning)
                        .Select(column => $"{column}={layout.WidthOf(column)}"));
                }
                var today = Widths();

                workspace.Context.ShowProposalView(MarkupView.Proposal);
                PageScreenshots.Settle(window);
                workspace.Context.ShowProposalView(MarkupView.Today);
                PageScreenshots.Settle(window);

                Assert.Equal(today, Widths());
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void InTheStackedPairRowTheWithThisProposalValueSitsOnTheLineOfItsCaption()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenedAsync();
            try
            {
                window.Width = 1240;
                window.Height = 1000;
                workspace.Assess.Compare.SelectCells([new(WordProjectStatus.Approved, CompareColumnKind.NoMatch)]);
                workspace.Context.ShowProposalView(MarkupView.TodayToProposal);
                PageScreenshots.Settle(window);

                var wrapper = window.GetVisualDescendants().OfType<Grid>().First(grid =>
                    grid.Classes.Contains("wordPresentationStackedCell") &&
                    Avalonia.Automation.AutomationProperties.GetName(grid) == "With This Proposal");
                var caption = wrapper.Children.OfType<TextBlock>().Single();
                var glyph = wrapper.GetVisualDescendants().OfType<MarkGlyph>().Single();
                var text = wrapper.GetVisualDescendants().OfType<CopyableTextBlock>().Single();
                double Middle(Control control) => control.TranslatePoint(new Point(0, control.Bounds.Height / 2), window)!.Value.Y;
                var top = caption.TranslatePoint(new Point(0, 0), window)!.Value.Y;

                Assert.Equal("Different", text.Text);
                foreach (var part in new Control[] { glyph, text })
                    Assert.True(Middle(part) >= top && Middle(part) <= top + caption.Bounds.Height,
                        $"{part.GetType().Name} sits at {Middle(part)}, off the caption's line {top}..{top + caption.Bounds.Height}");
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    private static string NameOf(Control control) => Avalonia.Automation.AutomationProperties.GetName(control) ?? string.Empty;

    [Fact]
    public void ARowsNamesSayWhoseNumberEachIsInThePairAndTheProposalView()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenedAsync();
            try
            {
                window.Width = 1600;
                window.Height = 1000;
                workspace.Assess.Compare.SelectCells([new(WordProjectStatus.Approved, CompareColumnKind.NoMatch)]);
                IEnumerable<T> Of<T>() where T : Control => window.GetVisualDescendants().OfType<T>().Where(control => control.IsEffectivelyVisible);

                workspace.Context.ShowProposalView(MarkupView.TodayToProposal);
                PageScreenshots.Settle(window);
                var pair = NameOf(Of<SIL.Motif.App.Controls.WordPresentation.WordRow>().First());
                Assert.Contains("PanGloss today: ", pair);
                Assert.Contains("With this Proposal: ", pair);

                workspace.Context.ShowProposalView(MarkupView.Proposal);
                PageScreenshots.Settle(window);
                var alone = NameOf(Of<SIL.Motif.App.Controls.WordPresentation.WordRow>().First());
                Assert.DoesNotContain("PanGloss", alone);
                Assert.Contains("With this Proposal: ", alone);
                Assert.StartsWith("Time with this Proposal: ", NameOf(Of<CopyableTextBlock>().First(text => text.Classes.Contains("wordPresentationTime"))));
                Assert.Contains(Of<NamedMark>(), mark => NameOf(mark).StartsWith("With This Proposal: ", StringComparison.Ordinal));
                Assert.Contains(Of<CopyableTextBlock>(), text => text.Classes.Contains("wordPresentationHeading") && text.Text == "Proposal Time");
                Assert.All(Of<CopyableTextBlock>().Where(text => text.Classes.Contains("wordPresentationTime")),
                    text => Assert.Equal(Avalonia.Media.TextTrimming.None, text.TextTrimming));
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [ScreenshotFact]
    public void CaptureThePairWithMovedCellsAndTheWordsInEachView()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenedAsync();
            try
            {
                var compare = workspace.Assess.Compare;
                var views = new[] { ("today", MarkupView.Today), ("pair", MarkupView.TodayToProposal), ("proposal", MarkupView.Proposal) };
                var cells = new[] { ("different", CompareColumnKind.NoMatch), ("notparsed", CompareColumnKind.Skipped) };
                foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
                {
                    Application.Current!.RequestedThemeVariant = variant;
                    window.Width = 1240;
                    window.Height = 1000;
                    foreach (var (cellName, column) in cells)
                    {
                        compare.SelectCells([new(WordProjectStatus.Approved, column)]);
                        foreach (var (name, view) in views)
                        {
                            workspace.Context.ShowProposalView(view);
                            PageScreenshots.Save(window, Path.Combine(folder, $"texts-{cellName}-{name}-{theme}.png"));
                        }
                    }
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
            }
        }, TimeSpan.FromMinutes(3));
    }
}
