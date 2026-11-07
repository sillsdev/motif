using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Xunit;

namespace SIL.TextEditing.Playground.Tests
{
    /// <summary>ADR 0021 and 0025: the playground's find bar plays the host's dialog; the field searches, selects, highlights and
    /// replaces (as single undo steps).</summary>
    public class FindReplacePlaygroundTests
    {
        static void Find(Session s, string text)
        {
            s.Find<TextBox>(PlaygroundView.Names.FindText).Text = text;
            Headless.Pump();
        }

        static void Replacement(Session s, string text)
        {
            s.Find<TextBox>(PlaygroundView.Names.ReplaceText).Text = text;
            Headless.Pump();
        }

        static void Check(Session s, string name, bool on)
        {
            s.Find<CheckBox>(name).IsChecked = on;
            Headless.Pump();
        }

        static string Selected(Session s)
        {
            var r = s.Editor.SelectionRange;
            return s.ParagraphText(r.Start.Paragraph).Substring(r.Start.Offset, r.End.Offset - r.Start.Offset);
        }

        /// <summary>Presses Find Next until the search wraps, collecting what each press selected.</summary>
        static List<string> AllFound(Session s)
        {
            var seen = new List<TextRange>();
            var texts = new List<string>();
            s.Caret(0, 0);
            for (int i = 0; i < 40; i++)
            {
                s.Click(PlaygroundView.Names.FindNext);
                var r = s.Editor.SelectionRange;
                if (r.IsEmpty || seen.Contains(r)) break;
                seen.Add(r);
                texts.Add(Selected(s));
            }
            return texts;
        }

        static string Status(Session s) => s.Find<TextBlock>(PlaygroundView.Names.FindStatus).Text ?? string.Empty;

