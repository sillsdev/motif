using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;
using static SIL.Motif.Tests.App.AnalyzeTextsLayoutTests;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Opens Analyze texts' menus over the sample project and checks each reads as a menu: left-aligned entries of one
/// width rather than loose centred buttons, and Select ▾ showing only commands that can apply now.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class AnalyzeTextsMenuTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

    [Fact]
    public void TheStripsDispositionMenuHasTilesAndLeftAlignedRows()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts();
            try
            {
                workspace.PageModel<TextsPageModel>().ResultsInText.CloseTokenCard();
                Settle(window);
                var mark = Named<Button>(StripOf(Panel(window), "chakula"), WordDispositionButtons.MarkButtonName);
                AssertDispositionMenu(Open(mark, window), "the strip's disposition menu");
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void TheCardsDispositionMenuHasTilesAndLeftAlignedRows()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (_, window) = await OpenAnalyzeTexts();
            try
            {
                var mark = Named<Button>(OpenCard(window), WordDispositionButtons.MarkButtonName);
                AssertDispositionMenu(Open(mark, window), "the card's disposition menu");
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void SelectShowsOnlyCommandsThatCanApplyAsGroupedRows()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (_, window) = await OpenAnalyzeTexts();
            try
            {
                var select = Named<Button>(Panel(window), "Select words for actions");
                var menu = Open(select, window);
                var shown = Entries(menu);
                Assert.NotEmpty(shown);
                Assert.All(shown, entry => Assert.True(entry.IsEffectivelyEnabled,
                    $"Select ▾ shows \"{Label(entry)}\", which cannot apply now."));
                foreach (var group in shown.GroupBy(entry => entry.GetVisualParent()))
                    AssertMenuRows(group.ToArray(), "a Select ▾ group");
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    private static Control Open(Button button, Window window)
    {
        var flyout = Assert.IsAssignableFrom<Flyout>(button.Flyout);
        flyout.ShowAt(button);
        Settle(window);
        Assert.True(flyout.IsOpen);
        return FlyoutContent.Of(flyout);
    }

    private static Button[] Entries(Control menu) =>
        menu.GetVisualDescendants().OfType<Button>()
            .Where(button => button.IsEffectivelyVisible && button.FindAncestorOfType<Expander>() is null)
            .ToArray();

    private static void AssertMenuRows(Control menu, string what) => AssertMenuRows(Entries(menu), what);

    // The opinion tiles sit side by side in fours; the menu's other entries read as left-aligned rows.
    private static void AssertDispositionMenu(Control menu, string what)
    {
        var entries = Entries(menu);
        var tiles = entries.Where(entry => entry.Classes.Contains("dispositionButton")).ToArray();
        var stored = Assert.IsType<ResultsTokenViewModel>(menu.DataContext).Disposition.StoredRows.Count;
        Assert.True(tiles.Length == stored * 4, $"{what} shows {tiles.Length} opinion tiles for {stored} stored analyses.");
        AssertMenuRows(entries.Where(entry => entry.Classes.Contains("menuEntry")).ToArray(), what);
    }

    private static void AssertMenuRows(Button[] entries, string what)
    {
        Assert.True(entries.Length > 0, $"{what} has no entries.");
        Assert.All(entries, entry => Assert.Equal(HorizontalAlignment.Left, entry.HorizontalContentAlignment));
        var widths = entries.Select(entry => Math.Round(entry.Bounds.Width)).Distinct().ToArray();
        Assert.True(widths.Length == 1, $"{what} has entries {string.Join(", ", widths)} px wide.");
        var lefts = entries.Select(entry => Math.Round(entry.TranslatePoint(default, entries[0])!.Value.X))
            .Distinct().ToArray();
        Assert.True(lefts.Length == 1, $"{what} has entries starting at {string.Join(", ", lefts)}.");
    }

    private static string Label(Button button) => button.Content as string
        ?? string.Join(" ", button.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));
}
