using Avalonia.Headless;
using SIL.Motif.App.Preview;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TextEditorPreviewWindowTests
{
    private readonly AvaloniaHeadlessFixture _avalonia;

    public TextEditorPreviewWindowTests(AvaloniaHeadlessFixture avalonia) => _avalonia = avalonia;

    [Fact]
    public void PreviewWindowOpensAndItsFieldAcceptsTyping()
    {
        _avalonia.Invoke(() =>
        {
            var window = new TextEditorPreviewWindow();
            using var closer = window.CloseOnExit();
            window.Show();
            window.Field.Focus();

            var before = window.Field.Editor.Document.Paragraphs[0].Text;
            window.KeyTextInput("xyz");

            Assert.Equal("xyz" + before, window.Field.Editor.Document.Paragraphs[0].Text);
            Assert.StartsWith("Writing system at the caret: en", window.CaretLabelText, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("1", true, true)]
    [InlineData("1", false, false)]
    [InlineData(null, true, false)]
    [InlineData("0", true, false)]
    public void PreviewIsRequestedOnlyByTheDeveloperFlagAndItsArgument(string? flag, bool argument, bool expected)
    {
        var args = argument ? new[] { TextEditorPreviewWindow.StartupArgument } : [];
        Assert.Equal(expected, TextEditorPreviewWindow.IsRequested(flag, args));
    }

    [Fact]
    public void PreviewWindowIsNotOpenedWithoutTheGate()
    {
        _avalonia.Invoke(() =>
        {
            Assert.Null(TextEditorPreviewWindow.ShowIfRequested([]));
        });
    }
}