        [Fact]
        public void Find_Next_selects_each_match_in_turn_wraps_round_and_the_status_says_so() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.FindReplace))
            {
                Find(s, "cat");
                s.Caret(0, 0);
                s.Click(PlaygroundView.Names.FindNext);
                Assert.Equal(new TextRange(0, 4, 7), s.Editor.SelectionRange);
                Assert.Equal(s.Editor.SelectionRange, s.Field.CurrentMatch);
                s.Click(PlaygroundView.Names.FindNext);
                Assert.Equal("Cat", Selected(s));
                Assert.Contains("Found at", Status(s));
                s.Click(PlaygroundView.Names.FindPrevious);
                Assert.Equal(new TextRange(0, 4, 7), s.Editor.SelectionRange);
                s.Click(PlaygroundView.Names.FindPrevious);
                Assert.StartsWith("Wrapped.", Status(s));
                Assert.Equal(4, s.Editor.SelectionRange.Start.Paragraph);
                Find(s, "zebra");
                s.Click(PlaygroundView.Names.FindNext);
                Assert.Equal("Not found", Status(s));
            }
        });

        [Fact]
        public void The_options_decide_what_is_found_case_marks_whole_words_and_regular_expressions() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.FindReplace))
            {
                Find(s, "cat");
                var every = AllFound(s);
                Assert.Equal(new[] { "cat", "Cat", "CAT", "cat", "cat", "cat", "cat", "cat", "cat", "cat", "cat" }, every.ToArray());
                Check(s, PlaygroundView.Names.MatchCase, true);
                Assert.Equal(every.Count - 2, AllFound(s).Count);
                Check(s, PlaygroundView.Names.MatchCase, false);
                Check(s, PlaygroundView.Names.WholeWord, true);
                // "concat" and "category" no longer count; "cat-like" does.
                Assert.Equal(every.Count - 2, AllFound(s).Count);
                Check(s, PlaygroundView.Names.WholeWord, false);

                Find(s, "cafe");
                Assert.Single(AllFound(s));   // the bare word only: the accented ones carry a mark
                Check(s, PlaygroundView.Names.MatchDiacritics, false);
                Assert.Equal(4, AllFound(s).Count);
                Find(s, "caf" + (char)0x00E9);
                Check(s, PlaygroundView.Names.MatchDiacritics, true);
                Assert.Equal(3, AllFound(s).Count);   // composed, decomposed and (case is ignored) upper case, found whichever way it is typed
                Check(s, PlaygroundView.Names.MatchDiacritics, true);

                Check(s, PlaygroundView.Names.RegularExpression, true);
                Find(s, "c[aeiou]t");
                Assert.True(AllFound(s).Count >= 10);
                Find(s, "(");
                s.Click(PlaygroundView.Names.FindNext);
                Assert.StartsWith("Not a valid regular expression", Status(s));
            }
        });

        [Fact]
        public void The_current_match_and_all_matches_are_highlighted_and_the_highlight_goes_with_an_edit() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.FindReplace))
            {
                s.Field.FindHighlightBrush = Brushes.Lime;
                s.Field.CurrentMatchBrush = Brushes.Red;
                Find(s, "mat.");
                Check(s, PlaygroundView.Names.HighlightAll, true);
                s.Caret(0, 0);
                var rect = s.Field.ParagraphLayoutFor(0).SelectionRects(19, 23)[0];
                Assert.Equal(Colors.Lime, DrawingParagraphTests.Pixel(s, rect.X + 1, rect.Y + 1));
                Assert.Equal(Colors.White, DrawingParagraphTests.Pixel(s, rect.X - 3, rect.Y + 1));

                s.Click(PlaygroundView.Names.FindNext);
                Assert.Equal(new TextRange(0, 19, 23), s.Editor.SelectionRange);
                s.Caret(0, 0);   // the selection goes; the current match stays marked
                Assert.Equal(Colors.Red, DrawingParagraphTests.Pixel(s, rect.X + 1, rect.Y + 1));

                Check(s, PlaygroundView.Names.HighlightAll, false);
                s.Field.ClearFindHighlight();
                Headless.Pump();
                Assert.Equal(Colors.White, DrawingParagraphTests.Pixel(s, rect.X + 1, rect.Y + 1));
                Assert.Null(s.Field.CurrentMatch);

                Check(s, PlaygroundView.Names.HighlightAll, true);
                s.Click(PlaygroundView.Names.FindNext);
                s.Caret(0, 0);
                s.Editor.SetCaret(new TextPosition(0, 0));
                s.Type("XY");   // an edit before the match moves it: nothing stale is drawn where it was
                var moved = s.Field.ParagraphLayoutFor(0).SelectionRects(21, 25)[0];
                Assert.Equal(Colors.Lime, DrawingParagraphTests.Pixel(s, moved.X + 1, moved.Y + 1));
                Assert.Null(s.Field.CurrentMatch);
            }
        });

        [Fact]
        public void A_host_that_changes_the_document_directly_and_refreshes_the_field_leaves_no_stale_highlight_or_match() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.FindReplace))
            {
                s.Field.FindHighlightBrush = Brushes.Lime;
                s.Field.CurrentMatchBrush = Brushes.Red;
                Find(s, "cat");
                Check(s, PlaygroundView.Names.HighlightAll, true);
                s.Click(PlaygroundView.Names.FindNext);
                Assert.NotNull(s.Field.CurrentMatch);

                var doc = s.Editor.Document;
                doc.Paragraphs.Clear();
                doc.Paragraphs.Add(new Paragraph { DefaultWs = "en", Runs = { new Run { Ws = "en", Text = "x" } } });
                s.Editor.Anchor = s.Editor.Caret = new TextPosition(0, 0);
                s.Field.RefreshFromDocument();
                Headless.Pump();

                var shot = new DrawingParagraphTests.Shot(s);   // drawing the old ranges against the new text would throw
                Assert.Null(s.Field.CurrentMatch);
                for (int y = 0; y < shot.Height; y += 3)
                    for (int x = 0; x < shot.Width; x += 3)
                        Assert.NotEqual(Colors.Lime, shot.At(x, y));
            }
        });

        [Fact]
        public void A_match_in_read_only_text_is_selected_exactly_and_Replace_leaves_it_alone() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.FindReplace))
            {
                Find(s, "Label");
                Replacement(s, "Tag");
                s.Click(PlaygroundView.Names.FindNext);
                Assert.Equal(new TextRange(4, 0, 5), s.Editor.SelectionRange);
                s.Click(PlaygroundView.Names.Replace);
                Assert.StartsWith("Label: cat", s.ParagraphText(4));
            }
        });

        [Fact]
        public void Replace_replaces_the_selected_match_in_one_undo_step_then_finds_the_next() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.FindReplace))
            {
                Find(s, "mat");
                Replacement(s, "rug");
                s.Caret(0, 0);
                s.Click(PlaygroundView.Names.Replace);   // nothing is selected yet: it selects the first match
                Assert.Equal("mat", Selected(s));
                int steps = s.View.Undo.DoneSteps;
                s.Click(PlaygroundView.Names.Replace);
                Assert.Contains("on the rug.", s.ParagraphText(0));
                Assert.Equal(steps + 1, s.View.Undo.DoneSteps);
                s.Click(PlaygroundView.Names.Undo);
                Assert.Contains("on the mat.", s.ParagraphText(0));
            }
        });

        [Fact]
        public void Replace_All_is_one_undo_step_skips_read_only_text_and_does_not_search_what_it_put_in() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.FindReplace))
            {
                Find(s, "cat");
                Replacement(s, "catalog");
                int steps = s.View.Undo.DoneSteps;
                s.Click(PlaygroundView.Names.ReplaceAll);
                Assert.StartsWith("Replaced 10 matches", Status(s));   // eleven matches, one of them in the read-only label
                Assert.Contains("Label: cat and an editable catalog.", s.ParagraphText(4));
                Assert.Contains("The catalog sat on the mat. catalog, catalog, concatalog and catalogegory; a catalog-like catalog.", s.ParagraphText(0));
                Assert.Equal(steps + 1, s.View.Undo.DoneSteps);
                Assert.Null(s.Field.CurrentMatch);
                s.Click(PlaygroundView.Names.Undo);
                Assert.Contains("The cat sat on the mat. Cat,", s.ParagraphText(0));
                Assert.Contains("and an editable cat.", s.ParagraphText(4));
            }
        });

        [Fact]
        public void Replace_All_gives_the_new_text_the_writing_system_of_the_text_it_replaced() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.FindReplace))
            {
                Find(s, "bonjour");
                Replacement(s, "salut");
                s.Click(PlaygroundView.Names.ReplaceAll);
                var runs = s.Editor.Document.Paragraphs[2].Runs;
                Assert.Equal(new[] { "Say ", "salut", " and then ", "salut", " again, or hello." }, runs.Select(r => r.Text).ToArray());
                Assert.Equal("fr", runs[1].Ws);
                Assert.Equal("en", runs[0].Ws);
            }
        });
    }
}
