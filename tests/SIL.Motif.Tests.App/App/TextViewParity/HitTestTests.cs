using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls.WordPresentation;
using Xunit;

namespace SIL.Motif.Tests.App.TextViewParity;

/// <summary>Pointing at a word or one of its constituents reaches that word, and a click opens its card.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class HitTestTests(LineScenes lines, CardScenes cards) : IClassFixture<LineScenes>, IClassFixture<CardScenes>
{
    private static void Run(Action work) => AvaloniaHeadlessPlatform.Invoke(work);

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void ThePointAtTheCentreOfEachWordIsInThatWord(string name) => Run(() =>
    {
        var scene = lines.Get(name);
        for (var i = 0; i < 4; i++)
        {
            var hit = scene.VisualAt(scene.WordBounds(i).Center);
            Assert.True(hit is IInputElement && scene.WordIndexOf((IInputElement)hit) == i, $"The centre of word {i} hits word {scene.WordIndexOf(hit as IInputElement)}.");
        }
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void ThePointAtTheCentreOfEachConstituentIsInsideIt(string name) => Run(() =>
    {
        var scene = lines.Get(name);
        var others = scene.Constituents(1);
        foreach (var constituent in others)
        {
            var hit = scene.VisualAt(scene.Centre(constituent));
            Assert.True(hit is not null && Reaches(constituent, hit),
                $"{constituent.Part} {constituent.Morph} '{constituent.Text}' is not what the pointer reaches at its centre ({scene.Bounds(constituent.Visual)} in a {scene.Window.Width} window): {hit?.GetType().Name}.");
        }
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void AClickAtTheCentreOfEachConstituentOpensThatWordsCard(string name) => Run(() =>
    {
        var scene = lines.Get(name);
        var key = scene.TokenOf(2).PresentationKey;
        var failed = new List<string>();
        foreach (var constituent in scene.Constituents(2))
        {
            scene.Host.Requests.Clear();
            scene.Click(scene.Centre(constituent));
            if (!scene.Host.Requests.Any(r => r.Action == WordAction.OpenCard && r.Key == key))
                failed.Add($"{constituent.Part} {constituent.Morph}");
        }
        Assert.Empty(failed);
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void AClickOnAWordOpensOnlyThatWord(string name) => Run(() =>
    {
        var scene = lines.Get(name);
        for (var i = 0; i < 4; i++)
        {
            scene.Host.Requests.Clear();
            scene.Click(scene.Centre(scene.Constituent(i, Part.Word)));
            var opened = scene.Host.Requests.Where(r => r.Action == WordAction.OpenCard).Select(r => r.Key).ToList();
            Assert.Equal([scene.TokenOf(i).PresentationKey], opened);
        }
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void AClickOnAnActionRunsTheActionWithoutOpeningTheCard(string name) => Run(() =>
    {
        var scene = lines.Get(name);
        scene.Word(1).FocusWord();
        scene.Settle();
        var action = TextViewScene.ActionsOf(scene.Word(1)).OfType<Button>().First(b => b.IsEffectivelyEnabled);
        scene.Click(scene.Bounds(action).Center);
        Assert.DoesNotContain(scene.Host.Requests, r => r.Action == WordAction.OpenCard);
    });

    [Theory]
    [InlineData("ltr")]
    public void ThePointAtTheCentreOfEachCardConstituentIsInsideIt(string name) => Run(() =>
    {
        var scene = cards.Get(name);
        var others = scene.CardConstituents();
        foreach (var constituent in others)
        {
            var hit = scene.VisualAt(scene.Centre(constituent));
            Assert.True(hit is not null && Reaches(constituent, hit),
                $"{constituent.Part} {constituent.Morph} '{constituent.Text}' is not what the pointer reaches at its centre ({scene.Bounds(constituent.Visual)} in a {scene.Window.Width} window): {hit?.GetType().Name}.");
        }
    });

    [Fact]
    public void TheRightToLeftCardsConstituentsAreHitAtTheirCentres() => Run(() =>
    {
        var scene = cards.Get("rtl");
        var others = scene.CardConstituents();
        foreach (var constituent in others)
        {
            var hit = scene.VisualAt(scene.Centre(constituent));
            Assert.True(hit is not null && Reaches(constituent, hit),
                $"{constituent.Part} {constituent.Morph} '{constituent.Text}' is not what the pointer reaches at its centre ({scene.Bounds(constituent.Visual)} in a {scene.Window.Width} window): {hit?.GetType().Name}.");
        }
    });

    /// <summary>True when the pointer reaches the constituent or a visual over it.</summary>
    private static bool Reaches(Constituent constituent, Visual hit) =>
        ReferenceEquals(hit, constituent.Visual) || constituent.Visual.IsVisualAncestorOf(hit);
}
