using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SIL.TextEditing.Tests
{
    /// <summary>Issue 102: one word rule for the double-click, Ctrl+Left and Right, the whole-word find, the context request and the spell checker: the
    /// host's word-forming rule for the writing system of each character, digits, and the characters that join words.</summary>
    public class OneWordRuleTests
    {
        static string C(int codePoint) => char.ConvertFromUtf32(codePoint);

        static readonly string ZWNJ = C(0x200C), ZWJ = C(0x200D);

        // Exotic input first: an apostrophe followed by a combining mark (between letters; a mark with no letter before it is the one difference left, below), a zero-width non-joiner before an apostrophe, joiners at the edges, surrogate pairs,
        // modifier symbols, an object, no-break spaces and hyphens.
        static readonly string[] Pieces =
        {
            "a", "b", "ab", "cd", " ", "  ", "'", "a'" + C(0x301) + "b", C(0x2019), "b" + C(0x2019) + C(0x301) + "a", ZWNJ, ZWJ, ZWNJ + "'", "'" + ZWNJ, ZWJ + "'",
            "-", "--", ".", ",", "1", "23", "_", C(0x1D400), C(0x1D401) + "b", "e" + C(0x301), C(0xE9), C(0xAD), C(0x200B), C(0x2C2), "^", "\t", "!",
            C(0x5D0), C(0x5D1), C(0xB2), C(0xA0), "x" + ZWNJ, "y" + ZWJ, "q'", "'q",
        };

        // The text of a paragraph in writing systems "en" and "xx", cut at random places, and a host for which a hyphen and an underscore are letters
        // in "xx" only.
        static Paragraph Build(string text, Random rng)
        {
            var p = new Paragraph { DefaultWs = "en" };
            int i = 0;
            while (i < text.Length)
            {
                int n = Math.Min(text.Length - i, rng.Next(1, 6));
                if (n > 0 && i + n < text.Length && char.IsHighSurrogate(text[i + n - 1])) n++;          // a pair is never cut
                p.Runs.Add(new Run { Ws = rng.Next(2) == 0 ? "en" : "xx", Text = text.Substring(i, n) });
                i += n;
            }
            return p;
        }

        sealed class HyphenHost : SpellCheckHostBase
        {
            public override ISpellChecker? GetChecker(string ws) => null;
            public override bool IsWordForming(string ws, int codePoint) => (ws == "xx" && (codePoint == '-' || codePoint == '_')) || SpellScanner.DefaultIsWordForming(codePoint);
        }

        // A trailing joiner belongs to the grapheme cluster before it, which the double-click keeps whole; the spell checker's word stops before it.
        static (int, int) WithoutTrailingJoiners(string text, int start, int end)
        {
            while (end > start && (text[end - 1] == (char)0x200D || text[end - 1] == (char)0x200C)) end--;
            return (start, end);
        }

        static bool InsideACluster(string text, int index) => !GraphemeClusters.Starts(text).Contains(index);

        [Fact]
        public void The_word_a_double_click_takes_is_the_word_the_spell_checker_finds_at_every_character_for_exotic_input_and_the_hosts_rule()
        {
            var rng = new Random(102);
            var host = new HyphenHost();
            int words = 0, joinedByHost = 0;
            for (int round = 0; round < 6000; round++)
            {
                string text = string.Concat(Enumerable.Range(0, rng.Next(1, 9)).Select(_ => Pieces[rng.Next(Pieces.Length)]));
                var p = Build(text, rng);
                for (int i = 0; i < text.Length; i++)
                {
                    if (InsideACluster(text, i)) continue;     // the middle of a pair or of a cluster is not a place to click
                    bool breaks = WordBreaks.TryWordAt(p, i, host, out int a, out int b);
                    bool spell = SpellScanner.TryWordAt(p, i, host, out int s, out int e);
                    if (breaks) (a, b) = WithoutTrailingJoiners(text, a, b);
                    string what = "[" + text.Replace("\t", "<tab>").Replace("‌", "<ZWNJ>").Replace("‍", "<ZWJ>") + "] at " + i + ": double-click says " + breaks + " " + a + "," + b + ", spell checker says " + spell + " " + s + "," + e;
                    Assert.True(breaks == spell, what);
                    if (!breaks) continue;
                    Assert.True(a == s && b == e, what);
                    words++;
                    if (text.Substring(a, b - a).IndexOfAny(new[] { '-', '_' }) >= 0) joinedByHost++;
                }
            }
            Assert.True(words > 10000, words + " words");
            Assert.True(joinedByHost > 100, "the host's rule was never at work: " + joinedByHost);
        }

        [Fact]
        public void An_apostrophe_followed_by_a_combining_mark_stays_in_the_word_for_both()
        {
            string text = "a'" + C(0x301) + "b c";
            var p = TestDocs.Para("en", "en", text);
            Assert.True(WordBreaks.TryWordAt(p, 0, null, out int a, out int b));
            Assert.Equal((0, 4), (a, b));
            Assert.True(SpellScanner.TryWordAt(p, 0, new FakeSpellHost(), out a, out b));
            Assert.Equal((0, 4), (a, b));
            Assert.Equal(new[] { (0, 4) }, SpellScanner.Check(p, new FakeSpellHost("c")).Select(m => (m.Start, m.End)));
            Assert.Equal(5, WordBreaks.NextWordStart(p, 0, null));
        }

        [Fact]
        public void A_zero_width_non_joiner_between_a_word_and_an_apostrophe_does_not_join_the_apostrophe_to_the_word_for_both()
        {
            string text = "a" + ZWNJ + "'b";
            var p = TestDocs.Para("en", "en", text);
            var host = new FakeSpellHost();
            // the spell checker: "a" and "b" are two words and the apostrophe belongs to neither
            Assert.Equal(new[] { (0, 1), (3, 4) }, Enumerable.Range(0, text.Length).Where(i => !InsideACluster(text, i) && SpellScanner.TryWordAt(p, i, host, out _, out _))
                .Select(i => { SpellScanner.TryWordAt(p, i, host, out int s, out int e); return (s, e); }).Distinct());
            // the double-click: the same two words (the first keeps its trailing joiner, which is part of its grapheme cluster)
            Assert.True(WordBreaks.TryWordAt(p, 0, null, out int a, out int b));
            Assert.Equal((0, 2), (a, b));
            Assert.True(WordBreaks.TryWordAt(p, 3, null, out a, out b));
            Assert.Equal((3, 4), (a, b));
            Assert.False(WordBreaks.TryWordAt(p, 2, null, out _, out _));
        }
        // ---- the host's rule reaches every word question

        static Paragraph Para(params string[] wsAndText) => TestDocs.Para("en", wsAndText);

        [Fact]
        public void A_double_click_and_the_word_moves_follow_a_host_that_makes_the_hyphen_a_letter_in_one_writing_system()
        {
            var host = new HyphenHost();
            var inX = Para("xx", "foo-bar baz");
            var inEn = Para("en", "foo-bar baz");
            Assert.True(WordBreaks.TryWordAt(inX, 5, host, out int a, out int b));
            Assert.Equal((0, 7), (a, b));
            WordBreaks.WordAt(inX, 1, host, out a, out b);
            Assert.Equal((0, 7), (a, b));
            Assert.Equal(8, WordBreaks.NextWordStart(inX, 0, host));
            Assert.Equal(8, WordBreaks.PreviousWordStart(inX, 11, host));
            Assert.Equal(0, WordBreaks.PreviousWordStart(inX, 7, host));
            // the same text in a writing system for which the hyphen is punctuation, and with no host rule at all
            WordBreaks.WordAt(inEn, 5, host, out a, out b);
            Assert.Equal((4, 7), (a, b));
            Assert.Equal(3, WordBreaks.NextWordStart(inEn, 0, host));
            Assert.Equal(3, WordBreaks.NextWordStart(inX, 0, null));
            WordBreaks.WordAt(inX, 5, null, out a, out b);
            Assert.Equal((4, 7), (a, b));
        }

        [Fact]
        public void The_rule_is_asked_for_the_writing_system_of_each_character()
        {
            var host = new HyphenHost();
            var split = Para("en", "foo", "xx", "-", "en", "bar baz");           // only the hyphen is in the writing system that makes it a letter
            Assert.True(WordBreaks.TryWordAt(split, 5, host, out int a, out int b));
            Assert.Equal((0, 7), (a, b));
            var other = Para("en", "foo-", "xx", "bar baz");                       // the hyphen is in "en": punctuation
            Assert.True(WordBreaks.TryWordAt(other, 5, host, out a, out b));
            Assert.Equal((4, 7), (a, b));
            // the spell checker takes the same words: "foo-bar" (which mixes two writing systems) and "baz"
            Assert.Equal(new[] { 0, 8 }, SpellScanner.Check(split, new HyphenHostWithChecker()).Select(m => m.Start));
        }

        sealed class HyphenHostWithChecker : SpellCheckHostBase
        {
            public override ISpellChecker? GetChecker(string ws) => new FakeBareChecker();
            public override bool IsWordForming(string ws, int codePoint) => (ws == "xx" && codePoint == '-') || SpellScanner.DefaultIsWordForming(codePoint);
        }

        [Fact]
        public void The_word_boundaries_a_whole_word_find_uses_follow_the_hosts_rule_for_the_paragraph()
        {
            var host = new HyphenHost();
            var p = Para("xx", "foo-bar baz");
            var edit = new DocumentEditor(TestDocs.Doc(p));
            var options = new FindOptions { Pattern = "bar", WholeWord = true };
            Assert.Single(edit.FindAllIn(0, options));                      // with the default rule the hyphen ends a word
            edit.WordRules = host;
            Assert.Empty(edit.FindAllIn(0, options));                               // with the host's it does not: "bar" is the end of "foo-bar"
            Assert.Single(edit.FindAllIn(0, new FindOptions { Pattern = "foo-bar", WholeWord = true }));
            Assert.Single(edit.FindAllIn(0, new FindOptions { Pattern = "baz", WholeWord = true }));
        }

        [Fact]
        public void A_whole_word_find_looks_up_the_writing_system_where_the_character_is_even_with_objects_in_the_text()
        {
            var host = new HyphenHost();
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(new Run { Ws = "en", Text = "ab" });
            p.Runs.Add(Run.Object("en", "pic"));
            p.Runs.Add(new Run { Ws = "xx", Text = "-cd ef" });                     // judged without the object: "ab-cd", a word only for the host
            var edit = new DocumentEditor(TestDocs.Doc(p));
            var options = new FindOptions { Pattern = "cd", WholeWord = true };
            Assert.Single(edit.FindAllIn(0, options));
            edit.WordRules = host;
            Assert.Empty(edit.FindAllIn(0, options));
            Assert.Single(edit.FindAllIn(0, new FindOptions { Pattern = "ef", WholeWord = true }));
        }

        [Fact]
        public void The_word_of_a_context_request_follows_the_editors_word_rule()
        {
            var edit = new DocumentEditor(TestDocs.Doc(Para("xx", "foo-bar baz")));
            Assert.Equal("bar", edit.DescribeContext(new TextPosition(0, 5), false).Word);
            edit.WordRules = new HyphenHost();
            var info = edit.DescribeContext(new TextPosition(0, 5), false);
            Assert.Equal("foo-bar", info.Word);
            Assert.Equal(new TextRange(0, 0, 7), info.WordRange);
        }

        [Fact]
        public void A_combining_mark_with_nothing_before_it_to_stand_on_stays_with_what_it_follows_for_a_double_click_though_the_spell_checker_calls_it_a_word()
        {
            // The one difference left, by design: a caret and a selection never split a grapheme cluster, so a mark after a space or punctuation is part of that
            // cluster (here the hyphen and its mark are one stop). The spell checker walks code points, as FieldWorks does, and starts a word at the mark.
            string text = "a -" + C(0x301) + "b";                                  // a, space, hyphen, mark, b
            var p = TestDocs.Para("en", "en", text);
            Assert.False(WordBreaks.TryWordAt(p, 3, null, out _, out _));
            WordBreaks.WordAt(p, 3, null, out int a, out int b);
            Assert.Equal((2, 4), (a, b));
            Assert.True(SpellScanner.TryWordAt(p, 3, new FakeSpellHost(), out a, out b));
            Assert.Equal((3, 5), (a, b));
        }
    }
}
