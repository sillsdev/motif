using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App.TextViewParity;

/// <summary>
/// Keyboard reach: Tab visits every word, constituent and enabled action in reading order in both directions, and the
/// reader's arrow, Enter and Escape keys ask the host for the right movement.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class KeyboardTests(LineScenes lines, CardScenes cards) : IClassFixture<LineScenes>, IClassFixture<CardScenes>
{
    private static void Run(Action work) => AvaloniaHeadlessPlatform.Invoke(work);

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void TabVisitsTheWordsInReadingOrder(string name) => Run(() =>
    {
        var scene = lines.Get(name);
        var stops = scene.TabThroughEveryWord();
        var visited = scene.WalkWords.Distinct().ToList();
        Assert.Equal(Enumerable.Range(0, 4), visited);
        // Reading order of the words on screen: top to bottom, and along each row in the text's direction.
        var onScreen = Enumerable.Range(0, 4)
            .OrderBy(i => Math.Round(scene.WordBounds(i).Top))
            .ThenBy(i => scene.Sample.RightToLeft ? -scene.WordBounds(i).Right : scene.WordBounds(i).Left).ToList();
        Assert.Equal(onScreen, visited);
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void TabReachesEveryWordStripConstituentAndEnabledActionOfEachWord(string name) => Run(() =>
    {
        var scene = lines.Get(name);
        var stops = scene.TabThroughEveryWord();
        for (var word = 0; word < 4; word++)
        {
            var inWord = stops.Where((_, i) => scene.WalkWords[i] == word).ToList();
            Assert.Same(scene.Word(word), inWord[0]);
            var constituents = scene.SemanticConstituents(word).Select(c => (c.Owner, c.RunIndex)).ToList();
            Assert.Equal(constituents, scene.SemanticStops(stops, word));
            var buttons = inWord.OfType<Button>().ToList();
            Assert.NotEmpty(buttons);
            Assert.All(buttons, button => Assert.True(button.IsEnabled));
        }
    });

    [Fact]
    public void AnActionTheWordCannotPerformYetIsLeftOutOfTheTabOrderNotHidden() => Run(() =>
    {
        var scene = lines.Get("ltr");
        scene.Word(0).FocusWord();
        var disabled = TextViewScene.ActionsOf(scene.Word(0)).Where(a => !a.IsEffectivelyEnabled).ToList();
        var stops = scene.TabThroughEveryWord();
        Assert.All(disabled, action => Assert.DoesNotContain(action, stops));
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void ShiftTabWalksTheSameStopsBackwards(string name) => Run(() =>
    {
        var scene = lines.Get(name);
        var forward = scene.TabThroughEveryWord();
        var backward = scene.TabFrom((Control)forward[^1], backwards: true, inside: e => scene.WordIndexOf(e) >= 0);
        Assert.Equal(forward.AsEnumerable().Reverse(), backward);
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void ReaderKeysAskTheHostForTheMatchingMovementFromTheFocusedWord(string name) => Run(() =>
    {
        var scene = lines.Get(name);
        var key = scene.TokenOf(1).PresentationKey;
        var expected = new (Key Key, WordAction Action)[]
        {
            (Key.Right, scene.Sample.RightToLeft ? WordAction.NavigatePrevious : WordAction.NavigateNext),
            (Key.Left, scene.Sample.RightToLeft ? WordAction.NavigateNext : WordAction.NavigatePrevious),
            (Key.Down, WordAction.NavigateNextLine), (Key.Up, WordAction.NavigatePreviousLine),
            (Key.PageDown, WordAction.NavigateNextPage), (Key.PageUp, WordAction.NavigatePreviousPage),
            (Key.Home, WordAction.NavigateFirst), (Key.End, WordAction.NavigateLast),
            (Key.Space, WordAction.OpenCard), (Key.Escape, WordAction.CloseCard),
        };
        var wrong = new List<string>();
        foreach (var (pressed, action) in expected)
        {
            scene.Host.Requests.Clear();
            scene.Word(1).FocusWord();
            Assert.Equal(1, scene.WordIndexOf(scene.Focused!));
            scene.Press(pressed);
            var got = scene.Host.Requests.Select(r => $"{r.Action}/{r.Key == key}").ToList();
            if (!got.SequenceEqual([$"{action}/True"])) wrong.Add($"{pressed}: wanted {action}, got [{string.Join(",", got)}]");
        }
        Assert.Empty(wrong);
    });

    [Fact]
    public void EnterOpensTheWordCardLikeSpace() => Run(() =>
    {
        var scene = lines.Get("ltr");
        scene.Word(1).FocusWord();
        scene.Press(Key.Enter);
        var request = Assert.Single(scene.Host.Requests);
        Assert.Equal(WordAction.OpenCard, request.Action);
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void ReaderKeysWorkWhileASelectableConstituentOfTheWordHoldsFocus(string name) => Run(() =>
    {
        var scene = lines.Get(name);
        var gloss = scene.SemanticConstituent(2, Part.FieldWorksGloss, 1);
        scene.FocusSemantic(gloss);
        scene.Press(Key.Right);
        var request = Assert.Single(scene.Host.Requests);
        Assert.Equal(scene.Sample.RightToLeft ? WordAction.NavigatePrevious : WordAction.NavigateNext, request.Action);
        Assert.Equal(scene.TokenOf(2).PresentationKey, request.Key);
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void HorizontalArrowsMoveTheOpenCardInTheTextsVisualDirection(string name) => Run(() =>
    {
        var scene = cards.Get(name);
        scene.TheCard.HandlesTextReaderShortcuts = true;
        foreach (var (key, action) in new[]
        {
            (Key.Left, scene.Sample.RightToLeft ? WordAction.NavigateCardNext : WordAction.NavigateCardPrevious),
            (Key.Right, scene.Sample.RightToLeft ? WordAction.NavigateCardPrevious : WordAction.NavigateCardNext),
        })
        {
            scene.Host.Requests.Clear();
            scene.TheCard.FocusCard();
            scene.Press(key);
            Assert.Equal(action, Assert.Single(scene.Host.Requests).Action);
        }
        scene.TheCard.HandlesTextReaderShortcuts = false;
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void TabReachesEveryEnabledActionInTheCardInDrawnOrder(string name) => Run(() =>
    {
        var scene = cards.Get(name);
        var card = scene.TheCard;
        var expected = TextViewScene.ActionsOf(card).Where(a => a.IsEffectivelyEnabled && a.IsTabStop).Cast<object>().ToList();
        Assert.NotEmpty(expected);
        var stops = scene.TabFrom(card, backwards: false, inside: e => e is Visual v && card.IsVisualAncestorOf(v));
        Assert.Equal(expected, stops.OfType<Button>().Cast<object>().ToList());
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void TheCardsConstituentsAreTabStopsInTheOrderTheyAreDrawn(string name) => Run(() =>
    {
        var scene = cards.Get(name);
        var card = scene.TheCard;
        var expected = scene.CardConstituents().Select(c => (object)c.Visual).ToList();
        var stops = scene.TabFrom(card, backwards: false, inside: e => e is Visual v && card.IsVisualAncestorOf(v));
        Assert.Equal(expected, stops.OfType<CopyableTextBlock>().Where(b => expected.Contains(b)).Cast<object>().ToList());
    });
}
