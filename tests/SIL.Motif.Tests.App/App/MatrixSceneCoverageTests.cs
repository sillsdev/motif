using SIL.Motif.App.ViewModels;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class MatrixSceneCoverageTests
{
    [Fact]
    public void StandardScenesCoverEveryPage()
    {
        var expected = Enum.GetValues<WorkspacePage>().Where(page => page != WorkspacePage.AiHandoff);
        var actual = PageScreenshots.Views().Select(view => view.Page).Distinct();

        Assert.Equal(expected.Order(), actual.Order());
    }

    [Fact]
    public void TextsScenesCoverEveryTab()
    {
        var actual = PageScreenshots.Views().Where(view => view.Page == WorkspacePage.Texts).Select(view => view.Tab);

        Assert.Equal(Enum.GetValues<TextsTab>().Order(), actual.Order());
    }
}
