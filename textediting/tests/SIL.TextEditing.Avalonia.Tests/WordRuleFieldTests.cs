using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using SIL.TextEditing.Tests;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>Issue 102: the field gives the host's word rule to the double-click, Ctrl+Left and Right, the context request and the marks alike, whether
    /// the host sets it as <see cref="RichTextField.WordRules"/> or only through its spell checker.</summary>
    public class WordRuleFieldTests
    {
        const string Text = "foo-bar baz_qux end";

        /// <summary>A hyphen is a letter and an underscore is not.</summary>
        sealed class HyphenRules : IWordRules
        {
            public int Asked;
            public bool IsWordForming(string ws, int codePoint) { Asked++; return codePoint == '-' || SpellScanner.DefaultIsWordForming(codePoint); }
            public bool JoinsWords(string ws, int codePoint) => SpellScanner.DefaultJoinsWords(codePoint);
        }

        /// <summary>An underscore is a letter and a hyphen is not.</summary>
        sealed class UnderscoreHost : SpellCheckHostBase
        {
            public override ISpellChecker? GetChecker(string ws) => new FakeBareChecker();
            public override bool IsWordForming(string ws, int codePoint) => codePoint == '_' || SpellScanner.DefaultIsWordForming(codePoint);
        }

        static Rect CharRect(RichTextField f, int index)
        {
            var pl = f.ParagraphLayoutFor(0);
            var li = pl.Lines[pl.LineIndexOf(index)];
            double x0 = li.Line.GetDistanceFromCharacterHit(new CharacterHit(index)), x1 = li.Line.GetDistanceFromCharacterHit(new CharacterHit(index, 1));
            return new Rect(Math.Min(x0, x1), li.Y, Math.Abs(x1 - x0), li.Line.Height);
        }

        static Point Center(RichTextField f, int index) { var r = CharRect(f, index); return new Point(r.X + r.Width / 2, r.Y + r.Height / 2); }

        static string DoubleClick(Window w, RichTextField f, int index)
        {
            // Avalonia counts clicks by the real time between them (the system's double-click interval), so a busy machine can make the two presses
            // of one attempt look like two single clicks, which leave no selection. Such an attempt is simply made again (a failed attempt changes
            // nothing but the caret); a double click that is wrong in another way still fails the test.
            for (int attempt = 0; ; attempt++)
            {
                var far = new Point(w.Width - 3, w.Height - 3);
                w.MouseDown(far, MouseButton.Left);                // a click somewhere else (and a moment later in effect) ends any click count in progress
                w.MouseUp(far, MouseButton.Left);
                var p = Center(f, index);
                for (int i = 0; i < 2; i++) { w.MouseDown(p, MouseButton.Left); w.MouseUp(p, MouseButton.Left); }
                Headless.Pump();
                if (f.Editor.HasSelection || attempt >= 4)
                    return string.Join("\n", f.Editor.HasSelection ? f.Editor.GetFragment().Select(x => x.Text) : Enumerable.Empty<string>());
                System.Threading.Thread.Sleep(600);                // past the double-click interval, so the next attempt starts a fresh count
            }
        }

        static (Window, RichTextField) Open(string text = Text)
        {
            var (w, f) = Fixtures.InWindow(OneParagraph(text), 400);
            Headless.Pump();
            return (w, f);
        }

        [Fact]
        public void A_field_with_no_host_rule_uses_the_defaults_for_the_double_click_and_the_word_moves() => Headless.Run(() =>
        {
            var (w, f) = Open();
            Assert.Equal("bar", DoubleClick(w, f, 5));
            Assert.Equal("baz", DoubleClick(w, f, 9));
            f.Editor.SetCaret(new TextPosition(0, 0));
            f.MoveByWord(false, false);
            Assert.Equal(3, f.Editor.Caret.Offset);
            w.Close();
        });

        [Fact]
        public void A_word_rule_the_host_sets_reaches_the_double_click_and_Ctrl_Right_and_Left_without_any_spell_checking() => Headless.Run(() =>
        {
            var (w, f) = Open();
            f.WordRules = new HyphenRules();
            Assert.Equal("foo-bar", DoubleClick(w, f, 5));
            Assert.Equal("baz", DoubleClick(w, f, 9));
            f.Editor.SetCaret(new TextPosition(0, 0));
            f.MoveByWord(false, false);
            Assert.Equal(8, f.Editor.Caret.Offset);                       // past "foo-bar" and its space
            f.MoveByWord(true, false);
            Assert.Equal(0, f.Editor.Caret.Offset);
            Assert.Equal("foo-bar", f.Editor.DescribeContext(new TextPosition(0, 5), false).Word);
            w.Close();
        });

        [Fact]
        public void The_rule_of_the_spell_checker_is_the_word_rule_when_the_host_set_none_and_the_marks_follow_it_too() => Headless.Run(() =>
        {
            var (w, f) = Open();
            f.SpellIdlePost = a => { };
            f.SpellChecking = new UnderscoreHost();
            Assert.Equal("baz_qux", DoubleClick(w, f, 9));
            Assert.Equal("bar", DoubleClick(w, f, 5));
            f.Editor.SetCaret(new TextPosition(0, 8));
            f.MoveByWord(false, false);
            Assert.Equal(16, f.Editor.Caret.Offset);                      // "baz_qux" is one word
            while (!f.CheckSpellingStep()) { }
            Assert.Equal(new[] { "foo", "bar", "baz_qux", "end" }, f.SpellingMarks(0).Select(m => Text.Substring(m.Start, m.Length)).ToArray());
            Assert.Equal("baz_qux", f.Editor.DescribeContext(new TextPosition(0, 10), false).Word);
            w.Close();
        });

        [Fact]
        public void A_rule_set_on_the_field_wins_over_the_spell_checkers_and_clearing_it_gives_the_spell_checkers_back() => Headless.Run(() =>
        {
            var (w, f) = Open();
            f.SpellIdlePost = a => { };
            f.SpellChecking = new UnderscoreHost();
            f.WordRules = new HyphenRules();
            Assert.Equal("foo-bar", DoubleClick(w, f, 5));
            Assert.Equal("baz", DoubleClick(w, f, 9));                    // the underscore is punctuation for the field's rule
            f.WordRules = null;
            Assert.Equal("baz_qux", DoubleClick(w, f, 9));
            f.SpellChecking = null;
            Assert.Equal("baz", DoubleClick(w, f, 9));                    // no host, no rule: the defaults
            f.WordRules = new HyphenRules();
            f.SpellChecking = new UnderscoreHost();
            Assert.Equal("foo-bar", DoubleClick(w, f, 5));                // a new spell checker does not take the field's own rule away
            w.Close();
        });

        [Fact]
        public void A_rule_set_on_the_field_is_the_spell_checkers_rule_too_so_the_marks_and_the_double_click_agree() => Headless.Run(() =>
        {
            var (w, f) = Open();
            f.SpellIdlePost = a => { };
            f.SpellChecking = new UnderscoreHost();
            while (!f.CheckSpellingStep()) { }
            Assert.Equal(new[] { "foo", "bar", "baz_qux", "end" }, f.SpellingMarks(0).Select(m => Text.Substring(m.Start, m.Length)).ToArray());
            f.WordRules = new HyphenRules();                               // starts checking over, with the field's rule
            while (!f.CheckSpellingStep()) { }
            Assert.Equal(new[] { "foo-bar", "baz", "qux", "end" }, f.SpellingMarks(0).Select(m => Text.Substring(m.Start, m.Length)).ToArray());
            Assert.Equal("foo-bar", DoubleClick(w, f, 5));
            f.WordRules = null;
            while (!f.CheckSpellingStep()) { }
            Assert.Equal(new[] { "foo", "bar", "baz_qux", "end" }, f.SpellingMarks(0).Select(m => Text.Substring(m.Start, m.Length)).ToArray());
            w.Close();
        });

        [Fact]
        public void A_word_rule_that_throws_is_traced_once_and_the_defaults_answer_in_its_place() => Headless.Run(() =>
        {
            var (w, f) = Open();
            var traced = new List<string>();
            f.SpellTraceSink = traced.Add;
            f.WordRules = new ThrowingRules();
            Assert.Equal("bar", DoubleClick(w, f, 5));
            Assert.Equal("baz", DoubleClick(w, f, 9));
            Assert.Single(traced);
            Assert.Contains("boom", traced[0]);
            w.Close();
        });

        sealed class ThrowingRules : IWordRules
        {
            public bool IsWordForming(string ws, int codePoint) => throw new InvalidOperationException("boom");
            public bool JoinsWords(string ws, int codePoint) => throw new InvalidOperationException("boom");
        }

        [Fact]
        public void The_word_rule_is_asked_about_the_characters_near_the_caret_not_the_whole_paragraph_for_Ctrl_arrows() => Headless.Run(() =>
        {
            string text = string.Join(" ", Enumerable.Repeat("word", 2000));
            var f = Fixtures.Field(OneParagraph(text), 400);
            var rules = new HyphenRules();
            f.WordRules = rules;
            f.Editor.SetCaret(new TextPosition(0, 4000));
            rules.Asked = 0;
            f.MoveByWord(false, false);
            Assert.True(rules.Asked < 50, rules.Asked + " questions for one Ctrl+Right in " + text.Length + " characters");
            rules.Asked = 0;
            f.MoveByWord(true, false);
            Assert.True(rules.Asked < 50, rules.Asked + " questions for one Ctrl+Left in " + text.Length + " characters");
        });

        sealed class CodeStyle : IStyleResolver
        {
            public PropertyBag? Resolve(string styleName, string ws) =>
                styleName == "Code" ? new PropertyBag().SetInt(TextPropertyNumbers.SpellCheck, TextPropertyNumbers.SpellCheckDoNot) : null;
        }

        [Fact]
        public void A_double_click_stops_at_a_change_of_writing_system_and_of_a_style_that_changes_spell_checking_and_the_drag_goes_by_the_same_words() => Headless.Run(() =>
        {
            var p = Para("en", "en", "ab", "fr", "cd", "fr", "ef gh");
            p.Runs[2].Props.SetStr(TextPropertyNumbers.NamedStyle, "Code");
            var (w, f) = Fixtures.InWindow(Doc(p), 400);
            f.StyleResolver = new CodeStyle();
            Headless.Pump();
            Assert.Equal("ab", DoubleClick(w, f, 1));
            Assert.Equal("cd", DoubleClick(w, f, 3));
            Assert.Equal("ef", DoubleClick(w, f, 5));
            Assert.Equal("gh", DoubleClick(w, f, 8));
            w.Close();
        });
    }
}
