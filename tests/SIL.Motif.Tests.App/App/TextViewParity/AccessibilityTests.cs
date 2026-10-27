using Avalonia.Controls;
using Xunit;

namespace SIL.Motif.Tests.App.TextViewParity;

/// <summary>
/// What a screen reader meets today, recorded as the contract: each word is named by its summary, and the peers
/// below it name every constituent and action. Headless cannot prove that a real screen reader reads these well.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class AccessibilityTests(LineScenes lines, CardScenes cards) : IClassFixture<LineScenes>, IClassFixture<CardScenes>
{
    private static void Run(Action work) => AvaloniaHeadlessPlatform.Invoke(work);

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void EachWordIsNamedByItsSummaryAndTheNamesDiffer(string name) => Run(() =>
    {
        var scene = lines.Get(name);
        var names = Enumerable.Range(0, 4).Select(i => TextViewScene.PeerOf(scene.AccessibleWord(i)).GetName()).ToList();
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(scene.TokenOf(i).HoverSummary, names[i]);
            Assert.False(string.IsNullOrWhiteSpace(names[i]));
        }
        Assert.Equal(4, names.Distinct().Count());
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void TheNamedWordMentionsItsFormSoItIsFoundByWhatItSays(string name) => Run(() =>
    {
        var scene = lines.Get(name);
        for (var i = 0; i < 4; i++)
            Assert.Contains(scene.Sample.WordText(i), TextViewScene.PeerOf(scene.AccessibleWord(i)).GetName());
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void ThePeersBelowAWordNameEveryConstituentText(string name) => Run(() =>
    {
        var scene = lines.Get(name);
        var names = TextViewScene.PeerNames(scene.Word(1));
        foreach (var constituent in scene.Constituents(1).Where(c => c.Text.Length > 0))
            Assert.Contains(names, n => n.Contains(constituent.Text, StringComparison.Ordinal));
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void EveryActionOfAWordHasAName(string name) => Run(() =>
    {
        var scene = lines.Get(name);
        var unnamed = TextViewScene.ActionsOf(scene.Word(1))
            .Where(action => string.IsNullOrWhiteSpace(TextViewScene.PeerOf(action).GetName())).Select(a => a.GetType().Name).ToList();
        Assert.Empty(unnamed);
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void ThePeersOfTheCardNameEveryConstituentAndEveryAction(string name) => Run(() =>
    {
        var scene = cards.Get(name);
        var names = TextViewScene.PeerNames(scene.TheCard);
        foreach (var constituent in scene.CardConstituents().Where(c => c.Text.Length > 0))
            Assert.Contains(names, n => n.Contains(constituent.Text, StringComparison.Ordinal));
        var unnamed = TextViewScene.ActionsOf(scene.TheCard)
            .Where(action => string.IsNullOrWhiteSpace(TextViewScene.PeerOf(action).GetName())).Select(a => a.GetType().Name).ToList();
        Assert.Empty(unnamed);
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void TheWordsViewIsFocusableAndEachSelectableConstituentIsReachableByPeer(string name) => Run(() =>
    {
        var scene = lines.Get(name);
        var word = scene.AccessibleWord(2);
        Assert.True(word.Focusable);
        Assert.True(word.IsEffectivelyEnabled);
        Assert.All(scene.Constituents(2), c => Assert.NotNull(TextViewScene.PeerOf(c.Visual)));
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void AnUnfocusedWordExposesNoActionButtonsToScreenReadersUntilItHasFocus(string name) => Run(() =>
    {
        var scene = lines.Get(name);
        for (var word = 0; word < 4; word++)
            Assert.Empty(TextViewScene.DispositionButtonsOf(scene.Word(word)));
        scene.Word(1).FocusWord();
        scene.Settle();
        var buttons = TextViewScene.DispositionButtonsOf(scene.Word(1));
        Assert.NotEmpty(buttons);
        Assert.All(buttons, button => Assert.False(string.IsNullOrWhiteSpace(TextViewScene.PeerOf(button).GetName())));
        Assert.Contains(TextViewScene.PeerNames(scene.Word(1)), n => n == TextViewScene.PeerOf(buttons[0]).GetName());
        Assert.Empty(TextViewScene.DispositionButtonsOf(scene.Word(0)));
    });
}
