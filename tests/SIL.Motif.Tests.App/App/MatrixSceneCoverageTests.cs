using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class MatrixSceneCoverageTests
{
    [Fact]
    public void StandardScenesCoverEveryPage()
    {
        var expected = PageRegistry.Entries.Select(entry => entry.Page);
        var actual = PageScreenshots.Views().Select(view => view.Page).Distinct();

        Assert.Equal(expected.Order(), actual.Order());
    }

    [Fact]
    public void TextsAndWordsHaveSeparateScenes()
    {
        var pages = PageScreenshots.Views().Select(view => view.Page).ToArray();
        Assert.Single(pages, page => page == WorkspacePage.Texts);
        Assert.Single(pages, page => page == WorkspacePage.Words);
    }
}
