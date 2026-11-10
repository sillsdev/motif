using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using SIL.Motif.App.Preview;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Gdr;
using SIL.Motif.Runner.Operations;
using SIL.TextEditing;
using Xunit;

namespace SIL.Motif.Tests.App;

// Each formatting action the toolbar offers reaches the Draft as text/setParagraphs.
public sealed partial class GdrPreviewWindowTests
{
    private static readonly GdrSummary Linked = new(CanonicalId.FromGuid(new Guid("6a3c0f4e-2b1d-4c8e-9f70-1d2e3f405162")).Value,
        "GDR-9", "Clitics", "Where clitics attach.");

    private static GdrPreviewWindow Staging(FakeStore store, Func<GdrSummary, GdrShowResponse>? show = null)
    {
        var records = new[] { First, Second, Linked };
        var window = new GdrPreviewWindow(records, id => (show ?? Canned)(records.Single(record => record.Id == id)), store);
        window.Show();
        window.Show(First.Id);
        window.Field!.Focus();
        return window;
    }

    private static void Select(GdrPreviewWindow window, int paragraph, int start, int end)
    {
        var editor = window.Field!.Editor;
        editor.SetCaret(new TextPosition(paragraph, start));
        editor.SetCaret(new TextPosition(paragraph, end), extend: true);
    }

    private static ComboBox List(GdrPreviewWindow window, string label) =>
        window.GetLogicalDescendants().OfType<ComboBox>().Single(box => Equals(box.PlaceholderText, label));

    private static TextParagraphChange StagedFirst(FakeStore store) =>
        Assert.Single(store.Staged[^1].Paragraphs.Changed, change => change.Key == "para-1");

    [Fact]
    public void TheToolbarOffersTheProjectsStylesAndWritingSystemsAndTheOtherRecordsToLinkTo()
    {
        _avalonia.Invoke(() =>
        {
            var window = Staging(new FakeStore());
            using var closer = window.CloseOnExit();

            Assert.Equal(["Normal", "Quote"], window.ParagraphStyleChoices);
            Assert.Equal([GdrPreviewWindow.NoCharacterStyle, "Emphasis", "Strong"], window.CharacterStyleChoices);
            Assert.Equal(["ar", "en"], window.WritingSystemChoices);
            Assert.Equal(["GDR-2  Spelling variants", "GDR-9  Clitics"], window.LinkChoices);
            Assert.All(window.GetLogicalDescendants().OfType<ComboBox>(), box => Assert.False(box.Focusable));
        });
    }

    [Fact]
    public void BoldItalicAndUnderlineAreStagedAsFieldWorksTogglesAndEachIsOneUndoStep()
    {
        _avalonia.Invoke(() =>
        {
            var store = new FakeStore();
            var window = Staging(store);
            using var closer = window.CloseOnExit();
            Select(window, 0, 0, "Words".Length);

            Assert.True(window.ToggleBold());
            Assert.True(window.ToggleItalic());
            Assert.True(window.ToggleUnderline());
            Assert.Equal(3, window.Session!.UndoSteps);
            Leave(window);

            var words = StagedFirst(store).Runs[0];
            Assert.Equal("Words", words.Text);
            Assert.Equal((TextPropertyNumbers.VariationEnum, 1), words.Props!.Ints[TextPropertyNumbers.Bold]);
            Assert.Equal((TextPropertyNumbers.VariationEnum, 1), words.Props.Ints[TextPropertyNumbers.Italic]);
            Assert.Equal((TextPropertyNumbers.VariationEnum, TextPropertyNumbers.UnderlineSingle), words.Props.Ints[TextPropertyNumbers.Underline]);
            Assert.False(StagedFirst(store).Runs[1].Props?.Ints.ContainsKey(TextPropertyNumbers.Bold) ?? false);
        });
    }

    [Fact]
    public void BoldOnTextThatIsAllBoldTakesItOffAndUndoingTheFormattingLeavesNoEdit()
    {
        _avalonia.Invoke(() =>
        {
            var store = new FakeStore();
            var window = Staging(store);
            using var closer = window.CloseOnExit();
            Select(window, 0, 0, "Words".Length);
            window.ToggleBold();
            window.ToggleBold();
            Assert.False(window.Field!.Editor.Document.Paragraphs[0].Runs[0].Props.IntProps.ContainsKey(TextPropertyNumbers.Bold));

            window.ToggleItalic();
            Press(window, Key.Z, RawInputModifiers.Control);
            Press(window, Key.Z, RawInputModifiers.Control);
            Press(window, Key.Z, RawInputModifiers.Control);
            Leave(window);

            Assert.Empty(window.PendingEdits);
            // A session may end on the way; editing back to the start takes the edit out again.
            Assert.Equal(store.Staged.Count, store.Discards.Count);
        });
    }

