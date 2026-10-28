using Avalonia.Automation.Peers;
using Avalonia.Controls;
using SIL.Motif.App.Controls.WordPresentation;
using Xunit;

namespace SIL.Motif.Tests.App.TextViewParity;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SeamRegressionTests
{
    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void CopyAllClearsStaleSelectionAndRestoresNativeFocusability(string name) => AvaloniaHeadlessPlatform.Invoke(() =>
    {
        using var scene = TextViewScene.SingleWord(TextViewSample.Named(name));
        var part = scene.SemanticConstituent(0, Part.FieldWorksForm, 1);
        Assert.Equal(part.Text[..1], scene.CopySemanticRange(part, 0, 1));
        if (part.Owner is SelectableTextBlock native) native.Focusable = false;
        Assert.Equal(part.Text, scene.CopySemanticAll(part));
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void EmptyFormsExposePeersAndCopyNoStaleText(string name) => AvaloniaHeadlessPlatform.Invoke(() =>
    {
        var sample = TextViewSample.Named(name);
        var forms = (string[])sample.Forms.Clone();
        forms[0] = string.Empty;
        using var scene = TextViewScene.SingleWord(sample with { Forms = forms });
        var word = scene.SemanticConstituent(0, Part.Word);
        Assert.Equal(word.Text, scene.CopySemanticAll(word));
        var empty = scene.SemanticConstituent(0, Part.FieldWorksForm);
        Assert.Empty(empty.Text);
        var peer = scene.SemanticPeer(empty);
        Assert.True(string.IsNullOrEmpty(peer.GetName()));
        Assert.Equal(AutomationControlType.Text, peer.GetAutomationControlType());
        var bounds = peer.GetBoundingRectangle();
        Assert.True(bounds.Width >= 0 && bounds.Height > 0);
        Assert.True(string.IsNullOrEmpty(scene.CopySemanticAll(empty)));
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void StartingOutsideWordsDoesNotRefocusThePreviousNativeWord(string name) => AvaloniaHeadlessPlatform.Invoke(() =>
    {
        using var scene = TextViewScene.Line(TextViewSample.Named(name), 4, 1100);
        scene.TabThroughEveryWord();
        var last = scene.WalkWords[^1];
        var focusedLast = 0;
        scene.Word(last).GotFocus += (_, _) => focusedLast++;
        scene.Root.Focusable = true;
        var stops = scene.TabFrom(scene.Root, backwards: false, inside: _ => false);
        Assert.Same(scene.Root, Assert.Single(stops));
        if (scene.SemanticConstituent(0, Part.Word).Owner is not InterlinearWord)
            Assert.Equal(0, focusedLast);
    });
}
