using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
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
    public void TheStripsFixMenuIsLeftAlignedRowsOfOneWidth()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await OpenAnalyzeTexts();
            try
            {
                workspace.PageModel<TextsPageModel>().ResultsInText.CloseTokenCard();
                Settle(window);
                var fix = Named<Button>(StripOf(Panel(window), "chakula"), "Fix actions from the word strip");
                AssertMenuRows(Open(fix, window), "the strip's Fix menu");
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    [Fact]
    public void TheCardsFixMenuIsLeftAlignedRowsOfOneWidth()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (_, window) = await OpenAnalyzeTexts();
            try
            {
                var fix = Named<Button>(OpenCard(window), "Fix actions for this word");
                AssertMenuRows(Open(fix, window), "the card's Fix menu");
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