    [Fact]
    public void ACharacterStyleChosenFromTheToolbarIsStagedAndTheNoneChoiceTakesOneAway()
    {
        _avalonia.Invoke(() =>
        {
            var store = new FakeStore();
            var window = Staging(store);
            using var closer = window.CloseOnExit();
            var styles = List(window, "Character style");

            Select(window, 0, 0, "Words".Length);
            styles.SelectedItem = "Strong";
            Select(window, 0, "Words end at ".Length, "Words end at spaces".Length);
            styles.SelectedItem = GdrPreviewWindow.NoCharacterStyle;
            Leave(window);

            Assert.Equal(-1, styles.SelectedIndex);
            Assert.Equal([("Words", "Strong"), (" end at spaces", (string?)null)],
                StagedFirst(store).Runs.Select(run => (run.Text, run.CharacterStyle)));
        });
    }

    [Fact]
    public void AParagraphStyleChosenFromTheToolbarIsStagedForTheCaretsParagraph()
    {
        _avalonia.Invoke(() =>
        {
            var store = new FakeStore();
            var window = Staging(store);
            using var closer = window.CloseOnExit();
            window.Field!.Editor.SetCaret(new TextPosition(0, 3));

            List(window, "Paragraph style").SelectedItem = "Quote";
            Leave(window);

            var changed = StagedFirst(store);
            Assert.Equal("Quote", changed.Style);
            Assert.Equal("Words end at spaces", string.Concat(changed.Runs.Select(run => run.Text)));
            Assert.DoesNotContain(store.Staged[^1].Paragraphs.Changed, change => change.Key == "para-2");
        });
    }

    [Fact]
    public void AWritingSystemChangeMovesTheSelectionAndTypingContinuesInTheChosenRightToLeftOne()
    {
        _avalonia.Invoke(() =>
        {
            var store = new FakeStore();
            var window = Staging(store);
            using var closer = window.CloseOnExit();
            var systems = List(window, "Writing system");

            Select(window, 0, 0, "Words".Length);
            systems.SelectedItem = "ar";
            window.Field!.Editor.SetCaret(new TextPosition(0, "Words end at spaces".Length));
            systems.SelectedItem = "ar";
            window.KeyTextInput("ب");
            Leave(window);

            var runs = StagedFirst(store).Runs;
            Assert.Equal(("Words", "ar"), (runs[0].Text, runs[0].Ws));
            Assert.Equal(("ب", "ar"), (runs[^1].Text, runs[^1].Ws));
            Assert.Equal("en", runs[1].Ws);
            Assert.True(window.Field.WritingSystems.Get("ar").Rtl);
        });
    }

    [Fact]
    public void ALinkToAnotherRecordIsStagedWithItsObjectDataAndWithoutTheViewsLinkColour()
    {
        _avalonia.Invoke(() =>
        {
            var store = new FakeStore();
            var window = Staging(store);
            using var closer = window.CloseOnExit();
            window.Field!.Editor.SetCaret(new TextPosition(0, "Words end at ".Length));

            List(window, "Link to a record").SelectedItem = "GDR-9  Clitics";
            Leave(window);

            var link = StagedFirst(store).Runs.Single(run => run.ObjData is not null);
            Assert.Equal(("GDR-9", "en", (string?)null), (link.Text, link.Ws, link.CharacterStyle));
            Assert.Equal(GdrReader.LinkData(Linked.Id), link.ObjData);
            Assert.Empty(link.Props?.Ints ?? new Dictionary<int, (int, int)>());
            Assert.Equal("Words end at GDR-9spaces", string.Concat(StagedFirst(store).Runs.Select(run => run.Text)));
        });
    }

    private const string LinkData = "\u0004https://example.invalid/x", PictureData = "\u000201234567";

