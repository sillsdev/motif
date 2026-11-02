using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the Moved By This Proposal chip in a real window on the Matrix and Lists tabs: it shows in the pair and the
/// Proposal view, not in Today, choosing it narrows the rows of both tabs, and the chip shows it is on. It also saves
/// both tabs with the chip chosen as images, in Light and Dark.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TextsProposalMovedChipWindowTests
{
    private static Control Part(MainWindow window, string name) => TextsProposalTabsWindowTests.Part(window, name);

    private static int Rows(MainWindow window, string name) => ((ListBox)Part(window, name)).ItemCount;

    // Chooses the first list the Proposal moves a word out of or into, so the Lists tab has a list to narrow.
    private static void ChooseAListWithMoves(TextsPageModel page)
    {
        foreach (var list in page.TextsLists.Lists.Where(list => list.HasWords))
        {
            page.TextsLists.SelectListCommand.Execute(list);
            if (page.Assess.Compare.MovedChipCount > 0) return;
        }
        throw new InvalidOperationException("No list holds a word the Proposal moves.");
    }

    [Theory]
    [InlineData(TextsTab.Lists, "TextsListsMovedChip", "TextsListsPanelWordsItems")]
    [InlineData(TextsTab.Matrix, "ComparePanelMovedChip", "ComparePanelWordsItems")]
    public void TheChipShowsInThePairAndTheProposalViewNotInTodayAndNarrowsTheRowsOfItsTab(
        TextsTab tab, string chip, string rows)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await TextsProposalTabsWindowTests.OpenedAsync(tab);
            try
            {
                var compare = workspace.Assess.Compare;
                workspace.Context.ShowProposalView(MarkupView.TodayToProposal);
                if (tab == TextsTab.Lists) ChooseAListWithMoves(workspace.PageModel<TextsPageModel>());
                workspace.Context.ShowProposalView(MarkupView.Today);
                PageScreenshots.Settle(window);
                Assert.False(Part(window, chip).IsEffectivelyVisible);

                foreach (var view in new[] { MarkupView.TodayToProposal, MarkupView.Proposal })
                {
                    workspace.Context.ShowProposalView(view);
                    if (tab == TextsTab.Lists) ChooseAListWithMoves(workspace.PageModel<TextsPageModel>());
                    PageScreenshots.Settle(window);
                    Assert.True(Part(window, chip).IsEffectivelyVisible);
                    var before = Rows(window, rows);
                    var moved = compare.MovedChipCount;
                    Assert.True(moved > 0 && (tab == TextsTab.Lists ? moved <= before : moved < before));

                    compare.ToggleMovedFilterCommand.Execute(null);
                    PageScreenshots.Settle(window);
                    Assert.Equal(moved, Rows(window, rows));
                    Assert.True(((FilterChip)Part(window, chip)).IsActive);
                    Assert.Contains(MotifAppStrings.TextsMovedChipLabel, TextsProposalTabsWindowTests.Texts(Part(window, chip)));
                    compare.ToggleMovedFilterCommand.Execute(null);
                    PageScreenshots.Settle(window);
                    Assert.Equal(before, Rows(window, rows));
                }
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }

    [ScreenshotFact]
    public void CaptureTheMatrixAndListsTabsWithTheMovedChipChosenInLightAndDark()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await TextsProposalTabsWindowTests.OpenedAsync(TextsTab.Lists);
            try
            {
                var page = workspace.PageModel<TextsPageModel>();
                var compare = workspace.Assess.Compare;
                foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
                {
                    Application.Current!.RequestedThemeVariant = variant;
                    window.Width = 1240;
                    window.Height = 1000;
                    foreach (var (tab, tabName) in new[] { (TextsTab.Matrix, "matrix"), (TextsTab.Lists, "lists") })
                    {
                        page.Tab = tab;
                        foreach (var (name, view) in new[] { ("pair", MarkupView.TodayToProposal), ("proposal", MarkupView.Proposal) })
                        {
                            workspace.Context.ShowProposalView(view);
                            if (tab == TextsTab.Lists) ChooseAListWithMoves(page);
                            else compare.ClearSelectionCommand.Execute(null);
                            foreach (var on in new[] { false, true })
                            {
                                if (compare.IsMovedFilterOn != on) compare.ToggleMovedFilterCommand.Execute(null);
                                PageScreenshots.Settle(window);
                                PageScreenshots.Save(window, Path.Combine(folder,
                                    $"texts-{tabName}-{name}-moved-{(on ? "on" : "off")}-{theme}.png"));
                            }
                            if (compare.IsMovedFilterOn) compare.ToggleMovedFilterCommand.Execute(null);
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
