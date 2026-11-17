using System.Linq;
using Xunit;

namespace SIL.TextEditing.Tests
{
    public class SelectionFormattingTests
    {
        sealed class Styles : IStyleResolver
        {
            readonly int _property;
            public Styles(int property) => _property = property;
            public PropertyBag? Resolve(string styleName, string ws) => new PropertyBag().SetInt(_property,
                _property == TextPropertyNumbers.Underline ? TextPropertyNumbers.UnderlineDouble : TextPropertyNumbers.ToggleOn);
        }

        [Theory]
        [InlineData(TextPropertyNumbers.Bold)]
        [InlineData(TextPropertyNumbers.Italic)]
        [InlineData(TextPropertyNumbers.Underline)]
        public void Reversed_selection_across_paragraphs_changes_only_selected_text_and_keeps_other_properties(int property)
        {
            var document = new Document();
            foreach (string key in new[] { "first", "second" })
            {
                var paragraph = new Paragraph { Key = key, Style = "Styled" };
                paragraph.Runs.Add(new Run { Text = "abcd", Ws = key });
                paragraph.Runs[0].Props.SetInt(9001, 42);
                document.Paragraphs.Add(paragraph);
            }
            var editor = new DocumentEditor(document);
            editor.SetCaret(new TextPosition(1, 2));
            editor.SetCaret(new TextPosition(0, 2), true);
            var styles = new Styles(property);
            Assert.True(SelectionFormatting.IsOn(editor, styles, property));
            var delta = SelectionFormatting.Toggle(editor, styles, property);
            Assert.NotNull(delta);
            Assert.All(document.Paragraphs, p => Assert.False(p.Runs[0].Props.IntProps.ContainsKey(property)));
            editor.Apply(delta!);
            Assert.Equal(new[] { "first", "second" }, document.Paragraphs.Select(p => p.Key));
            Assert.Equal(new[] { "ab", "cd", "ab", "cd" }, document.Paragraphs.SelectMany(p => p.Runs).Select(r => r.Text));
            Assert.All(document.Paragraphs.SelectMany(p => p.Runs), r => Assert.Equal(42, r.Props.IntProps[9001].Value));
            Assert.False(document.Paragraphs[0].Runs[0].Props.IntProps.ContainsKey(property));
            Assert.Equal(0, document.Paragraphs[0].Runs[1].Props.IntProps[property].Value);
            Assert.Equal(0, document.Paragraphs[1].Runs[0].Props.IntProps[property].Value);
            Assert.False(document.Paragraphs[1].Runs[1].Props.IntProps.ContainsKey(property));
        }

        [Theory]
        [InlineData(TextPropertyNumbers.Bold)]
        [InlineData(TextPropertyNumbers.Italic)]
        public void Direct_invert_resolves_against_the_style_and_is_removed_when_returning_to_it(int property)
        {
            var document = new Document();
            var paragraph = new Paragraph { Style = "Styled" };
            var run = new Run { Text = "word" };
            run.Props.SetInt(property, TextPropertyNumbers.ToggleInvert, TextPropertyNumbers.VariationEnum);
            paragraph.Runs.Add(run);
            document.Paragraphs.Add(paragraph);
            var editor = new DocumentEditor(document);
            editor.SetCaret(new TextPosition(0, 4), true);
            var styles = new Styles(property);
            Assert.False(SelectionFormatting.IsOn(editor, styles, property));
            editor.Apply(SelectionFormatting.Toggle(editor, styles, property)!);
            Assert.False(Assert.Single(document.Paragraphs[0].Runs).Props.IntProps.ContainsKey(property));
            editor.SetCaret(new TextPosition(0, 0));
            editor.SetCaret(new TextPosition(0, 4), true);
            Assert.True(SelectionFormatting.IsOn(editor, styles, property));
        }

