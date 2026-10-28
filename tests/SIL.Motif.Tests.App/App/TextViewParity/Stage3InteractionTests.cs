using Avalonia.Input;
using SIL.Motif.App.Controls.WordPresentation;
using Xunit;

namespace SIL.Motif.Tests.App.TextViewParity;

internal sealed class Stage3TheoryAttribute : TheoryAttribute
{
    public Stage3TheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("MOTIF_TEXTVIEW_STAGE3") != "1")
            Skip = "The drawn reader is opt-in.";
    }
}

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class Stage3InteractionTests(WordScenes words, LineScenes lines)
    : IClassFixture<WordScenes>, IClassFixture<LineScenes>
{
    [Stage3Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void EachReaderConstituentCanBeSelectedAndCopied(string name) => AvaloniaHeadlessPlatform.Invoke(() =>
    {
        var scene = words.Get(name);
        var all = scene.SemanticConstituents(0);
        Assert.Equal(15, all.Count);
        foreach (var item in all) Assert.Equal(item.Text, scene.CopySemanticAll(item));
        Assert.Empty(scene.Host.Requests);
    });

    [Stage3Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void DraggingPartsInTheReaderCopiesOnlyTheSelectedCharacters(string name) => AvaloniaHeadlessPlatform.Invoke(() =>
    {
        var scene = words.Get(name);
        var all = scene.SemanticConstituents(0);
        foreach (var item in all.Where(c => c.Text.Length >= 3 && c.Part is Part.Word or Part.FieldWorksForm or Part.PanGlossGloss))
        {
            scene.Host.Requests.Clear();
            var copied = scene.CopySemanticByDrag(item, 0.6);
            Assert.False(string.IsNullOrEmpty(copied), $"{item.Part} {item.Morph} '{item.Text}' selected nothing.");
            Assert.StartsWith(copied, item.Text);
            Assert.True(copied.Length < item.Text.Length, $"{item.Part} {item.Morph} copied the entire text.");
            Assert.Empty(scene.Host.Requests);
        }
    });

    [Stage3Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void ShiftArrowSelectsCharactersInTheWritingSystemDirection(string name) => AvaloniaHeadlessPlatform.Invoke(() =>
    {
        var scene = words.Get(name);
        foreach (var item in scene.SemanticConstituents(0).Where(c => (c.Part is Part.Word or Part.PanGlossGloss) && c.Text.Length >= 3))
        {
            var copied = scene.CopySemanticKeyboardPrefix(item);
            Assert.False(string.IsNullOrEmpty(copied), $"{item.Part} '{item.Text}' selected nothing.");
            Assert.StartsWith(copied, item.Text);
            Assert.True(copied.Length < item.Text.Length);
        }
        Assert.Empty(scene.Host.Requests);
    });

    [Stage3Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void ColumnsAndConstituentDirectionsFollowTheirWritingSystems(string name) => AvaloniaHeadlessPlatform.Invoke(() =>
    {
        var scene = words.Get(name);
        var all = scene.SemanticConstituents(0);
        var forms = all.Where(item => item.Part == Part.FieldWorksForm).ToArray();
        var glosses = all.Where(item => item.Part == Part.FieldWorksGloss).ToArray();
        Assert.Equal(3, forms.Length);
        for (var i = 0; i < forms.Length; i++)
        {
            Assert.Equal(scene.Sample.RightToLeft ? Avalonia.Media.FlowDirection.RightToLeft : Avalonia.Media.FlowDirection.LeftToRight, forms[i].Direction);
            Assert.Equal(Avalonia.Media.FlowDirection.LeftToRight, glosses[i].Direction);
            if (i > 0)
                Assert.True(scene.Sample.RightToLeft
                    ? scene.SemanticBounds(forms[i - 1]).Left > scene.SemanticBounds(forms[i]).Left
                    : scene.SemanticBounds(forms[i - 1]).Left < scene.SemanticBounds(forms[i]).Left);
        }
    });

    [Stage3Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void TabAndShiftTabReachEveryConstituentInTheReader(string name) => AvaloniaHeadlessPlatform.Invoke(() =>
    {
        var scene = lines.Get(name);
        scene.Word(0).FocusWord();
        scene.Settle();
        var focus = scene.Tab();
        var drawn = Assert.IsType<InterlinearWord>(focus);
        var count = scene.SemanticConstituents(0).Count;
        Assert.Equal(0, drawn.SelectedRunIndex);
        for (var i = 1; i < count; i++)
        {
            Assert.Same(drawn, scene.Tab());
            Assert.Equal(i, drawn.SelectedRunIndex);
        }
        Assert.NotSame(drawn, scene.Tab());
        Assert.Same(drawn, scene.Tab(backwards: true));
        Assert.Equal(count - 1, drawn.SelectedRunIndex);
        for (var i = count - 2; i >= 0; i--)
        {
            Assert.Same(drawn, scene.Tab(backwards: true));
            Assert.Equal(i, drawn.SelectedRunIndex);
        }
        Assert.NotSame(drawn, scene.Tab(backwards: true));
        Assert.Same(drawn, scene.Tab());
        Assert.Equal(0, drawn.SelectedRunIndex);
    });

    [Stage3Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void ReaderKeysWorkWhenAConstituentHasFocus(string name) => AvaloniaHeadlessPlatform.Invoke(() =>
    {
        var scene = lines.Get(name);
        var item = scene.SemanticConstituents(2).First(c => c.Part == Part.FieldWorksGloss);
        scene.CopySemanticAll(item);
        scene.Host.Requests.Clear();
        scene.Press(Key.Right);
        var request = Assert.Single(scene.Host.Requests);
        Assert.Equal(scene.Sample.RightToLeft ? WordAction.NavigatePrevious : WordAction.NavigateNext, request.Action);
        Assert.Equal(scene.TokenOf(2).PresentationKey, request.Key);
    });

    [Stage3Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void ClickingTheWordOpensItsCard(string name) => AvaloniaHeadlessPlatform.Invoke(() =>
    {
        var scene = lines.Get(name);
        for (var i = 0; i < 4; i++)
        {
            var item = scene.SemanticConstituents(i).Single(c => c.Part == Part.Word);
            scene.Host.Requests.Clear();
            scene.Click(scene.SemanticBounds(item).Center);
            var request = Assert.Single(scene.Host.Requests.Where(r => r.Action == WordAction.OpenCard));
            Assert.Equal(scene.TokenOf(i).PresentationKey, request.Key);
        }
    });

    [Stage3Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void ConstituentPeersExposeTheirBoundsAndCanReceiveFocusInTheReader(string name) => AvaloniaHeadlessPlatform.Invoke(() =>
    {
        var scene = lines.Get(name);
        var constituents = scene.SemanticConstituents(1);
        var drawn = Assert.IsType<InterlinearWord>(constituents[0].Owner);
        var peer = TextViewScene.PeerOf(drawn);
        var children = peer.GetChildren();
        Assert.Equal(constituents.Count, children.Count);
        foreach (var constituent in constituents)
        {
            var child = children[constituent.RunIndex];
            var expected = scene.SemanticBounds(constituent);
            var actual = child.GetBoundingRectangle();
            Assert.Equal(constituent.Text, child.GetName());
            Assert.Equal(expected.X, actual.X, 0.01);
            Assert.Equal(expected.Y, actual.Y, 0.01);
            Assert.Equal(expected.Width, actual.Width, 0.01);
            Assert.Equal(expected.Height, actual.Height, 0.01);
            Assert.Same(peer, child.GetParent());
            child.SetFocus();
            Assert.True(child.HasKeyboardFocus());
        }
    });
}
