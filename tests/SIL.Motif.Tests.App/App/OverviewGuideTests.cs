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
/// Pins that the Guide's Overview pages name the cards the window draws, by the titles the window gives them, and
/// make no claim about a Baseline on the page, whose freshness the top bar alone reports.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class OverviewGuideTests
{
    private static readonly string[] GuidePages = ["overview", "reading-the-overview"];

    private static readonly string[] RetiredCardNames = ["Accuracy", "Text Coverage", "**Timing**", "**Warnings**"];

    [Fact]
    public void TheOverviewGuidePagesNameTheWindowsCards()
    {
        var titles = RenderedTileTitles();
        Assert.Equal(["Speed", "Text coverage", "Approved analyses kept", "Grammar warnings"], titles);

        foreach (var name in GuidePages)
        {
            var guide = HelpCatalog.Load(CultureInfo.GetCultureInfo("en")).GetHelpPage(HelpEntryKind.Guide, name);
            Assert.NotNull(guide);
            foreach (var title in titles)
                Assert.True(guide.Contains($"**{title}**", StringComparison.Ordinal), $"{name} does not name {title}");
            foreach (var retired in RetiredCardNames)
                Assert.False(guide.Contains(retired, StringComparison.Ordinal), $"{name} still says {retired}");
            Assert.False(guide.Contains("its [Baseline]", StringComparison.Ordinal), $"{name} puts a Baseline on the page");
        }
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
                titles = window.GetLogicalDescendants().OfType<Button>()
                    .Where(button => button.Classes.Contains("overviewTile") && button.IsEffectivelyVisible)
                    .OrderBy(Grid.GetRow).ThenBy(Grid.GetColumn)
                    .Select(button => button.GetLogicalDescendants().OfType<TextBlock>()
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
