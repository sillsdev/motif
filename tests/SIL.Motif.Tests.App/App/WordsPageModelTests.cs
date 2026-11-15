using SIL.Motif.App.ViewModels;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class WordsPageModelTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WordsAndTextsShareTheSameSelectionRowsInEitherConstructionOrder(bool wordsFirst)
    {
        var context = WorkspaceContextTests.NewContext(new FakeCommandClient());
        WordsPageModel words;
        TextsPageModel texts;
        if (wordsFirst)
        {
            words = new WordsPageModel(context);
            texts = new TextsPageModel(context);
        }
        else
        {
            texts = new TextsPageModel(context);
            words = new WordsPageModel(context);
        }

        Assert.Same(words.Words, texts.Words);
        Assert.Same(words.Words, context.Assess.TextWords);
    }
}
