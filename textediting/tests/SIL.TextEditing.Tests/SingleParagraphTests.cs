using System.Collections.Generic;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Tests
{
    /// <summary>ADR 0012: in single-paragraph mode the field never makes a second paragraph.</summary>
    public class SingleParagraphTests
    {
        static DocumentEditor Single(string text)
        {
            var e = new DocumentEditor(OneParagraph(text)) { SingleParagraph = true };
            e.SetCaret(new TextPosition(0, text.Length));
            return e;
        }

        [Fact]
        public void Enter_does_nothing_in_the_editor_and_keeps_the_selection()
        {
            var e = Single("abc");
            var undo = new SimpleUndoStack();
            undo.Attach(e);
            e.SetCaret(new TextPosition(0, 1));
            e.SetCaret(new TextPosition(0, 3), true);
            e.Enter();
            Assert.Single(e.Document.Paragraphs);
            Assert.Equal("abc", e.Document.Paragraphs[0].Text);
            Assert.True(e.HasSelection);
            Assert.Empty(undo.Done);
        }

        [Fact]
        public void A_line_break_in_typed_text_becomes_a_space()
        {
            var e = Single("ab");
            e.TypeText("x\ny\r\nz");
            Assert.Single(e.Document.Paragraphs);
            Assert.Equal("abx y z", e.Document.Paragraphs[0].Text);
        }

        [Fact]
        public void Pasted_paragraphs_are_joined_into_one_with_a_space_in_the_text_before_it()
        {
            var e = Single("ab");
            var one = Para("en", "en", "one");
            var two = Para("fr", "fr", "deux");
            two.Style = "Other";
            var three = Para("en", "en", "three");
            e.Paste(new List<Paragraph> { one, two, three });
            Assert.Single(e.Document.Paragraphs);
            Assert.Equal("abone deux three", e.Document.Paragraphs[0].Text);
            Assert.Equal(new TextPosition(0, 16), e.Caret);
            // the space after "one" is English and the one after "deux" is French, so they merge into the runs before them
            var runs = e.Document.Paragraphs[0].Runs;
            Assert.Equal(new[] { "abone ", "deux ", "three" }, new[] { runs[0].Text, runs[1].Text, runs[2].Text });
            Assert.Equal("fr", runs[1].Ws);
        }

        [Theory]
        [InlineData("\r")]
        [InlineData("\n")]
        [InlineData("\r\n")]
        [InlineData("\u2029")]
        [InlineData("\u0085")]
        public void Every_paragraph_separator_in_typed_text_becomes_a_space(string separator)
        {
            var e = Single("X");
            e.TypeText("a" + separator + "b");
            Assert.Single(e.Document.Paragraphs);
            Assert.Equal("Xa b", e.Document.Paragraphs[0].Text);
        }

        [Theory]
        [InlineData("foo\r\n", "Xfoo")]
        [InlineData("foo\n\n\n", "Xfoo")]
        [InlineData("a\n\n", "Xa")]
        [InlineData("a\r\n\r\n", "Xa")]
        [InlineData("a\n\r", "Xa")]
        [InlineData("a\n\t", "Xa")]
        [InlineData("\r\nfoo", "X foo")]
        [InlineData("a\r\n\r\n\u2029\rb", "Xa    b")]
        [InlineData("\n\n", "X")]
        [InlineData("\n", "X")]
        public void Each_separator_is_one_space_and_trailing_ones_are_dropped(string pasted, string expected)
        {
            var e = Single("X");
            e.TypeText(pasted);
            Assert.Equal(expected, e.Document.Paragraphs[0].Text);
        }

        [Theory]
        [InlineData("foo\nbar\n", "Xfoo bar")]
        [InlineData("a\n\n", "Xa")]
        [InlineData("a\r\n\r\n", "Xa")]
        [InlineData("a\r\n\n", "Xa")]
        [InlineData("foo\n\nbar", "Xfoo  bar")]
        [InlineData("\nfoo", "X foo")]
        [InlineData("\n\n", "X")]
        [InlineData("a\u2028\nb", "Xa\u2028 b")]
        public void Separators_inside_one_pasted_run_are_one_space_each_and_trailing_ones_are_dropped(string pasted, string expected)
        {
            var e = Single("X");
            e.Paste(new List<Paragraph> { Para("en", "en", pasted) });
            Assert.Equal(expected, e.Document.Paragraphs[0].Text);
        }

        [Fact]
        public void Separators_become_spaces_across_run_and_paragraph_boundaries()
        {
            var e = Single("X");
            var two = Para("en", "en", "foo\n");
            two.Runs.Add(new Run { Text = "\nbar\n", Ws = "fr" });
            e.Paste(new List<Paragraph> { two, Para("en", "en", "baz\n"), Para("en", "en", "\n") });
            Assert.Equal("Xfoo  bar  baz", e.Document.Paragraphs[0].Text);
            Assert.Equal(new TextPosition(0, 14), e.Caret);
        }

        [Fact]
        public void Pasting_only_separators_over_a_selection_replaces_it_with_nothing()
        {
            var e = Single("hello");
            e.SetCaret(new TextPosition(0, 0));
            e.SetCaret(new TextPosition(0, 5), true);
            e.PasteText("\n");
            Assert.Equal(string.Empty, e.Document.Paragraphs[0].Text);

            e = Single("hello");
            e.SetCaret(new TextPosition(0, 1));
            e.PasteText("\r\n");
            Assert.Equal("hello", e.Document.Paragraphs[0].Text);
        }

        [Fact]
        public void A_hard_line_break_is_not_a_paragraph_separator_and_stays_in_the_paragraph()
        {
            var e = Single("X");
            e.TypeText("a\u2028b");
            Assert.Equal("Xa\u2028b", e.Document.Paragraphs[0].Text);
        }

        [Fact]
        public void Pasted_empty_paragraphs_are_separators_too()
        {
            var e = Single("X");
            e.Paste(new List<Paragraph> { Para("en", "en", "foo"), Para("en") });
            Assert.Equal("Xfoo", e.Document.Paragraphs[0].Text);

            e = Single("X");
            e.Paste(new List<Paragraph> { Para("en", "en", "a"), Para("en"), Para("en", "en", "b"), Para("en") });
            Assert.Equal("Xa  b", e.Document.Paragraphs[0].Text);

            e = Single("X");
            e.Paste(new List<Paragraph> { Para("en"), Para("en", "en", "a") });
            Assert.Equal("X a", e.Document.Paragraphs[0].Text);   // a leading empty paragraph is a leading separator
        }

        [Theory]
        [InlineData("X", "\nb", "X b")]
        [InlineData("X", "\r\n\r\nb", "X  b")]
        [InlineData("X", " \n b", "X   b")]
        [InlineData("X ", "\nb", "X  b")]
        [InlineData("X ", "\n b", "X   b")]
        [InlineData("", "\nb", " b")]
        [InlineData("a\u2028", "\nb", "a\u2028 b")]
        [InlineData("X", "b\n", "Xb")]
        [InlineData("X", "b \n", "Xb ")]
        [InlineData("X", "a \n b", "Xa   b")]
        [InlineData("X", "a\u2029  b", "Xa   b")]
        [InlineData("X", "a  b", "Xa  b")]
        [InlineData("X", "a\u00A0\nb", "Xa\u00A0 b")]
        public void A_separator_is_one_space_whatever_is_beside_it(string existing, string pasted, string expected)
        {
            var typed = Single(existing);
            typed.TypeText(pasted);
            Assert.Equal(expected, typed.Document.Paragraphs[0].Text);

            var plain = Single(existing);
            plain.PasteText(pasted);
            Assert.Equal(expected, plain.Document.Paragraphs[0].Text);

            var runs = Single(existing);
            runs.Paste(new List<Paragraph> { Para("en", "en", pasted) });
            Assert.Equal(expected, runs.Document.Paragraphs[0].Text);
        }

        [Theory]
        [InlineData("a\tb", "Xab")]
        [InlineData("\ta\t\tb\t", "Xab")]
        [InlineData("a\t\nb", "Xa b")]
        [InlineData("a\r\t\nb", "Xa  b")]
        [InlineData("a\n\t", "Xa")]
        [InlineData("\t", "X")]
        public void Tabs_are_removed(string pasted, string expected)
        {
            var typed = Single("X");
            typed.TypeText(pasted);
            Assert.Equal(expected, typed.Document.Paragraphs[0].Text);

            var plain = Single("X");
            plain.PasteText(pasted);
            Assert.Equal(expected, plain.Document.Paragraphs[0].Text);

            var runs = Single("X");
            runs.Paste(new List<Paragraph> { Para("en", "en", pasted) });
            Assert.Equal(expected, runs.Document.Paragraphs[0].Text);
        }

        [Fact]
        public void Tabs_are_removed_when_the_field_takes_paragraphs_too()
        {
            var e = new DocumentEditor(OneParagraph("X"));
            e.SetCaret(new TextPosition(0, 1));
            e.Paste(new List<Paragraph> { Para("en", "en", "a\tb") });
            Assert.Equal("Xab", e.Document.Paragraphs[0].Text);

            e = new DocumentEditor(OneParagraph("X"));
            e.SetCaret(new TextPosition(0, 1));
            e.Paste(new List<Paragraph> { Para("en", "en", "\ta\t"), Para("en", "en", "\tb\t\tc") });
            Assert.Equal(new[] { "Xa", "bc" }, new[] { e.Document.Paragraphs[0].Text, e.Document.Paragraphs[1].Text });
            Assert.Equal(new TextPosition(1, 2), e.Caret);

            e = new DocumentEditor(OneParagraph("X"));
            e.SetCaret(new TextPosition(0, 1));
            e.PasteText("a\tb\n\tc");
            Assert.Equal(new[] { "Xab", "c" }, new[] { e.Document.Paragraphs[0].Text, e.Document.Paragraphs[1].Text });
        }

        [Fact]
        public void A_trailing_newline_pasted_before_existing_text_fuses_the_words()
        {
            var e = Single("bcd");
            e.SetCaret(new TextPosition(0, 0));
            e.PasteText("a\n");
            Assert.Equal("abcd", e.Document.Paragraphs[0].Text);
        }

        [Fact]
        public void A_long_single_run_with_many_newlines_pastes_in_linear_time()
        {
            var line = new string('x', 49) + "\n";
            var text = string.Concat(System.Linq.Enumerable.Repeat(line, 8000));   // 400,000 characters in one run
            var e = Single(string.Empty);
            // Counted, not timed: what the paste allocates grows with the text when it is linear, and with its square when each
            // character is appended to a string of the run so far (a 400,000-character run would allocate over 100 GB that way).
            // The runtime's own count for this thread does not depend on how busy the machine is.
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            e.Paste(new List<Paragraph> { Para("en", "en", text) });
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(text.Length - 1, e.Document.Paragraphs[0].Text.Length);
            Assert.True(allocated < 200L * text.Length, $"{allocated} bytes allocated for {text.Length} characters");
        }

        [Fact]
        public void Many_pasted_paragraphs_join_into_one_with_a_space_between_each()
        {
            // The cost of a large paste is counted in the field tests (the layout lookups); here the join is a plain result check.
            var paragraphs = new List<Paragraph>();
            for (int i = 0; i < 16000; i++) paragraphs.Add(Para("en", i % 2 == 0 ? "en" : "fr", "line " + i));
            var e = Single(string.Empty);
            e.Paste(paragraphs);
            Assert.Single(e.Document.Paragraphs);
            Assert.StartsWith("line 0 line 1 line 2 ", e.Document.Paragraphs[0].Text);
            Assert.EndsWith(" line 15999", e.Document.Paragraphs[0].Text);
            Assert.Equal(16000, e.Document.Paragraphs[0].Runs.Count);
        }

        [Fact]
        public void A_leading_empty_paragraph_or_one_that_starts_with_whitespace_beside_a_break_gets_one_space_too()
        {
            var e = Single("X");
            e.Paste(new List<Paragraph> { Para("en"), Para("en", "en", "foo") });
            Assert.Equal("X foo", e.Document.Paragraphs[0].Text);

            e = Single("X");
            e.Paste(new List<Paragraph> { Para("en", "en", "a "), Para("en", "en", " b") });
            Assert.Equal("Xa   b", e.Document.Paragraphs[0].Text);
        }

        [Fact]
        public void A_leading_separator_replacing_a_selection_looks_at_the_text_before_the_selection()
        {
            var e = Single("Xhello");
            e.SetCaret(new TextPosition(0, 1));
            e.SetCaret(new TextPosition(0, 6), true);
            e.PasteText("\nb");
            Assert.Equal("X b", e.Document.Paragraphs[0].Text);
        }

        [Fact]
        public void A_run_inside_a_pasted_paragraph_that_holds_a_separator_gets_a_space_too()
        {
            var e = Single("X");
            e.Paste(new List<Paragraph> { Para("en", "en", "a\u2029b"), Para("en", "en", "c") });
            Assert.Equal("Xa b c", e.Document.Paragraphs[0].Text);
        }

        [Fact]
        public void A_paste_of_one_paragraph_and_ordinary_typing_work_as_before()
        {
            var e = Single("ab");
            e.Paste(new List<Paragraph> { Para("en", "en", "XY") });
            e.TypeText("!");
            Assert.Equal("abXY!", e.Document.Paragraphs[0].Text);
        }

        [Fact]
        public void A_document_with_several_paragraphs_keeps_them_and_Backspace_may_still_join()
        {
            var e = new DocumentEditor(Doc(Para("en", "en", "one"), Para("en", "en", "two"))) { SingleParagraph = true };
            e.SetCaret(new TextPosition(1, 0));
            e.Backspace();
            Assert.Single(e.Document.Paragraphs);
            Assert.Equal("onetwo", e.Document.Paragraphs[0].Text);
        }

        [Fact]
        public void Turning_the_mode_off_restores_Enter()
        {
            var e = Single("ab");
            e.SingleParagraph = false;
            e.Enter();
            Assert.Equal(2, e.Document.Paragraphs.Count);
        }
    }
}
