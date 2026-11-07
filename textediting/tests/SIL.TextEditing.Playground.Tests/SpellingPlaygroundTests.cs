using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Xunit;

namespace SIL.TextEditing.Playground.Tests
{
    /// <summary>ADR 0020 through the playground: the host's dictionaries, squiggles drawn while the application is idle, the skip rules,
    /// and the choices a context menu would offer. Idle work is the real dispatcher's, run by pumping jobs: nothing waits on a clock.</summary>
    public class SpellingPlaygroundTests
    {
        static string[] Words(Session s, int paragraph, SpellingProblem? only = null) =>
            s.Field.SpellingMarks(paragraph).Where(m => only == null || m.Problem == only).Select(m => s.ParagraphText(paragraph).Substring(m.Start, m.Length)).ToArray();

        [Fact]
        public void The_spelling_scenario_marks_misspelled_words_in_view_once_the_field_has_been_idle() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Spelling))
            {
                Headless.Pump();
                Assert.True(s.Field.IsSpellingComplete);
                Assert.Equal(new[] { "jumpd", "teh" }, Words(s, 0));
                Assert.Empty(Words(s, 5));   // Arabic has no dictionary
                Assert.Contains("spell checking: on", s.Log);
            }
        });

        [Fact]
        public void Apostrophes_hyphens_and_numbers_follow_the_word_rules_and_a_glued_word_in_two_writing_systems_is_blue() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Spelling))
            {
                Headless.Pump();
                Assert.Equal(new[] { "abc123" }, Words(s, 1));   // don't, well, known are words; 2026 is only digits
                Assert.Equal(new[] { "Gluedmots" }, Words(s, 2, SpellingProblem.Mixed));
                Assert.Equal(new[] { "exmple" }, Words(s, 2, SpellingProblem.Misspelled));
            }
        });

        [Fact]
        public void Do_not_check_and_read_only_text_are_skipped_forced_text_is_checked_and_an_object_in_a_word_is_its_own_problem() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Spelling))
            {
                Headless.Pump();
                Assert.Equal(new[] { "wrongg", "wrongg" }, Words(s, 3));   // the plain one and the forced one; not zzzqq, not the locked one
                var forced = s.ParagraphText(3).LastIndexOf("wrongg", StringComparison.Ordinal);
                Assert.Equal(forced, s.Field.SpellingMarks(3).Last().Start);
                Assert.Equal(new[] { SpellingProblem.Embedded }, s.Field.SpellingMarks(4).Select(m => m.Problem));
            }
        });

        [Fact]
        public void Typing_keeps_the_squiggles_outside_the_edit_and_idle_time_brings_back_the_right_ones() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Spelling))
            {
                Headless.Pump();
                s.Caret(6, s.ParagraphText(6).Length);
                s.Type("teh ");
                Assert.Equal(new[] { "jumpd", "teh" }, Words(s, 0));   // other paragraphs keep theirs
                Headless.Pump();
                Assert.Equal(new[] { "teh" }, Words(s, 6));
                s.Press(global::Avalonia.Input.Key.Back, times: 2);   // "te"
                Headless.Pump();
                Assert.Equal(new[] { "te" }, Words(s, 6));
            }
        });

        [Fact]
        public void The_checkbox_turns_checking_off_and_on() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Spelling))
            {
                Headless.Pump();
                var box = s.Find<CheckBox>(PlaygroundView.Names.SpellCheck);
                Assert.True(box.IsChecked);
                box.IsChecked = false;
                Headless.Pump();
                Assert.Empty(Words(s, 0));
                Assert.Null(s.Field.SpellChecking);
                box.IsChecked = true;
                Headless.Pump();
                Assert.Equal(new[] { "jumpd", "teh" }, Words(s, 0));
            }
        });

        [Fact]
        public void Other_scenarios_have_checking_off_until_it_is_ticked() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                Headless.Pump();
                Assert.Null(s.Field.SpellChecking);
                Assert.Empty(Words(s, 0));
                s.Find<CheckBox>(PlaygroundView.Names.SpellCheck).IsChecked = true;
                Headless.Pump();
                Assert.NotEmpty(Words(s, 0));   // "means", "book" and the rest are not in the sample word list
            }
        });

        [Fact]
        public void The_context_menu_button_reports_the_word_why_it_is_marked_and_the_suggestions() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Spelling))
            {
                Headless.Pump();
                s.Caret(0, s.ParagraphText(0).IndexOf("jumpd", StringComparison.Ordinal) + 2);
                s.Click(PlaygroundView.Names.SpellShow);
                Assert.Contains("context menu: 'jumpd' (en, Misspelled): jumped", s.Log);
                Assert.Contains("Add to dictionary", s.Log);
                s.Caret(0, 1);
                s.Click(PlaygroundView.Names.SpellShow);
                Assert.Contains("context menu: no marked word at the caret", s.Log);
                s.Caret(2, s.ParagraphText(2).IndexOf("Glued", StringComparison.Ordinal) + 2);
                s.Click(PlaygroundView.Names.SpellShow);
                Assert.Contains("(fr, Mixed): No suggestions; (Add to dictionary disabled)", s.Log);
            }
        });

        [Fact]
        public void Using_the_first_suggestion_replaces_the_word_as_one_undo_step() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Spelling))
            {
                Headless.Pump();
                int at = s.ParagraphText(0).IndexOf("jumpd", StringComparison.Ordinal);
                s.Caret(0, at + 1);
                s.Click(PlaygroundView.Names.SpellReplace);
                Assert.Contains("The quick brown fox jumped over", s.ParagraphText(0));
                Headless.Pump();
                Assert.Equal(new[] { "teh" }, Words(s, 0));
                s.Click(PlaygroundView.Names.Undo);
                Assert.Contains("fox jumpd over", s.ParagraphText(0));
                Headless.Pump();
                Assert.Equal(new[] { "jumpd", "teh" }, Words(s, 0));
            }
        });

        [Fact]
        public void Add_to_dictionary_and_ignore_go_back_to_the_host_and_the_squiggle_goes() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Spelling))
            {
                Headless.Pump();
                s.Caret(0, s.ParagraphText(0).IndexOf("jumpd", StringComparison.Ordinal) + 1);
                s.Click(PlaygroundView.Names.SpellAdd);
                Assert.Contains("dictionary en: added 'jumpd'", s.Log);
                Headless.Pump();
                Assert.Equal(new[] { "teh" }, Words(s, 0));
                s.Caret(0, s.ParagraphText(0).IndexOf("teh", StringComparison.Ordinal) + 1);
                s.Click(PlaygroundView.Names.SpellIgnore);
                Assert.Contains("dictionary en: ignoring 'teh'", s.Log);
                Headless.Pump();
                Assert.Empty(Words(s, 0));
                s.Caret(2, s.ParagraphText(2).IndexOf("Glued", StringComparison.Ordinal) + 2);
                s.Click(PlaygroundView.Names.SpellAdd);
                Assert.Contains("is not a plain misspelling", s.Log);
            }
        });

        [Fact]
        public void A_long_document_checks_only_what_is_in_view_and_scrolling_checks_the_rest() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Long))
            {
                s.Find<CheckBox>(PlaygroundView.Names.SpellCheck).IsChecked = true;
                Headless.Pump();
                int count = s.Editor.Document.Paragraphs.Count;
                Assert.NotEmpty(Words(s, 0));
                Assert.Empty(Words(s, count - 1));
                Assert.False(s.Field.SpellingMarks(count - 1).Any());
                var scroll = s.Field.GetVisualAncestors().OfType<ScrollViewer>().First();
                scroll.Offset = new Vector(0, scroll.Extent.Height);
                Headless.Pump();
                Headless.Pump();
                Assert.True(s.Field.IsSpellingComplete);
                Assert.NotEmpty(Words(s, count - 1));
            }
        });

        [Fact]
        public void A_red_squiggle_is_drawn_under_the_misspelled_word_only_and_goes_when_the_word_changes() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Spelling))
            {
                Headless.Pump();
                int at = s.ParagraphText(0).IndexOf("jumpd", StringComparison.Ordinal);
                int good = s.ParagraphText(0).IndexOf("brown", StringComparison.Ordinal);
                Assert.True(RedBelowBaseline(s, 0, at, 5) > 3);
                Assert.Equal(0, RedBelowBaseline(s, 0, good, 5));
                s.Select(0, at, at + 5);
                s.Type("jumped");
                Assert.Equal(0, RedBelowBaseline(s, 0, at, 6));
            }
        });

        [Fact]
        public void A_mixed_word_is_drawn_with_a_blue_squiggle() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Spelling))
            {
                Headless.Pump();
                int at = s.ParagraphText(2).IndexOf("Glued", StringComparison.Ordinal);
                var shot = new DrawingParagraphTests.Shot(s);
                var pl = s.Field.ParagraphLayoutFor(2);
                var li = pl.Lines[pl.LineIndexOf(at)];
                var bounds = li.Line.GetTextBounds(at, 5);
                double x0 = bounds.Min(b => b.Rectangle.X) + li.X, x1 = bounds.Max(b => b.Rectangle.Right) + li.X;
                int baseline = (int)Math.Round(s.Field.ParagraphTop(2) + li.TextY + li.Line.Baseline);
                int blue = 0, red = 0;
                for (int x = (int)x0 + 1; x < (int)x1 - 1; x++)
                    for (int y = baseline; y < baseline + 8; y++)
                    {
                        var c = shot.At(x, y);
                        if (c.B > 200 && c.R < 90 && c.G < 90) blue++;
                        if (c.R > 200 && c.G < 90 && c.B < 90) red++;
                    }
                Assert.True(blue > 3);
                Assert.Equal(0, red);
            }
        });

        [Fact]
        public void The_insert_missing_spaces_button_separates_a_mixed_word_and_each_part_keeps_its_writing_system() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Spelling))
            {
                Headless.Pump();
                s.Caret(2, s.ParagraphText(2).IndexOf("Glued", StringComparison.Ordinal) + 2);
                s.Click(PlaygroundView.Names.SpellSpaces);
                Assert.Contains("insert spaces: 'Gluedmots': done", s.Log);
                Assert.Contains("A word in Glued mots is blue", s.ParagraphText(2));
                var runs = s.Editor.Document.Paragraphs[2].Runs;
                Assert.Equal("fr", runs.Single(r => r.Text == "mots").Ws);
                Headless.Pump();
                Assert.Empty(Words(s, 2, SpellingProblem.Mixed));
                s.Click(PlaygroundView.Names.Undo);
                Assert.Contains("Gluedmots", s.ParagraphText(2));
            }
        });

        [Fact]
        public void The_whole_word_buttons_make_a_mixed_word_one_writing_system_and_leave_a_word_in_one_alone() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Spelling))
            {
                Headless.Pump();
                s.Caret(2, s.ParagraphText(2).IndexOf("Glued", StringComparison.Ordinal) + 2);
                s.Click(PlaygroundView.Names.SpellWholeLast);
                Assert.Contains("whole word: 'Gluedmots' -> fr: done", s.Log);
                Assert.Contains("A word in Gluedmots is blue", s.ParagraphText(2));   // the text is the same; only the writing system changed
                Assert.Equal("fr", s.Editor.Document.Paragraphs[2].RunAfter(s.ParagraphText(2).IndexOf("Glued", StringComparison.Ordinal) + 2, out _)!.Ws);
                Headless.Pump();
                Assert.Empty(Words(s, 2, SpellingProblem.Mixed));
                s.Click(PlaygroundView.Names.Undo);
                Assert.Equal("en", s.Editor.Document.Paragraphs[2].RunAfter(s.ParagraphText(2).IndexOf("Glued", StringComparison.Ordinal) + 2, out _)!.Ws);
                s.Click(PlaygroundView.Names.SpellWholeFirst);
                Assert.Contains("whole word: 'Gluedmots' -> en: done", s.Log);
                Assert.Equal("en", s.Editor.Document.Paragraphs[2].RunAfter(s.ParagraphText(2).IndexOf("mots", StringComparison.Ordinal) + 1, out _)!.Ws);
            }
        });

        [Fact]
        public void The_insert_missing_spaces_button_keeps_the_object_of_an_embedded_word() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Spelling))
            {
                Headless.Pump();
                s.Caret(4, s.ParagraphText(4).IndexOf("abc", StringComparison.Ordinal) + 1);
                s.Click(PlaygroundView.Names.SpellSpaces);
                Assert.Contains("abc ￼ def", s.ParagraphText(4));
                Assert.Single(s.Editor.Document.Paragraphs[4].Runs, r => r.IsObject);
            }
        });

        [Fact]
        public void A_squiggle_in_a_right_to_left_paragraph_is_under_the_misspelled_word_and_nowhere_else() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Spelling))
            {
                Headless.Pump();
                var host = new SIL.TextEditing.Tests.FakeSpellHost().With("ar", "نص", "عربي", "بلا", "هنا");
                host.Checkers.Remove("en");   // only Arabic is checked, so only its squiggle can be red
                s.Field.SpellChecking = host;
                Headless.Pump();
                string text = s.ParagraphText(5);
                int at = text.IndexOf("قاموس", StringComparison.Ordinal);
                Assert.Equal(new[] { "قاموس" }, Words(s, 5));
                var shot = new DrawingParagraphTests.Shot(s);
                var pl = s.Field.ParagraphLayoutFor(5);
                double top = s.Field.ParagraphTop(5);
                var wrong = pl.SelectionRects(at, at + 5);
                int inside = 0, outside = 0;
                for (int x = 0; x < shot.Width; x++)
                    for (int y = (int)top; y < (int)(top + pl.Height); y++)
                    {
                        var c = shot.At(x, y);
                        if (!(c.R > 200 && c.G < 90 && c.B < 90)) continue;
                        if (wrong.Any(r => x >= r.X - 1 && x <= r.Right + 1)) inside++; else outside++;
                    }
                Assert.True(inside > 5);
                Assert.Equal(0, outside);
            }
        });

        static int RedBelowBaseline(Session s, int paragraph, int at, int length)
        {
            var shot = new DrawingParagraphTests.Shot(s);
            var pl = s.Field.ParagraphLayoutFor(paragraph);
            var li = pl.Lines[pl.LineIndexOf(at)];
            var bounds = li.Line.GetTextBounds(at, length);
            double x0 = bounds.Min(b => b.Rectangle.X) + li.X, x1 = bounds.Max(b => b.Rectangle.Right) + li.X;
            int baseline = (int)Math.Round(s.Field.ParagraphTop(paragraph) + li.TextY + li.Line.Baseline);
            int red = 0;
            for (int x = (int)x0 + 1; x < (int)x1 - 1; x++)
                for (int y = baseline; y < baseline + 8; y++)
                {
                    var c = shot.At(x, y);
                    if (c.R > 200 && c.G < 90 && c.B < 90) red++;
                }
            return red;
        }
    }
}
