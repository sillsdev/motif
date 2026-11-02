using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the Texts matrix in a real window while a Proposal is open: where focus goes when the words under the
/// matrix hide, that the scroll position stays through a flip, and saves the moved outline and the Proposal view as images.
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
        for (var wait = 0; wait < 100 && workspace.Assess.Compare.ProposalWordsMatch is null; wait++)
        {
            await Task.Delay(20);
            PageScreenshots.Settle(window);
        }
        Assert.True(workspace.Assess.Compare.ProposalWordsMatch);
        PageScreenshots.Settle(window);
        return (workspace, window);
    }

    private static Control Part(MainWindow window, string name) =>
        window.GetVisualDescendants().OfType<Control>().First(control => control.Name == name);

    [Fact]
    public void FocusInTheChosenWordsMovesToAMatrixCellWhenTheProposalViewHidesThem()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenedAsync();
            try
            {
                var compare = workspace.Assess.Compare;
                compare.SelectCells([new(WordProjectStatus.Approved, CompareColumnKind.Match)]);
                PageScreenshots.Settle(window);
                var card = Part(window, "ChosenWordsCard");
                var inCard = card.GetVisualDescendants().OfType<InputElement>()
                    .First(element => element.Focusable && element.IsEffectivelyVisible && element.IsTabStop);
                Assert.True(inCard.Focus());

                workspace.Context.ShowProposalView(MarkupView.Proposal);
                PageScreenshots.Settle(window);
                await Task.Delay(50);
                PageScreenshots.Settle(window);

                Assert.False(card.IsEffectivelyVisible);
                var focused = Assert.IsAssignableFrom<Visual>(window.FocusManager!.GetFocusedElement());
                Assert.NotNull(focused.FindAncestorOfType<MatrixCell>(includeSelf: true));
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void FlippingBetweenTodayAndThePairKeepsTheScrollPosition()
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

                workspace.Context.ShowProposalView(MarkupView.Today);
                PageScreenshots.Settle(window);
                workspace.Context.ShowProposalView(MarkupView.TodayToProposal);
                PageScreenshots.Settle(window);

                Assert.Equal(offset, scroll.Offset);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [ScreenshotFact]
    public void CaptureThePairWithMovedCellsAndTheProposalView()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenedAsync();
            try
            {
                workspace.Assess.Compare.SelectCells([new(WordProjectStatus.Approved, CompareColumnKind.Match)]);
                Assert.Contains(workspace.Assess.Compare.Cells, cell => cell.IsMoved);
                foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
                {
                    Application.Current!.RequestedThemeVariant = variant;
                    window.Width = 1240;
                    window.Height = 900;
                    foreach (var (name, view) in new[] { ("today", MarkupView.Today), ("pair", MarkupView.TodayToProposal),
                        ("proposal", MarkupView.Proposal) })
                    {
                        workspace.Context.ShowProposalView(view);
                        PageScreenshots.Save(window, Path.Combine(folder, $"texts-matrix-{name}-{theme}.png"));
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
