using System.Globalization;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Help;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Ensures the canonical Overview guide names every card and the reading guide links back to its full page tour.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class OverviewGuideTests
{
    private static readonly string[] RetiredCardNames = ["Accuracy", "Text Coverage", "**Timing**", "**Warnings**"];

    [Fact]
    public void TheOverviewGuideNamesEveryCardAndTheReadingGuideLinksBack()
    {
        var titles = RenderedTileTitles();
        Assert.Equal(["Speed", "Text coverage", "Approved analyses kept", "Grammar warnings"], titles);

        var catalog = HelpCatalog.Load(CultureInfo.GetCultureInfo("en"));
        var guide = catalog.GetHelpPage(HelpEntryKind.Guide, "overview");
        Assert.NotNull(guide);
        foreach (var title in titles)
            Assert.True(guide.Contains($"**{title}**", StringComparison.Ordinal), $"overview does not name {title}");
        foreach (var retired in RetiredCardNames)
            Assert.False(guide.Contains(retired, StringComparison.Ordinal), $"overview still says {retired}");
        Assert.False(guide.Contains("its [Baseline]", StringComparison.Ordinal), "overview puts a Baseline on the page");

        var readingGuide = catalog.GetHelpPage(HelpEntryKind.Guide, "reading-the-overview");
        Assert.NotNull(readingGuide);
        Assert.Contains("[Overview](guide:overview)", readingGuide);
    }

    private static string[] RenderedTileTitles()
    {
        string[] titles = [];
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            var context = WorkspaceContextTests.NewContext(fake);
            var page = new OverviewPageModel(context);
            fake.OverviewCompletesWith(OverviewPageWordsTests.Populated());
            await context.OpenProjectAsync(@"C:\projects\sample.fwdata");
            var window = new Window { Width = 1240, Height = 900, Content = new OverviewPage(page) };
            try
            {
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                titles = window.GetLogicalDescendants().OfType<Control>()
                    .Where(tile => tile.Classes.Contains("overviewTile") && tile.IsEffectivelyVisible)
                    .OrderBy(Grid.GetRow).ThenBy(Grid.GetColumn)
                    .Select(tile => tile.GetLogicalDescendants().OfType<TextBlock>()
                        .Single(text => text.Classes.Contains("overviewTileTitle")).Text!)
                    .ToArray();
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(10));
        return titles;
    }

}
