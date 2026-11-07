using System.Collections.Generic;
using System.Linq;
using SIL.TextEditing.Tests;
using Xunit;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>Issue 107: an edit between the two halves of a surrogate pair, which the field's own caret never makes but a host or a regular
    /// expression can, leaves the marks a full check would find once the idle work has caught up.</summary>
    public class SpellingSplitPairTests
    {
        static RichTextField Field(string text, ISpellCheckHost host)
        {
            var field = Fixtures.Field(TestDocs.Doc(TestDocs.Para("en", "en", text)));
            field.SpellIdlePost = a => { };
            field.SpellChecking = host;
            return field;
        }

        static void CheckAll(RichTextField f) { while (!f.CheckSpellingStep()) { } }

        static void AssertSameAsFullCheck(RichTextField f, ISpellCheckHost host)
        {
            CheckAll(f);
            var reference = ReferenceSpellScanner.Check(f.Editor.Document.Paragraphs[0], host);
            Assert.Equal(reference, f.SpellingMarks(0).ToList());
        }

        [Fact]
        public void A_space_typed_between_the_halves_of_a_pair_keeps_the_mark_of_the_word_before_it() => Headless.Run(() =>
        {
            var host = new FakeSpellHost("ab");
            var f = Field("zzz\U00010400 ab", host);
            CheckAll(f);
            Assert.Equal(new[] { SpellingProblem.Misspelled }, f.SpellingMarks(0).Select(m => m.Problem));
            f.Editor.SetCaret(new TextPosition(0, 4));                   // between U+D801 and U+DC00; SetCaret does not snap
            f.Editor.TypeText(" ");
            AssertSameAsFullCheck(f, host);
            Assert.Equal("zzz", f.Editor.Document.Paragraphs[0].Text.Substring(f.SpellingMarks(0)[0].Start, f.SpellingMarks(0)[0].Length));
        });

        [Fact]
        public void A_space_typed_between_the_halves_of_a_pair_keeps_the_mark_of_the_word_after_it() => Headless.Run(() =>
        {
            var host = new FakeSpellHost("ab");
            var f = Field("ab \U00010400zzz", host);
            CheckAll(f);
            f.Editor.SetCaret(new TextPosition(0, 4));
            f.Editor.TypeText(" ");
            AssertSameAsFullCheck(f, host);
            Assert.Single(f.SpellingMarks(0));
        });

        [Fact]
        public void Every_edit_at_every_offset_of_text_with_pairs_ends_with_the_marks_of_a_full_check() => Headless.Run(() =>
        {
            var host = new FakeSpellHost("ab", "cd");
            string text = "zzz\U00010400 ab \U00010401yy cd\U00010400\U00010401q";
            foreach (string typed in new[] { " ", "a", "'" })
            {
                for (int at = 0; at <= text.Length; at++)
                {
                    var f = Field(text, host);
                    CheckAll(f);
                    f.Editor.SetCaret(new TextPosition(0, at));
                    f.Editor.TypeText(typed);
                    AssertSameAsFullCheck(f, host);
                }
            }
        });
    }
}