    private static GdrShowResponse WithLinkAndPicture(GdrSummary record)
    {
        var canned = Canned(record);
        var stored = new GdrTextProperties(new Dictionary<int, GdrIntProperty>(), new Dictionary<int, string> { [TextPropertyNumbers.NamedStyle] = "Emphasis" });
        return canned with
        {
            Discussion =
            [
                canned.Discussion[0] with
                {
                    Runs =
                    [
                        new GdrRun("See ", "en", null, Props()),
                        new GdrRun("here", "en", "Emphasis", stored, LinkData, new GdrLinkTarget(GdrLinkState.External, null, "https://example.invalid/x", "")),
                        new GdrRun("￼", "en", null, Props(), PictureData),
                    ],
                },
                canned.Discussion[1],
            ],
        };
    }

    [Fact]
    public void TypingInAParagraphKeepsItsLinkWithTheLinksOwnPropertiesAndItsPicture()
    {
        _avalonia.Invoke(() =>
        {
            var store = new FakeStore();
            var window = Staging(store, WithLinkAndPicture);
            using var closer = window.CloseOnExit();
            window.Field!.Editor.SetCaret(new TextPosition(0, 0));

            window.KeyTextInput("Now ");
            Select(window, 0, 0, "Now See here￼".Length);
            window.ToggleBold();
            Leave(window);

            var runs = StagedFirst(store).Runs;
            var link = runs.Single(run => run.ObjData == LinkData);
            Assert.Equal(("here", "Emphasis"), (link.Text, link.CharacterStyle));
            Assert.False(link.Props!.Ints.ContainsKey(TextPropertyNumbers.Underline));
            Assert.False(link.Props.Ints.ContainsKey(TextPropertyNumbers.ForeColor));
            Assert.False(link.Props.Ints.ContainsKey(TextPropertyNumbers.Bold));
            Assert.Equal("￼", runs.Single(run => run.ObjData == PictureData).Text);
            Assert.Equal("Now See ", runs[0].Text);
            Assert.True(runs[0].Props!.Ints.ContainsKey(TextPropertyNumbers.Bold));
        });
    }

    [Fact]
    public void FormattingTheWholeParagraphLeavesTheBrokenLinkMarkerOutOfTheProject()
    {
        _avalonia.Invoke(() =>
        {
            var store = new FakeStore();
            var window = Staging(store, record =>
            {
                var canned = Canned(record);
                return canned with
                {
                    Discussion =
                    [
                        canned.Discussion[0] with
                        {
                            Runs = [new GdrRun("gone", "en", null, Props(), LinkData, new GdrLinkTarget(GdrLinkState.Broken, null, null, "")),
                                new GdrRun(" after", "en", null, Props())],
                        },
                        canned.Discussion[1],
                    ],
                };
            });
            using var closer = window.CloseOnExit();
            Select(window, 0, 0, window.Field!.Editor.Document.Paragraphs[0].Length);

            Assert.True(window.ApplyCharacterStyle("Strong"));
            Leave(window);

            Assert.Equal("gone after", string.Concat(StagedFirst(store).Runs.Select(run => run.Text)));
            Assert.Equal((string?)null, StagedFirst(store).Runs[0].CharacterStyle);
            Assert.Equal("Strong", StagedFirst(store).Runs[^1].CharacterStyle);
        });
    }

    [Fact]
    public void NoFormattingReachesAFieldThatIsReadOnlyUntilItsDraftEditIsRead()
    {
        _avalonia.Invoke(() =>
        {
            var store = new FakeStore { PendingGate = new TaskCompletionSource() };
            var window = Staging(store);
            using var closer = window.CloseOnExit();
            var before = window.Field!.Editor.Document.Paragraphs.Select(paragraph => paragraph.Clone()).ToArray();
            Select(window, 0, 0, "Words".Length);

            Assert.False(window.ToggleBold());
            Assert.False(window.ApplyCharacterStyle("Strong"));
            Assert.False(window.ApplyParagraphStyle("Quote"));
            Assert.False(window.SetWritingSystem("ar"));
            Assert.False(window.InsertLink(Linked.Id));

            Assert.Equal(before.Select(paragraph => (paragraph.Style, paragraph.Text)),
                window.Field.Editor.Document.Paragraphs.Select(paragraph => (paragraph.Style, paragraph.Text)));
            Assert.True(window.Field.Editor.Document.Paragraphs[0].Runs[0].Props.IntProps.Count == 0);
            store.PendingGate.SetResult();
            Completed(window.Loading);
        });
    }
}