        [Fact]
        public void Hosts_can_exclude_markers_and_keep_text_links_formattable()
        {
            var document = new Document();
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run { Text = "marker", Ws = "marker" });
            paragraph.Runs.Add(new Run { Text = "link", ObjData = "link target" });
            document.Paragraphs.Add(paragraph);
            var editor = new DocumentEditor(document);
            editor.SetCaret(new TextPosition(0, 10), true);
            editor.Apply(SelectionFormatting.Toggle(editor, null, TextPropertyNumbers.Bold, r => r.Ws != "marker")!);
            Assert.False(document.Paragraphs[0].Runs[0].Props.IntProps.ContainsKey(TextPropertyNumbers.Bold));
            Assert.Equal(1, document.Paragraphs[0].Runs[1].Props.IntProps[TextPropertyNumbers.Bold].Value);
            Assert.Equal("link target", document.Paragraphs[0].Runs[1].ObjData);
        }
        [Fact]
        public void Underline_leaves_a_strikethrough_run_alone_and_underlines_the_rest()
        {
            var document = new Document();
            var paragraph = new Paragraph();
            var struck = new Run { Text = "ab" };
            struck.Props.SetInt(TextPropertyNumbers.Underline, TextPropertyNumbers.UnderlineStrikethrough, TextPropertyNumbers.VariationEnum);
            paragraph.Runs.Add(struck);
            paragraph.Runs.Add(new Run { Text = "cd" });
            document.Paragraphs.Add(paragraph);
            var editor = new DocumentEditor(document);
            editor.SetCaret(new TextPosition(0, 4), true);
            editor.Apply(SelectionFormatting.Toggle(editor, null, TextPropertyNumbers.Underline)!);
            Assert.Equal(TextPropertyNumbers.UnderlineStrikethrough, document.Paragraphs[0].Runs[0].Props.IntProps[TextPropertyNumbers.Underline].Value);
            Assert.Equal(TextPropertyNumbers.UnderlineSingle, document.Paragraphs[0].Runs[1].Props.IntProps[TextPropertyNumbers.Underline].Value);
        }

        [Fact]
        public void Underline_on_a_strikethrough_only_selection_produces_no_edit()
        {
            var document = new Document();
            var paragraph = new Paragraph();
            var struck = new Run { Text = "ab" };
            struck.Props.SetInt(TextPropertyNumbers.Underline, TextPropertyNumbers.UnderlineStrikethrough, TextPropertyNumbers.VariationEnum);
            paragraph.Runs.Add(struck);
            document.Paragraphs.Add(paragraph);
            var editor = new DocumentEditor(document);
            editor.SetCaret(new TextPosition(0, 2), true);
            Assert.False(SelectionFormatting.IsOn(editor, null, TextPropertyNumbers.Underline));
            Assert.Null(SelectionFormatting.Toggle(editor, null, TextPropertyNumbers.Underline));
        }

        [Theory]
        [InlineData(700, false)]
        [InlineData(600, false)]
        [InlineData(400, true)]
        public void A_direct_numeric_bold_weight_resolves_through_the_renderer_threshold(int weight, bool turnsOn)
        {
            var document = new Document();
            var paragraph = new Paragraph();
            var run = new Run { Text = "word" };
            run.Props.SetInt(TextPropertyNumbers.Bold, weight);
            paragraph.Runs.Add(run);
            document.Paragraphs.Add(paragraph);
            var editor = new DocumentEditor(document);
            editor.SetCaret(new TextPosition(0, 4), true);
            Assert.Equal(!turnsOn, SelectionFormatting.IsOn(editor, null, TextPropertyNumbers.Bold));
            editor.Apply(SelectionFormatting.Toggle(editor, null, TextPropertyNumbers.Bold)!);
            var props = Assert.Single(document.Paragraphs[0].Runs).Props.IntProps;
            // Turning bold off removes the direct weight (plain is what the run inherits); turning it on writes the enum.
            if (turnsOn) Assert.Equal(TextPropertyNumbers.ToggleOn, props[TextPropertyNumbers.Bold].Value);
            else Assert.False(props.ContainsKey(TextPropertyNumbers.Bold));
        }

        [Fact]
        public void Empty_selection_and_object_only_selection_produce_no_edit()
        {
            var document = new Document();
            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.Object("en", "picture"));
            document.Paragraphs.Add(paragraph);
            var editor = new DocumentEditor(document);
            Assert.Null(SelectionFormatting.Toggle(editor, null, TextPropertyNumbers.Bold));
            editor.SetCaret(new TextPosition(0, 1), true);
            Assert.False(SelectionFormatting.IsOn(editor, null, TextPropertyNumbers.Bold));
            Assert.Null(SelectionFormatting.Toggle(editor, null, TextPropertyNumbers.Bold));
            Assert.False(Assert.Single(paragraph.Runs).Props.IntProps.ContainsKey(TextPropertyNumbers.Bold));
        }

    }
}
