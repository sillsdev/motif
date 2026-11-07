using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Xunit;

namespace SIL.TextEditing.Playground.Tests
{
    /// <summary>Issue 102 through the playground: the "Hyphen is a letter" box gives every field a host word rule (<c>RichTextField.WordRules</c>), and the
    /// double-click, Ctrl+Right and the spell checker's marks all take it.</summary>
    public class WordRulePlaygroundTests
    {
        static string Selected(Session s) => string.Join("\n", s.Editor.HasSelection ? s.Editor.GetFragment().Select(p => p.Text) : Enumerable.Empty<string>());

        static string[] Words(Session s, int paragraph) =>
            s.Field.SpellingMarks(paragraph).Select(m => s.ParagraphText(paragraph).Substring(m.Start, m.Length)).ToArray();

        [Fact]
        public void Ticking_hyphen_is_a_letter_makes_a_hyphenated_word_one_word_for_the_double_click_Ctrl_Right_and_the_squiggles() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Spelling))
            {
                Headless.Pump();
                int at = s.ParagraphText(1).IndexOf("well-known", StringComparison.Ordinal);
                s.MouseClick(s.PointAt(1, at + 7), 2);
                Assert.Equal("known", Selected(s));                         // the default rule: the hyphen ends a word
                s.Caret(1, at);
                s.Press(Key.Right, KeyModifiers.Control);
                Assert.Equal(at + 4, s.Editor.Caret.Offset);                // to the hyphen
                Assert.Equal(new[] { "abc123" }, Words(s, 1));              // "well" and "known" are in the word list

                var box = s.Find<CheckBox>(PlaygroundView.Names.HyphenLetter);
                box.IsChecked = true;
                Headless.Pump();
                Assert.Contains("word rule: the hyphen is a letter", s.Log);
                s.MouseClick(s.PointAt(1, at + 2), 2);
                Assert.Equal("well-known", Selected(s));
                s.Caret(1, at);
                s.Press(Key.Right, KeyModifiers.Control);
                Assert.Equal(at + "well-known ".Length, s.Editor.Caret.Offset);   // past the whole word and its space
                Assert.Equal(new[] { "well-known", "abc123" }, Words(s, 1));       // one word the dictionary does not have

                box.IsChecked = false;
                Headless.Pump();
                s.MouseClick(s.PointAt(1, at + 7), 2);
                Assert.Equal("known", Selected(s));
                Assert.Equal(new[] { "abc123" }, Words(s, 1));
            }
        });
    }
}
