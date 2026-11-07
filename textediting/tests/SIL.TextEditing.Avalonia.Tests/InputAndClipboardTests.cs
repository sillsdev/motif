using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    public class InputAndClipboardTests
    {
        static TextInputMethodClient RequestClient(RichTextField field)
        {
            var args = new TextInputMethodClientRequestedEventArgs { RoutedEvent = InputElement.TextInputMethodClientRequestedEvent, Source = field };
            field.RaiseEvent(args);
            return args.Client!;
        }

        [Fact]
        public void Input_method_client_is_offered_with_preedit_and_surrounding_text() => Headless.Run(() =>
        {
            var field = Fixtures.Field(OneParagraph("abc def"));
            field.Editor.SetCaret(new TextPosition(0, 3));
            var client = RequestClient(field);
            Assert.NotNull(client);
            Assert.True(client.SupportsPreedit);
            Assert.True(client.SupportsSurroundingText);
            Assert.Equal("abc def", client.SurroundingText);
            Assert.Equal(3, client.Selection.End);
        });

        [Fact]
        public void Preedit_is_shown_inline_but_not_in_the_model_and_commit_goes_through_text_input() => Headless.Run(() =>
        {
            var field = Fixtures.Field(OneParagraph("abc def"), 400);
            field.Editor.SetCaret(new TextPosition(0, 3));
            var client = RequestClient(field);
            double before = field.ParagraphLayoutFor(0).Width;
            client.SetPreeditText("あい", 2);
            Fixtures.Layout(field, 400);
            double during = field.ParagraphLayoutFor(0).Width;
            Assert.Equal("abc def", field.Editor.Document.Paragraphs[0].Text);
            Assert.True(during > before + 5, "width " + before + " -> " + during);
            Assert.Equal("あい", field.Preedit);

            client.SetPreeditText(null);
            field.Type("あい");
            Assert.Equal("abcあい def", field.Editor.Document.Paragraphs[0].Text);
            Assert.Null(field.Preedit);
        });

        [Fact]
        public void Copy_then_paste_through_the_clipboard_keeps_runs_and_opaque_properties() => Headless.Run(async () =>
        {
            var p = new Paragraph();
            p.Runs.Add(new Run { Ws = "en", Text = "plain " });
            var r = new Run { Ws = "fr", Text = "opaque run" };
            r.Props.SetStr(4243, "a;b=c<&>é").SetInt(4242, 42);
            p.Runs.Add(r);
            p.Runs.Add(new Run { Ws = "en", Text = " tail" });
            var (window, field) = Fixtures.InWindow(Doc(p));
            field.ClipboardForms.Add(new InterchangeClipboardForm()).PasteOrder.Insert(0, new InterchangeClipboardForm().Id);
            try
            {
                field.Editor.Anchor = new TextPosition(0, 6);
                field.Editor.SetCaret(new TextPosition(0, 16), true);
                await field.CopyAsync(false);
                field.Editor.SetCaret(new TextPosition(0, 21));
                await field.PasteAsync();
                var pasted = field.Editor.Document.Paragraphs[0].Runs.Where(x => x.Props.StrProps.ContainsKey(4243)).ToList();
                Assert.Equal(20, pasted.Sum(x => x.Text.Length));
                Assert.All(pasted, x => Assert.Equal("fr", x.Ws));
                Assert.All(pasted, x => Assert.Equal(42, x.Props.IntProps[4242].Value));
            }
            finally { window.Close(); }
        });

        [Fact]
        public void Cut_removes_the_selection_and_paste_without_our_format_inserts_plain_text() => Headless.Run(async () =>
        {
            var (window, field) = Fixtures.InWindow(OneParagraph("hello world"));
            try
            {
                field.Editor.Anchor = new TextPosition(0, 0);
                field.Editor.SetCaret(new TextPosition(0, 6), true);
                await field.CopyAsync(true);
                Assert.Equal("world", field.Editor.Document.Paragraphs[0].Text);
                field.Editor.SetCaret(new TextPosition(0, 5));
                await field.PasteAsync();
                Assert.Equal("worldhello ", field.Editor.Document.Paragraphs[0].Text);
            }
            finally { window.Close(); }
        });
    }
}
