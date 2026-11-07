using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SIL.TextEditing.Tests
{
    /// <summary>Issue 91: the scanner looks at a window of the paragraph, not all of it, and finds what a pass over the whole paragraph finds.</summary>
    public class SpellingWindowTests
    {
        static readonly string[] Pieces =
        {
            "a", "b", "ab", "abc", "ba", "x", "xy", " ", " ", "  ", "'", "'", "\u2019", "\u200D", "\u200C", "-", ".", ",", "1", "23", "_",
            "\uD835\uDC00", "\uD835\uDC01b", "e\u0301", "\u00E9", "\uFFFC", "\uFEFF", "\u2028", "q", "zz", "don't",
        };

        static Paragraph RandomParagraph(Random rng)
        {
            var p = new Paragraph { DefaultWs = "en" };
            int runs = rng.Next(1, 6);
            for (int i = 0; i < runs; i++)
            {
                var text = string.Concat(Enumerable.Range(0, rng.Next(0, 9)).Select(_ => Pieces[rng.Next(Pieces.Length)]));
                Run run;
                if (rng.Next(12) == 0) run = Run.Object(rng.Next(2) == 0 ? "en" : "fr", "o");
                else run = new Run { Ws = rng.Next(4) == 0 ? "fr" : "en", Text = text.Replace("\uFFFC", "") };
                int kind = rng.Next(8);
                if (kind == 0) run.Props.SetInt(TextPropertyNumbers.SpellCheck, TextPropertyNumbers.SpellCheckDoNot);
                else if (kind == 1) run.Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableNo);
                else if (kind == 2) { run.Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableNo); run.Props.SetInt(TextPropertyNumbers.SpellCheck, TextPropertyNumbers.SpellCheckForce); }
                else if (kind == 3) run.Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableSemi);
                p.Runs.Add(run);
            }
            return p;
        }

        static FakeSpellHost RandomHost(Random rng)
        {
            var host = new FakeSpellHost("a", "ab", "don't", "e\u0301", "\u00E9", "\uD835\uDC00").With("fr", "a", "b", "abc");
            host.Joiners = rng.Next(5) != 0;
            if (rng.Next(4) == 0) { host.WordForming.Add('_'); host.WordForming.Add('-'); }
            return host;
        }

        [Fact]
        public void The_windowed_scanner_marks_exactly_what_a_pass_over_the_whole_paragraph_marked()
        {
            var rng = new Random(91);
            int words = 0;
            for (int round = 0; round < 1500; round++)
            {
                var p = RandomParagraph(rng);
                var host = RandomHost(rng);
                bool readOnly = rng.Next(5) == 0;
                int limit = rng.Next(4) == 0 ? rng.Next(0, p.Length + 1) : int.MaxValue;
                var want = ReferenceSpellScanner.Check(p, host, null, readOnly, limit);
                var got = SpellScanner.Check(p, host, null, readOnly, limit);
                Assert.True(want.SequenceEqual(got), "round " + round + " text [" + p.Text + "] want " + string.Join(",", want) + " got " + string.Join(",", got));
                words += got.Count;
            }
            Assert.True(words > 500);
        }

        [Fact]
        public void The_suggestions_at_every_offset_are_those_of_the_whole_pass()
        {
            var rng = new Random(92);
            int found = 0;
            for (int round = 0; round < 400; round++)
            {
                var p = RandomParagraph(rng);
                var host = RandomHost(rng);
                bool readOnly = rng.Next(6) == 0;
                int limit = rng.Next(5) == 0 ? rng.Next(0, p.Length + 1) : int.MaxValue;
                for (int offset = -1; offset <= p.Length + 1; offset++)
                {
                    var want = ReferenceSpellScanner.At(p, host, offset, null, readOnly, limit);
                    var got = SpellScanner.SuggestionsAt(p, 0, offset, host, null, readOnly, limit);
                    if (want == null) { Assert.True(got == null, "round " + round + " text [" + p.Text + "] offset " + offset + " expected none, got " + got?.Word); continue; }
                    Assert.True(got != null, "round " + round + " text [" + p.Text + "] offset " + offset + " expected " + want.Value.Word);
                    Assert.Equal(want.Value.Start, got!.Start.Offset);
                    Assert.Equal(want.Value.End, got.End.Offset);
                    Assert.Equal(want.Value.Problem, got.Problem);
                    Assert.Equal(want.Value.Word, got.Word);
                    found++;
                }
            }
            Assert.True(found > 300);
        }

        [Fact]
        public void Scanning_forward_from_any_word_start_the_window_finds_gives_the_rest_of_the_words()
        {
            var rng = new Random(93);
            for (int round = 0; round < 400; round++)
            {
                var p = RandomParagraph(rng);
                var host = RandomHost(rng);
                var scan = new SpellScanner.Window(p, host, null, false);
                var all = new List<(int, int)>();
                int pos = 0;
                while (scan.NextWord(pos, out int a, out int b)) { all.Add((a, b)); pos = b; }
                for (int from = 0; from <= p.Length; from++)
                {
                    int start = scan.WordStart(from);
                    var again = new List<(int, int)>();
                    pos = start;
                    while (scan.NextWord(pos, out int a, out int b)) { again.Add((a, b)); pos = b; }
                    // The words found from the aligned start are exactly the tail of all the words, starting at the word that holds or ends at the offset.
                    int skip = all.Count - again.Count;
                    Assert.True(skip >= 0 && all.Skip(skip).SequenceEqual(again), "round " + round + " text [" + p.Text + "] from " + from + " start " + start);
                    Assert.True(start <= from);
                }
            }
        }

        // ---- the work a call does is that of the words it looks at

        sealed class CountingHost : SpellCheckHostBase
        {
            public int WordFormingCalls, JoinsCalls;
            public readonly FakeChecker Checker = new FakeChecker();
            public override ISpellChecker? GetChecker(string ws) => Checker;
            public override bool IsWordForming(string ws, int codePoint) { WordFormingCalls++; return base.IsWordForming(ws, codePoint); }
            public override bool JoinsWords(string ws, int codePoint) { JoinsCalls++; return base.JoinsWords(ws, codePoint); }
        }

        [Fact]
        public void Asking_about_one_word_of_a_very_long_paragraph_asks_the_host_about_that_word_not_the_paragraph()
        {
            var text = string.Join(" ", Enumerable.Range(0, 5000).Select(i => "word" + (i % 10)));
            var p = TestDocs.Para("en", "en", text);
            var host = new CountingHost();
            int offset = text.Length / 2;
            var got = SpellScanner.SuggestionsAt(p, 0, offset, host);
            Assert.NotNull(got);
            Assert.True(host.WordFormingCalls < 100, host.WordFormingCalls + " word-forming questions for one word of " + text.Length + " characters");
            Assert.True(host.Checker.Asked.Count == 1);
        }

#if TEXTEDITING_COUNTERS
        [Fact]
        public void Asking_at_the_end_of_a_long_paragraph_in_many_runs_is_as_cheap_as_at_the_start()
        {
            var p = new Paragraph { DefaultWs = "en" };
            for (int i = 0; i < 3000; i++) p.Runs.Add(new Run { Ws = "en", Text = "ab " });
            var host = new CountingHost();
            _ = p.Text; _ = p.Length;                 // the first look at a paragraph builds its text and run starts once
            long before = p.Runs.ElementReads;
            Assert.NotNull(SpellScanner.SuggestionsAt(p, 0, p.Length - 2, host));
            Assert.True(p.Runs.ElementReads - before < 100, (p.Runs.ElementReads - before) + " runs read");
            Assert.True(host.WordFormingCalls < 50);
        }
#endif
    }
}
