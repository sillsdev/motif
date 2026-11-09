using Avalonia.Headless;
using SIL.Motif.App.Preview;
using SIL.Motif.Contract.Responses;
using SIL.TextEditing;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class GdrPreviewWindowTests
{
    private readonly AvaloniaHeadlessFixture _avalonia;

    public GdrPreviewWindowTests(AvaloniaHeadlessFixture avalonia) => _avalonia = avalonia;

    private static GdrTextProperties Props(int? italic = null, string? style = null) => new(
        italic is { } value ? new Dictionary<int, GdrIntProperty> { [TextPropertyNumbers.Italic] = new(0, value) } : new Dictionary<int, GdrIntProperty>(),
        style is null ? new Dictionary<int, string>() : new Dictionary<int, string> { [TextPropertyNumbers.NamedStyle] = style });

    private static readonly GdrSummary First = new("id-1", "GDR-1", "Word boundaries", "Where one word ends.");
    private static readonly GdrSummary Second = new("id-2", "GDR-2", "Spelling variants", "Which spelling wins.");

    private static GdrShowResponse Canned(GdrSummary record) => new(record,
    [
        new GdrParagraph("Normal", false, "en", Props(style: "Normal"),
        [
            new GdrRun("Words end at ", "en", null, Props()),
            new GdrRun("spaces", "en", "Emphasis", Props(style: "Emphasis")),
        ]),
        new GdrParagraph("Normal", true, "ar", Props(style: "Normal"), [new GdrRun("كتاب", "ar", null, Props())]),
    ])
    {
        DefaultVernacular = "ar",
        DefaultAnalysis = "en",
        Styles =
        [
            new GdrStyle("Emphasis", GdrStyleKind.Character,
                new Dictionary<string, GdrTextProperties> { ["en"] = Props(italic: 1), ["ar"] = Props(italic: 1) }),
            new GdrStyle("Normal", GdrStyleKind.Paragraph,
                new Dictionary<string, GdrTextProperties> { ["en"] = Props(), ["ar"] = Props() }),
        ],
        WritingSystems = [new GdrWritingSystem("ar", "rtl", "Segoe UI"), new GdrWritingSystem("en", "ltr", "Segoe UI")],
    };

    [Fact]
    public void SelectingARecordShowsItsDiscussionReadOnlyWithTheProjectsStylesAndWritingSystems()
    {
        _avalonia.Invoke(() =>
        {
            var window = new GdrPreviewWindow([First, Second], id => Canned(id == First.Id ? First : Second));
            using var closer = window.CloseOnExit();
            window.Show();
            Assert.Null(window.Field);

            window.Show(First.Id);

            var field = window.Field!;
            Assert.True(field.IsReadOnly);
            Assert.StartsWith("GDR-1  Word boundaries", window.HeadingText, StringComparison.Ordinal);
            var paragraphs = field.Editor.Document.Paragraphs;
            Assert.Equal(2, paragraphs.Count);
            Assert.False(paragraphs[0].Rtl);
            Assert.True(paragraphs[1].Rtl);
            Assert.Equal("ar", paragraphs[1].Runs[0].Ws);
            Assert.Equal("Emphasis", paragraphs[0].Runs[1].Props.StrProps[TextPropertyNumbers.NamedStyle]);
            Assert.True(field.WritingSystems.Get("ar").Rtl);
            Assert.False(field.WritingSystems.Get("en").Rtl);
            var resolved = field.StyleResolver!.Resolve("Emphasis", "en");
            Assert.NotNull(resolved);
            Assert.Equal(1, resolved.IntProps[TextPropertyNumbers.Italic].Value);
            Assert.Null(field.StyleResolver.Resolve("Absent", "en"));
        });
    }

    [Fact]
    public void TypingDoesNotChangeAReadOnlyDiscussion()
    {
        _avalonia.Invoke(() =>
        {
            var window = new GdrPreviewWindow([First], _ => Canned(First));
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(First.Id);
            window.Field!.Focus();
            var before = window.Field.Editor.Document.Paragraphs[0].Text;

            window.KeyTextInput("xyz");

            Assert.Equal(before, window.Field.Editor.Document.Paragraphs[0].Text);
        });
    }

    [Fact]
    public void ChoosingAnotherRecordReplacesTheDiscussion()
    {
        _avalonia.Invoke(() =>
        {
            var window = new GdrPreviewWindow([First, Second], id => Canned(id == First.Id ? First : Second));
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(First.Id);
            window.Show(Second.Id);

            Assert.StartsWith("GDR-2  Spelling variants", window.HeadingText, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("1", true, true)]
    [InlineData("1", false, false)]
    [InlineData(null, true, false)]
    [InlineData("0", true, false)]
    public void PreviewIsRequestedOnlyByTheDeveloperFlagAndItsArgument(string? flag, bool argument, bool expected)
    {
        var args = argument ? new[] { GdrPreviewWindow.StartupArgument } : [];
        Assert.Equal(expected, GdrPreviewWindow.IsRequested(flag, args));
    }

    [Fact]
    public void PreviewWindowIsNotOpenedWithoutTheGate()
    {
        _avalonia.Invoke(() => Assert.Null(GdrPreviewWindow.ShowIfRequested([])));
    }
}
