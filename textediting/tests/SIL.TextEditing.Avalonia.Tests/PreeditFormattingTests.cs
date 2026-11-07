using System.Linq;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>An IME composition is drawn in the character formatting the committed text will get (the run before the caret, as typing
    /// does, or where a selection it replaces began), with the composition underline.</summary>
    public class PreeditFormattingTests
    {
        static TextRunProperties PreeditProperties(RichTextField field, string preedit)
        {
            field.SetPreedit(preedit, null);
            Fixtures.Layout(field, 400);
            var line = field.ParagraphLayoutFor(0).Lines[0].Line;
            return line.TextRuns.First(r => r.Text.ToString().Contains(preedit)).Properties!;
        }

        static Document BoldRedRun(string ws = "en")
        {
            var doc = Doc(Para("en", ws, "abc def"));
            doc.Paragraphs[0].Runs[0].Props
                .SetInt(TextPropertyNumbers.Bold, TextPropertyNumbers.ToggleOn)
                .SetInt(TextPropertyNumbers.ForeColor, 0x0000FF);   // BGR: red
            return doc;
        }

        [Fact]
        public void A_composition_inside_bold_coloured_text_is_bold_and_coloured() => Headless.Run(() =>
        {
            var field = Fixtures.Field(BoldRedRun(), 400);
            field.Editor.SetCaret(new TextPosition(0, 3));
            var properties = PreeditProperties(field, "xyz");
            Assert.Equal(FontWeight.Bold, properties.Typeface.Weight);
            Assert.Equal(Colors.Red, ((ISolidColorBrush)properties.ForegroundBrush!).Color);
        });

        [Fact]
        public void A_composition_at_the_end_of_a_run_takes_that_runs_formatting_and_gets_the_composition_underline() => Headless.Run(() =>
        {
            var field = Fixtures.Field(BoldRedRun(), 400);
            field.Editor.SetCaret(new TextPosition(0, 7));
            var properties = PreeditProperties(field, "xyz");
            Assert.Equal(FontWeight.Bold, properties.Typeface.Weight);
            Assert.Equal(3, field.ParagraphLayoutFor(0).DecorationAt(field.ParagraphLayoutFor(0).EffectiveText.IndexOf("xyz", System.StringComparison.Ordinal))?.LineKind);   // the composition underline: a single line the field draws itself
            Assert.Null(properties.TextDecorations);
        });

        [Fact]
        public void A_composition_in_a_superscript_run_is_superscript() => Headless.Run(() =>
        {
            var doc = OneParagraph("abc def");
            doc.Paragraphs[0].Runs[0].Props.SetInt(TextPropertyNumbers.Superscript, 1);
            var field = Fixtures.Field(doc, 400);
            field.Editor.SetCaret(new TextPosition(0, 3));
            var properties = PreeditProperties(field, "xyz");
            Assert.True(properties.FontRenderingEmSize < 16 * 0.9, "smaller than the text beside it: " + properties.FontRenderingEmSize);
            double raised = field.ParagraphLayoutFor(0).Lines[0].Pieces(3, 6).Single().Value;   // the layout's own offset for the composition (Avalonia's runs do not carry it)
            Assert.True(raised > 0, "raised: " + raised);
        });

        [Fact]
        public void A_composition_in_another_writing_system_takes_nothing_from_the_neighbouring_run() => Headless.Run(() =>
        {
            var field = Fixtures.Field(BoldRedRun(), 400);
            field.Editor.SetCaret(new TextPosition(0, 3));
            field.Editor.SetActiveWs("fr");
            var properties = PreeditProperties(field, "xyz");
            Assert.NotEqual(FontWeight.Bold, properties.Typeface.Weight);
        });

        static Document BoldThenPlain()
        {
            var doc = Doc(Para("en", "en", "abc", "en", "def"));
            doc.Paragraphs[0].Runs[0].Props.SetInt(TextPropertyNumbers.Bold, TextPropertyNumbers.ToggleOn);
            return doc;
        }

        [Theory]
        [InlineData(1, 5, true)]    // the selection began in the bold run, as the caret did not
        [InlineData(5, 1, true)]
        [InlineData(0, 4, false)]   // nothing before the start, so the run after what is left: plain
        public void A_composition_over_a_selection_is_drawn_in_the_formatting_typing_over_it_would_get(int anchor, int caret, bool bold) => Headless.Run(() =>
        {
            var field = Fixtures.Field(BoldThenPlain(), 400);
            field.Editor.SetCaret(new TextPosition(0, anchor));
            field.Editor.SetCaret(new TextPosition(0, caret), extend: true);
            var properties = PreeditProperties(field, "xyz");
            Assert.Equal(bold ? FontWeight.Bold : FontWeight.Normal, properties.Typeface.Weight);
            // Committing the composition types it over the selection: it gets the same formatting.
            field.SetPreedit(null, null);
            field.Editor.TypeText("xyz");
            var typed = field.Editor.Document.Paragraphs[0].Runs.First(r => r.Text.Contains("xyz"));
            Assert.Equal(bold, typed.Props.TryGetInt(TextPropertyNumbers.Bold, out int value) && value == TextPropertyNumbers.ToggleOn);
        });
    }
}
