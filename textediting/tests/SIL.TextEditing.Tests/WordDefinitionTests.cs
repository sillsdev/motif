using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SIL.TextEditing.Tests
{
    /// <summary>Issue 93: one word definition for what a double-click and a context request call a word and what the spell checker marks, as FieldWorks has
    /// (an underscore is punctuation), and the "make the whole word writing system X" parts of a mixed word.</summary>
    public class WordDefinitionTests
    {
        static string C(int codePoint) => char.ConvertFromUtf32(codePoint);

        static readonly string[] Pieces =
        {
            "a", "b", "ab", "abc", "x", "xy", " ", " ", "  ", "'", "'", C(0x2019), "-", ".", ",", "1", "23", "_", "__",
            C(0x1D400), C(0x1D401) + "b", "e" + C(0x301), C(0xE9), C(0xAD), C(0x200B), C(0x2C2), "^", "`", "\t", "!", "(", C(0x5D0), C(0x5D1),
            C(0xB2), C(0x2160), C(0xFF3F), C(0x203F),
        };

        // A trailing zero-width joiner or non-joiner belongs to the grapheme cluster before it; the spell checker's word stops before it.
        static (int, int) WithoutTrailingJoiners(string text, int start, int end)
        {
            while (end > start && (text[end - 1] == (char)0x200D || text[end - 1] == (char)0x200C)) end--;
            return (start, end);
        }

        [Fact]
        public void The_word_a_double_click_takes_is_the_word_the_spell_checker_finds_at_every_character()
        {
            var rng = new Random(93);
            var host = new FakeSpellHost();
            int words = 0;
            for (int round = 0; round < 4000; round++)
            {
                string text = string.Concat(Enumerable.Range(0, rng.Next(1, 10)).Select(_ => Pieces[rng.Next(Pieces.Length)]));
                var p = TestDocs.Para("en", "en", text);
                for (int i = 0; i < text.Length; i++)
                {
                    if (char.IsLowSurrogate(text[i]) || text[i] == (char)0x200D || text[i] == (char)0x200C) continue;   // the middle of a pair or of a cluster is not a place to click
                    bool breaks = WordBreaks.TryWordAt(text, i, out int a, out int b);
                    bool spell = SpellScanner.TryWordAt(p, i, host, out int s, out int e);
                    if (breaks) (a, b) = WithoutTrailingJoiners(text, a, b);
                    Assert.True(breaks == spell, "[" + text.Replace("\t", "<tab>") + "] at " + i + ": double-click says " + breaks + " " + a + "," + b + ", spell checker says " + spell + " " + s + "," + e);
                    if (!breaks) continue;
                    Assert.True(a == s && b == e, "[" + text + "] at " + i + ": " + a + "," + b + " against " + s + "," + e);
                    words++;
                }
            }
            Assert.True(words > 5000);
        }

        [Fact]
        public void A_zero_width_joiner_between_letters_keeps_the_word_whole_in_both_and_one_after_a_space_starts_nothing()
        {
            string joined = "a" + C(0x200D) + "b c";
            var p = TestDocs.Para("en", "en", joined);
            var host = new FakeSpellHost();
            Assert.True(WordBreaks.TryWordAt(joined, 0, out int a, out int b));
            Assert.Equal((0, 3), (a, b));
            Assert.True(SpellScanner.TryWordAt(p, 2, host, out a, out b));
            Assert.Equal((0, 3), (a, b));
            string spaced = "x " + C(0x200D) + "a";
            Assert.True(WordBreaks.TryWordAt(spaced, 3, out a, out b));
            Assert.Equal((3, 4), (a, b));
            Assert.True(SpellScanner.TryWordAt(TestDocs.Para("en", "en", spaced), 3, host, out a, out b));
            Assert.Equal((3, 4), (a, b));
        }

        [Fact]
        public void An_underscore_ends_a_word_for_the_double_click_and_the_spell_checker_alike()
        {
            const string text = "foo_bar";
            Assert.True(WordBreaks.TryWordAt(text, 5, out int a, out int b));
            Assert.Equal((4, 7), (a, b));
            var host = new FakeSpellHost();
            var p = TestDocs.Para("en", "en", text);
            Assert.True(SpellScanner.TryWordAt(p, 5, host, out a, out b));
            Assert.Equal((4, 7), (a, b));
            Assert.False(SpellScanner.TryWordAt(p, 3, host, out a, out b));
            Assert.Equal(new[] { 0, 4 }, SpellScanner.Check(p, host).Select(m => m.Start));
        }

        [Fact]
        public void A_host_that_makes_the_underscore_a_letter_moves_the_spell_checkers_word_and_a_context_request_with_it()
        {
            var host = new FakeSpellHost();
            host.WordForming.Add('_');
            var p = TestDocs.Para("en", "en", "foo_bar baz");
            Assert.True(SpellScanner.TryWordAt(p, 5, host, out int a, out int b));
            Assert.Equal((0, 7), (a, b));
            var editor = new DocumentEditor(TestDocs.Doc(p)) { WordRules = host };
            var info = editor.DescribeContext(new TextPosition(0, 5), false);
            Assert.Equal("foo_bar", info.Word);
            Assert.Equal(new TextRange(0, 0, 7), info.WordRange);
            editor.WordRules = null;
            info = editor.DescribeContext(new TextPosition(0, 5), false);
            Assert.Equal("bar", info.Word);
        }

        [Fact]
        public void The_word_of_a_context_request_stops_at_an_object_where_the_spell_checkers_word_goes_through_it()
        {
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(new Run { Ws = "en", Text = "ab" });
            p.Runs.Add(Run.Object("en", "pic"));
            p.Runs.Add(new Run { Ws = "en", Text = "cd ef" });
            var host = new FakeSpellHost();
            Assert.True(SpellScanner.TryWordAt(p, 3, host, out int a, out int b));
            Assert.Equal((3, 5), (a, b));                  // "cd"
            Assert.True(SpellScanner.TryWordAt(p, 0, host, out a, out b));
            Assert.Equal((0, 2), (a, b));                  // "ab"
            Assert.False(SpellScanner.TryWordAt(p, 2, host, out a, out b));   // the object alone is not a word
            var mark = SpellScanner.Check(p, host).First();
            Assert.Equal((0, 5, SpellingProblem.Embedded), (mark.Start, mark.End, mark.Problem));
        }

        // ---- make the whole word writing system X

        static Paragraph Mixed() => TestDocs.Para("en", "en", "ab", "fr", "cd", "de", "ef");

        [Fact]
        public void A_mixed_word_lists_its_writing_systems_once_each_in_order_and_an_objects_does_not_count()
        {
            var s = SpellScanner.SuggestionsAt(Mixed(), 0, 1, new FakeSpellHost())!;
            Assert.Equal(SpellingProblem.Mixed, s.Problem);
            Assert.Equal(new[] { "en", "fr", "de" }, s.WritingSystems);
            var withObject = new Paragraph { DefaultWs = "en" };
            withObject.Runs.Add(new Run { Ws = "en", Text = "ab" });
            withObject.Runs.Add(Run.Object("fr", "pic"));
            withObject.Runs.Add(new Run { Ws = "en", Text = "cd" });
            var e = SpellScanner.SuggestionsAt(withObject, 0, 1, new FakeSpellHost())!;
            Assert.Equal(SpellingProblem.Embedded, e.Problem);
            Assert.Equal(new[] { "en" }, e.WritingSystems);
            Assert.Equal(new[] { "en" }, SpellScanner.SuggestionsAt(TestDocs.Para("en", "en", "zzz"), 0, 1, new FakeSpellHost())!.WritingSystems);
        }

        [Fact]
        public void The_whole_word_in_one_writing_system_keeps_the_text_and_every_other_property_and_merges_what_became_alike()
        {
            var p = Mixed();
            p.Runs[0].Props.SetInt(TextPropertyNumbers.Bold, 1);
            p.Runs[1].Props.SetInt(TextPropertyNumbers.Bold, 1);
            p.Runs[2].Props.SetInt(TextPropertyNumbers.Italic, 1);
            var s = SpellScanner.SuggestionsAt(p, 0, 1, new FakeSpellHost())!;
            var runs = s.WithWholeWordIn("fr");
            Assert.All(runs, r => Assert.Equal("fr", r.Ws));
            Assert.Equal("abcdef", string.Concat(runs.Select(r => r.Text)));
            Assert.Equal(new[] { "abcd", "ef" }, runs.Select(r => r.Text));       // the first two were bold, so they merge; the italic one stays apart
            Assert.True(runs[0].Props.TryGetInt(TextPropertyNumbers.Bold, out var bold) && bold == 1);
            Assert.True(runs[1].Props.TryGetInt(TextPropertyNumbers.Italic, out var italic) && italic == 1);
            Assert.Equal("ab", s.Parts[0].Text);                                   // the answer's own parts are untouched
            Assert.Equal("en", s.Parts[0].Ws);
            Assert.Throws<ArgumentNullException>(() => s.WithWholeWordIn(null!));
        }

        [Fact]
        public void The_whole_word_in_one_writing_system_keeps_an_object_and_its_data()
        {
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(new Run { Ws = "en", Text = "ab" });
            p.Runs.Add(Run.Object("fr", "pic"));
            p.Runs.Add(new Run { Ws = "fr", Text = "cd" });
            var s = SpellScanner.SuggestionsAt(p, 0, 1, new FakeSpellHost())!;
            var runs = s.WithWholeWordIn("en");
            Assert.Equal(3, runs.Count);
            Assert.Equal("pic", runs[1].ObjData);
            Assert.All(runs, r => Assert.Equal("en", r.Ws));
        }
    }
}
