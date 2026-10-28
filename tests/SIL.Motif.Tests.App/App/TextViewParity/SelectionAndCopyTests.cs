using Avalonia.Controls;
using Xunit;

namespace SIL.Motif.Tests.App.TextViewParity;

/// <summary>
/// "Can select anything": every word, morpheme form, gloss, category and PanGloss part can be selected and copied,
/// whole or in part, from the keyboard or the pointer, in left-to-right and right-to-left text.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SelectionAndCopyTests(WordScenes words, CardScenes cards) :
    IClassFixture<WordScenes>, IClassFixture<CardScenes>
{
    private static void Run(Action work) => AvaloniaHeadlessPlatform.Invoke(work);

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void AWordOffersItsWordFormAndEveryMorphemeFormAndGlossOfBothProducersToSelect(string name) =>
        AvaloniaHeadlessPlatform.Invoke(() =>
        {
            var scene = words.Get(name);
            var sample = scene.Sample;
            var constituents = scene.SemanticConstituents(0);
            Assert.Equal([sample.WordText(0)], constituents.Where(c => c.Part == Part.Word).Select(c => c.Text));
            Assert.Equal(sample.StoredForms(0), constituents.Where(c => c.Part == Part.FieldWorksForm).Select(c => c.Text));
            Assert.Equal(sample.ParserForms(0), constituents.Where(c => c.Part == Part.PanGlossForm).Select(c => c.Text));
            Assert.Equal(sample.StoredForms(0).Select(form => sample.Glosses[Array.IndexOf(sample.Forms, form)]),
                constituents.Where(c => c.Part == Part.FieldWorksGloss).Select(c => c.Text));
            Assert.Equal(sample.ParserForms(0).Select(form => sample.Glosses[Array.IndexOf(sample.Forms, form)]),
                constituents.Where(c => c.Part == Part.PanGlossGloss).Select(c => c.Text));
            Assert.All(constituents, c => Assert.Equal(c.Text, scene.CopySemanticAll(c)));
        });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void EveryConstituentOfAWordCopiesItsWholeTextFromTheKeyboard(string name) => Run(() =>
    {
        var scene = words.Get(name);
        var all = scene.SemanticConstituents(0);
        Assert.NotEmpty(all);
        foreach (var constituent in all)
            Assert.Equal(constituent.Text, scene.CopySemanticAll(constituent));
        Assert.Empty(scene.Host.Requests);

    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void ASubstringInsideAGlossCopiesExactlyThatSubstring(string name) => Run(() =>
    {
        var scene = words.Get(name);
        var gloss = scene.SemanticConstituent(0, Part.PanGlossGloss, 3);
        Assert.True(gloss.Text.Length >= 4, "The sample gloss needs at least four characters to select inside.");
        Assert.Equal(gloss.Text[1..^1], scene.CopySemanticRange(gloss, 1, gloss.Text.Length - 1));
        var storedGloss = scene.SemanticConstituent(0, Part.FieldWorksGloss, 2);
        Assert.Equal(storedGloss.Text[..1], scene.CopySemanticRange(storedGloss, 0, 1));
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void ASubstringInsideAFormCopiesExactlyThatSubstring(string name) => Run(() =>
    {
        var scene = words.Get(name);
        var form = scene.SemanticConstituent(0, Part.FieldWorksForm, 2);
        Assert.True(form.Text.Length >= 3);
        Assert.Equal(form.Text[..2], scene.CopySemanticRange(form, 0, 2));
        var word = scene.SemanticConstituent(0, Part.Word);
        Assert.Equal(word.Text[1..4], scene.CopySemanticRange(word, 1, 4));
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void DraggingAcrossAGlossSelectsItsStartInReadingOrderAndOpensNothing(string name) => Run(() =>
    {
        var scene = words.Get(name);
        var gloss = scene.SemanticConstituent(0, Part.PanGlossGloss, 3);
        var copied = scene.CopySemanticDrag(gloss, 0.3);
        Assert.False(string.IsNullOrEmpty(copied), "The drag selected nothing.");
        Assert.StartsWith(copied!, gloss.Text);
        Assert.True(copied!.Length < gloss.Text.Length, "A partial drag copied the whole gloss.");
        Assert.DoesNotContain(scene.Host.Requests, request => request.Action == SIL.Motif.App.Controls.WordPresentation.WordAction.OpenCard);
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void DraggingAcrossAFormSelectsItsStartInTheFormsOwnDirection(string name) => Run(() =>
    {
        var scene = words.Get(name);
        foreach (var form in new[] { scene.SemanticConstituent(0, Part.Word), scene.SemanticConstituent(0, Part.FieldWorksForm, 2) })
        {
            scene.Host.Requests.Clear();
            var copied = scene.CopySemanticDrag(form, 0.6);
            Assert.False(string.IsNullOrEmpty(copied), $"The drag selected nothing in {form.Part}.");
            Assert.StartsWith(copied!, form.Text);
            Assert.True(copied!.Length < form.Text.Length);
            Assert.DoesNotContain(scene.Host.Requests, request => request.Action == SIL.Motif.App.Controls.WordPresentation.WordAction.OpenCard);
        }
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void TheCardOffersEveryFormGlossAndCategoryToSelectAndCopy(string name) => Run(() =>
    {
        var scene = cards.Get(name);
        var sample = scene.Sample;
        var all = scene.CardConstituents();
        Assert.Equal(sample.StoredForms(0), all.Where(c => c.Part == Part.CardForm).Take(3).Select(c => c.Text));
        Assert.Equal(sample.StoredForms(0).Select(f => sample.Categories[Array.IndexOf(sample.Forms, f)]),
            all.Where(c => c.Part == Part.CardCategory).Take(3).Select(c => c.Text));
        foreach (var constituent in all)
            Assert.Equal(constituent.Text, scene.CopyAllByKeyboard(constituent));
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void ASubstringInsideACardCategoryOrGlossCopiesExactlyThatSubstring(string name) => Run(() =>
    {
        var scene = cards.Get(name);
        var gloss = scene.CardConstituents().First(c => c.Part == Part.CardGloss && c.Text.Length >= 3);
        Assert.Equal(gloss.Text[1..], scene.CopyRangeByKeyboard(gloss, 1, gloss.Text.Length));
    });
}
