using System.Linq;
using Avalonia;
using Avalonia.Input;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>The hard line break (U+2028, Shift+Enter) ends a line for good: the text after it starts a new line, the break
    /// itself is not drawn, and a caret before it sits at the end of its line, not at the start of the next.</summary>
    public class HardLineBreakLayoutTests
    {
        static readonly string Break = DocumentEditor.LineSeparator.ToString();
        static RichTextField Field(string text, int caret, bool rtl = false, System.Action<Paragraph>? shape = null)
        {
            var p = Para(rtl ? "ar" : "en", rtl ? "ar" : "en", text);
            p.Rtl = rtl;
            shape?.Invoke(p);
            var field = Fixtures.Field(Doc(p), 520);
            field.Editor.SetCaret(new TextPosition(0, caret));
            return field;
        }

        static (double X, double Y) Caret(RichTextField field) { var r = field.CaretRect(); return (r.X, r.Y); }

        [Fact]
        public void Text_after_the_break_starts_a_new_line_and_the_break_is_not_drawn() => Headless.Run(() =>
        {
            var field = Field("ab" + Break + "cd", 0);
            var pl = field.ParagraphLayoutFor(0);
            Assert.Equal(2, pl.Lines.Count);
            Assert.Equal(3, pl.Lines[1].Start);
            Assert.Equal(2, pl.TextEnd(pl.Lines[0]));   // the line's last caret place is before the break
            var plain = Field("ab", 2).ParagraphLayoutFor(0);
            Assert.Equal(plain.CaretRect(2).X, pl.CaretRect(2).X, 3);   // the break has no width
            Assert.Equal(pl.Lines[0].TextY, pl.CaretRect(2).Y, 3);
            Assert.Equal(pl.Lines[1].TextY, pl.CaretRect(3).Y, 3);
            Assert.Equal(plain.CaretRect(0).X, pl.CaretRect(3).X, 3);
        });

        [Fact]
        public void Shift_Enter_at_the_end_of_a_paragraph_puts_the_caret_on_a_new_empty_line() => Headless.Run(() =>
        {
            var field = Field("ab", 2);
            var before = Caret(field);
            Assert.True(field.HandleKey(Key.Enter, KeyModifiers.Shift));
            Assert.Equal("ab" + Break, field.Editor.Document.Paragraphs[0].Text);
            Assert.Equal(new TextPosition(0, 3), field.Editor.Caret);
            Fixtures.Layout(field, 520);
            var pl = field.ParagraphLayoutFor(0);
            Assert.Equal(2, pl.Lines.Count);
            var after = Caret(field);
            Assert.True(after.Y > before.Y + 5, "the caret moved down a line");
            Assert.Equal(pl.Lines[1].TextY, after.Y, 3);
            Assert.True(after.X < before.X, "and to the start of the line");
            Assert.True(field.DesiredSize.Height >= pl.Lines[1].Y + pl.Lines[1].Height);   // the empty line takes room
        });

        [Fact]
        public void Shift_Enter_at_the_start_of_a_paragraph_leaves_an_empty_first_line() => Headless.Run(() =>
        {
            var field = Field("ab", 0);
            field.HandleKey(Key.Enter, KeyModifiers.Shift);
            Fixtures.Layout(field, 520);
            var pl = field.ParagraphLayoutFor(0);
            Assert.Equal(2, pl.Lines.Count);
            Assert.Equal(new TextPosition(0, 1), field.Editor.Caret);
            Assert.Equal(pl.Lines[1].TextY, Caret(field).Y, 3);
            field.Press(Key.Left);
            Assert.Equal(pl.Lines[0].TextY, Caret(field).Y, 3);   // before the break, on the empty first line
        });

        [Fact]
        public void End_on_the_line_before_the_break_puts_the_caret_before_it() => Headless.Run(() =>
        {
            var field = Field("ab" + Break + "cd", 1);
            var pl = field.ParagraphLayoutFor(0);
            field.Press(Key.End);
            Assert.Equal(new TextPosition(0, 2), field.Editor.Caret);
            Assert.Equal(pl.Lines[0].TextY, Caret(field).Y, 3);
            Assert.Equal(pl.CaretRect(2).X, Caret(field).X, 3);
            field.Press(Key.Home);
            Assert.Equal(new TextPosition(0, 0), field.Editor.Caret);
            field.Editor.SetCaret(new TextPosition(0, 4));
            field.Press(Key.Home);
            Assert.Equal(new TextPosition(0, 3), field.Editor.Caret);
            Assert.Equal(pl.Lines[1].TextY, Caret(field).Y, 3);
            field.Press(Key.End);
            Assert.Equal(new TextPosition(0, 5), field.Editor.Caret);
        });

        [Fact]
        public void Right_and_left_cross_the_break_one_position_at_a_time_and_draw_the_caret_on_the_right_line() => Headless.Run(() =>
        {
            var field = Field("ab" + Break + "cd", 1);
            var pl = field.ParagraphLayoutFor(0);
            field.Press(Key.Right);
            Assert.Equal(new TextPosition(0, 2), field.Editor.Caret);
            Assert.Equal(pl.Lines[0].TextY, Caret(field).Y, 3);
            double endX = Caret(field).X;
            field.Press(Key.Right);
            Assert.Equal(new TextPosition(0, 3), field.Editor.Caret);
            Assert.Equal(pl.Lines[1].TextY, Caret(field).Y, 3);
            Assert.Equal(pl.CaretRect(3).X, Caret(field).X, 3);
            field.Press(Key.Right);
            Assert.Equal(new TextPosition(0, 4), field.Editor.Caret);

            field.Editor.SetCaret(new TextPosition(0, 4));
            field.Press(Key.Left);
            Assert.Equal(new TextPosition(0, 3), field.Editor.Caret);
            Assert.Equal(pl.Lines[1].TextY, Caret(field).Y, 3);
            field.Press(Key.Left);
            Assert.Equal(new TextPosition(0, 2), field.Editor.Caret);
            Assert.Equal(pl.Lines[0].TextY, Caret(field).Y, 3);
            Assert.Equal(endX, Caret(field).X, 3);
            field.Press(Key.Left);
            Assert.Equal(new TextPosition(0, 1), field.Editor.Caret);
        });

        [Fact]
        public void Up_and_down_move_between_the_lines_either_side_of_the_break() => Headless.Run(() =>
        {
            var field = Field("abcd" + Break + "efgh", 2);
            var pl = field.ParagraphLayoutFor(0);
            field.Press(Key.Down);
            Assert.Equal(new TextPosition(0, 7), field.Editor.Caret);   // same x on the line after the break
            Assert.Equal(pl.Lines[1].TextY, Caret(field).Y, 3);
            field.Press(Key.Up);
            Assert.Equal(new TextPosition(0, 2), field.Editor.Caret);
            Assert.Equal(pl.Lines[0].TextY, Caret(field).Y, 3);

            field.Editor.SetCaret(new TextPosition(0, 4));   // end of the first line
            field.Press(Key.Down);
            Assert.Equal(pl.Lines[1].TextY, Caret(field).Y, 3);
            Assert.True(field.Editor.Caret.Offset >= 5);
            field.Editor.SetCaret(new TextPosition(0, 9));   // end of the second line
            field.Press(Key.Up);
            Assert.Equal(new TextPosition(0, 4), field.Editor.Caret);   // before the break, not after it
            Assert.Equal(pl.Lines[0].TextY, Caret(field).Y, 3);
        });

        [Fact]
        public void Down_from_the_line_before_a_trailing_break_reaches_the_empty_last_line_and_up_returns() => Headless.Run(() =>
        {
            var field = Field("abcd" + Break, 2);
            var pl = field.ParagraphLayoutFor(0);
            field.Press(Key.Down);
            Assert.Equal(new TextPosition(0, 5), field.Editor.Caret);
            Assert.Equal(pl.Lines[1].TextY, Caret(field).Y, 3);
            field.Press(Key.Up);
            Assert.Equal(pl.Lines[0].TextY, Caret(field).Y, 3);
            Assert.True(field.Editor.Caret.Offset <= 4);
        });

        [Fact]
        public void A_click_beside_the_line_before_the_break_puts_the_caret_before_it() => Headless.Run(() =>
        {
            var field = Field("ab" + Break + "cd", 0);
            var pl = field.ParagraphLayoutFor(0);
            Assert.Equal(2, pl.HitTest(new Point(400, pl.Lines[0].Y + 2)));
            Assert.Equal(5, pl.HitTest(new Point(400, pl.Lines[1].Y + 2)));
            Assert.Equal(3, pl.HitTest(new Point(0, pl.Lines[1].Y + 2)));
        });

        [Fact]
        public void A_click_on_the_empty_last_line_puts_the_caret_after_the_trailing_break() => Headless.Run(() =>
        {
            var field = Field("ab" + Break, 0);
            var pl = field.ParagraphLayoutFor(0);
            var at = pl.Lines[1].Y + 2;
            Assert.Equal(3, pl.HitTest(new Point(100, at)));
            var located = field.PointToPosition(new Point(100, field.ParagraphTop(0) + at));
            Assert.Equal(new TextPosition(0, 3), located);
        });

        [Fact]
        public void The_selection_across_a_break_covers_the_text_either_side_and_nothing_for_the_break() => Headless.Run(() =>
        {
            var field = Field("ab" + Break + "cd", 0);
            var pl = field.ParagraphLayoutFor(0);
            var rects = pl.SelectionRects(0, 5);
            Assert.Equal(2, rects.Count);
            Assert.Equal(pl.Lines[0].TextY, rects[0].Y, 3);
            Assert.Equal(pl.Lines[1].TextY, rects[1].Y, 3);
        });

        [Fact]
        public void A_right_to_left_paragraph_breaks_and_moves_across_the_break_the_same_way() => Headless.Run(() =>
        {
            var field = Field("عر" + Break + "بي", 1, rtl: true);
            var pl = field.ParagraphLayoutFor(0);
            Assert.Equal(2, pl.Lines.Count);
            field.Press(Key.End);
            Assert.Equal(new TextPosition(0, 2), field.Editor.Caret);
            Assert.Equal(pl.Lines[0].TextY, Caret(field).Y, 3);
            field.Press(Key.Left);   // forward in a right-to-left paragraph
            Assert.Equal(new TextPosition(0, 3), field.Editor.Caret);
            Assert.Equal(pl.Lines[1].TextY, Caret(field).Y, 3);
            field.Press(Key.Right);
            Assert.Equal(new TextPosition(0, 2), field.Editor.Caret);
            Assert.Equal(pl.Lines[0].TextY, Caret(field).Y, 3);
        });

        [Fact]
        public void A_line_limit_still_counts_hard_lines_and_a_caret_never_rests_in_the_hidden_ones() => Headless.Run(() =>
        {
            var field = Field("ab" + Break + "cd" + Break + "ef", 0, shape: p => p.Props.SetInt(TextPropertyNumbers.MaxLines, 2));
            var pl = field.ParagraphLayoutFor(0);
            Assert.True(pl.Truncated);
            Assert.Equal(2, pl.Lines.Count);
            Assert.Equal(5, pl.VisibleEnd);   // before the second break
            field.Editor.SetCaret(new TextPosition(0, 8));
            Assert.Equal(new TextPosition(0, 5), field.Editor.Caret);
            Assert.Equal(pl.Lines[1].TextY, Caret(field).Y, 3);
            field.Press(Key.Right);
            Assert.Equal(new TextPosition(0, 5), field.Editor.Caret);
        });

        [Fact]
        public void A_bulleted_paragraph_with_a_break_labels_only_its_first_line() => Headless.Run(() =>
        {
            var field = Field("ab" + Break + "cd", 3, shape: p => p.Props.SetInt(TextPropertyNumbers.BulNumScheme, 101).SetInt(TextPropertyNumbers.LeadingIndent, 36000, 1).SetInt(TextPropertyNumbers.FirstIndent, -18000, 1));
            var pl = field.ParagraphLayoutFor(0);
            Assert.Equal(2, pl.Lines.Count);
            Assert.True(pl.LabelRect.Width > 0);
            Assert.Equal(pl.Lines[0].TextY, pl.LabelRect.Y, 3);
            Assert.Equal(pl.Lines[1].TextY, Caret(field).Y, 3);
        });

        static RichTextField Limited(string text, int caret, int maxLines) =>
            Field(text, caret, shape: p => p.Props.SetInt(TextPropertyNumbers.MaxLines, maxLines));

        static string Text(RichTextField field) => field.Editor.Document.Paragraphs[0].Text;

        [Theory]
        [InlineData("ab", 2, 1)]
        [InlineData("ab", 1, 1)]
        [InlineData("ab\u2028cd", 5, 2)]
        [InlineData("ab\u2028cd", 1, 2)]
        public void Shift_Enter_is_refused_when_the_new_line_would_be_hidden(string text, int caret, int maxLines) => Headless.Run(() =>
        {
            var field = Limited(text, caret, maxLines);
            field.HandleKey(Key.Enter, KeyModifiers.Shift);
            Assert.Equal(text, Text(field));
            Assert.Equal(new TextPosition(0, caret), field.Editor.Caret);
        });

        [Fact]
        public void Shift_Enter_still_works_when_the_new_line_fits_under_the_limit() => Headless.Run(() =>
        {
            var field = Limited("ab", 2, 2);
            field.HandleKey(Key.Enter, KeyModifiers.Shift);
            Assert.Equal("ab" + Break, Text(field));
            Assert.Equal(new TextPosition(0, 3), field.Editor.Caret);
            Assert.Equal(2, field.ParagraphLayoutFor(0).Lines.Count);
            Assert.False(field.ParagraphLayoutFor(0).Truncated);
        });

        [Theory]
        [InlineData(1, "ab")]
        [InlineData(2, "ab" + "\u2028" + "cd")]
        public void A_trailing_break_with_nothing_after_it_is_not_hidden_text_and_the_caret_stays_before_it(int maxLines, string text) => Headless.Run(() =>
        {
            var field = Limited(text + Break, 0, maxLines);
            var pl = field.ParagraphLayoutFor(0);
            Assert.False(pl.Truncated);
            Assert.Equal(text.Length, pl.VisibleEnd);
            field.Editor.SetCaret(new TextPosition(0, text.Length + 1));
            Assert.Equal(new TextPosition(0, text.Length), field.Editor.Caret);   // pulled back before the break
        });

        [Theory]
        [InlineData(1, "ab")]
        [InlineData(2, "ab" + "\u2028" + "cd")]
        public void The_keyboard_recovers_from_a_hidden_trailing_break(int maxLines, string text)
        {
            // Typing before the break, Delete (removes the break), and Backspace all work, so the field is never locked.
            Headless.Run(() =>
            {
                var typing = Limited(text + Break, text.Length, maxLines);
                typing.Type("x");
                Assert.Equal(text + "x" + Break, Text(typing));
            });
            Headless.Run(() =>
            {
                var delete = Limited(text + Break, text.Length, maxLines);
                delete.Press(Key.Delete);
                Assert.Equal(text, Text(delete));
            });
            Headless.Run(() =>
            {
                var back = Limited(text + Break, text.Length, maxLines);
                back.Press(Key.Back);
                Assert.Equal(text.Substring(0, text.Length - 1) + Break, Text(back));
                for (int i = 0; i < 6; i++) back.Press(Key.Back);   // Backspace can empty the field, but typing then still works
                back.Type("z");
                Assert.Contains("z", Text(back));
            });
        }

        [Fact]
        public void A_paragraph_that_is_only_a_break_in_a_one_line_field_takes_typing_and_Delete() => Headless.Run(() =>
        {
            var typing = Limited(Break, 0, 1);
            typing.Type("x");
            Assert.Equal("x" + Break, Text(typing));
            var delete = Limited(Break, 0, 1);
            delete.Press(Key.Delete);
            Assert.Equal(string.Empty, Text(delete));
        });

        [Fact]
        public void Text_after_a_break_that_the_limit_hides_refuses_typing_at_the_visible_end_and_Delete_only_removes_the_break() => Headless.Run(() =>
        {
            var field = Limited("ab" + Break + "cd", 2, 1);
            Assert.True(field.ParagraphLayoutFor(0).Truncated);
            field.Type("x");
            Assert.Equal("ab" + Break + "cd", Text(field));
            field.Press(Key.Delete);   // removes only the invisible break
            Assert.Equal("abcd", Text(field));
        });

        [Fact]
        public void Backspace_from_the_next_paragraph_is_refused_when_the_one_above_ends_in_a_hidden_break() => Headless.Run(() =>
        {
            var p0 = Para("en", "en", "ab" + Break);
            p0.Props.SetInt(TextPropertyNumbers.MaxLines, 1);
            var field = Fixtures.Field(Doc(p0, Para("en", "en", "cd")), 520);
            field.Editor.SetCaret(new TextPosition(1, 0));
            field.Press(Key.Back);
            Assert.Equal(2, field.Editor.Document.Paragraphs.Count);
        });

        static RichTextField BreakFirstThenHidden()
        {
            // The second paragraph starts with an editable hard break and the line limit hides everything after it.
            var p1 = Para("en", "en", Break + "rest");
            p1.Props.SetInt(TextPropertyNumbers.MaxLines, 1);
            var field = Fixtures.Field(Doc(Para("en", "en", "ab"), p1), 520);
            var pl = field.ParagraphLayoutFor(1);
            Assert.True(pl.Truncated);
            Assert.Equal(0, pl.VisibleEnd);
            return field;
        }

        [Fact]
        public void Backspace_at_the_start_of_a_paragraph_that_begins_with_an_editable_break_and_hides_the_rest_joins_it_to_the_one_above() => Headless.Run(() =>
        {
            // The paragraph mark is reachable (the caret rests at the start of the second paragraph), so the join is allowed even
            // though the editable text beside it is hidden; FieldWorks' join does not look at truncation.
            var field = BreakFirstThenHidden();
            field.Editor.SetCaret(new TextPosition(1, 0));
            field.Press(Key.Back);
            Assert.Single(field.Editor.Document.Paragraphs);
            Assert.Equal("ab" + Break + "rest", Text(field));
            Assert.Equal(new TextPosition(0, 2), field.Editor.Caret);
        });

        [Fact]
        public void Delete_at_the_end_of_the_paragraph_above_one_that_begins_with_an_editable_break_and_hides_the_rest_joins_them() => Headless.Run(() =>
        {
            var field = BreakFirstThenHidden();
            field.Editor.SetCaret(new TextPosition(0, 2));
            field.Press(Key.Delete);
            Assert.Single(field.Editor.Document.Paragraphs);
            Assert.Equal("ab" + Break + "rest", Text(field));
        });

        [Fact]
        public void The_join_is_still_refused_when_the_hidden_paragraph_begins_with_text_that_may_not_be_edited() => Headless.Run(() =>
        {
            var p1 = new Paragraph { DefaultWs = "en" };
            var locked = new Run { Ws = "en", Text = Break };
            locked.Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableNo);
            p1.Runs.Add(locked);
            p1.Runs.Add(new Run { Ws = "en", Text = "rest" });
            p1.Props.SetInt(TextPropertyNumbers.MaxLines, 1);
            var field = Fixtures.Field(Doc(Para("en", "en", "ab"), p1), 520);
            field.Editor.SetCaret(new TextPosition(1, 0));
            field.Press(Key.Back);
            Assert.Equal(2, field.Editor.Document.Paragraphs.Count);
            field.Editor.SetCaret(new TextPosition(0, 2));
            field.Press(Key.Delete);
            Assert.Equal(2, field.Editor.Document.Paragraphs.Count);
        });

        [Fact]
        public void A_preedit_before_a_trailing_break_draws_its_caret_after_the_preedit_on_the_same_line() => Headless.Run(() =>
        {
            var field = Field("ab" + Break, 2);
            field.SetPreedit("xy", 2);
            var pl = field.ParagraphLayoutFor(0);
            Assert.Equal(2, pl.PreeditAt);
            var caret = Caret(field);
            Assert.Equal(pl.Lines[0].TextY, caret.Y, 3);
            Assert.Equal(pl.CaretRect(4, true).X, caret.X, 3);   // after "abxy"
        });

        [Fact]
        public void Selecting_only_a_hard_break_draws_a_small_block_at_the_end_of_its_line() => Headless.Run(() =>
        {
            var field = Field("ab" + Break, 0);
            var pl = field.ParagraphLayoutFor(0);
            var rects = pl.SelectionRects(2, 3);
            var block = Assert.Single(rects);
            Assert.Equal(pl.CaretRect(2).X, block.X, 3);
            Assert.True(block.Width >= 2 && block.Height > 5);
            Assert.Equal(pl.Lines[0].TextY, block.Y, 3);
            // text plus the break draws the text's rectangle only, as before
            Assert.Single(pl.SelectionRects(1, 3));
            // a break-only middle line
            var mid = Field("a" + Break + Break + "b", 0).ParagraphLayoutFor(0);
            Assert.Single(mid.SelectionRects(2, 3));
        });

        static RichTextField LockedThenEditable(string locked, string editable, int maxLines)
        {
            var p = new Paragraph { DefaultWs = "en" };
            var first = new Run { Ws = "en", Text = locked };
            first.Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableNo);
            p.Runs.Add(first);
            p.Runs.Add(new Run { Ws = "en", Text = editable });
            p.Props.SetInt(TextPropertyNumbers.MaxLines, maxLines);
            return Fixtures.Field(Doc(p), 520);
        }

        [Theory]
        [InlineData("|", "y", 1)]
        [InlineData("ab|", "cd", 1)]
        [InlineData("a|b|", "cd", 2)]
        public void A_caret_set_where_the_only_editable_place_is_hidden_settles_at_the_visible_end(string locked, string editable, int maxLines) => Headless.Run(() =>
        {
            var field = LockedThenEditable(locked.Replace("|", Break), editable, maxLines);
            var pl = field.ParagraphLayoutFor(0);
            Assert.True(pl.Truncated);
            int visibleEnd = pl.VisibleEnd;
            Assert.True(visibleEnd < Text(field).Length);
            for (int o = 0; o <= Text(field).Length; o++)
            {
                field.Editor.SetCaret(new TextPosition(0, o));   // used to recurse until the stack overflowed
                Assert.Equal(new TextPosition(0, System.Math.Min(o, visibleEnd)), field.Editor.Caret);
            }
            field.Editor.SetCaret(new TextPosition(0, 0));
            field.Press(Key.End);
            field.Press(Key.Right);
            field.Press(Key.Down);
            Assert.True(field.Editor.Caret.Offset <= visibleEnd);
        });

        [Fact]
        public void A_field_whose_only_editable_place_is_hidden_has_no_place_to_edit_and_Tab_leaves() => Headless.Run(() =>
        {
            var field = LockedThenEditable(Break, "y", 1);
            string original = Text(field);
            field.Editor.SetCaret(new TextPosition(0, 0));
            field.Type("x");
            field.Press(Key.Delete);   // the break is not editable, so Delete cannot remove it
            Assert.Equal(original, Text(field));
            Assert.False(field.Editor.TryGetEditablePlace(EditablePlace.Next, out _));
            Assert.False(field.Editor.TryGetEditablePlace(EditablePlace.Last, out _));
            int left = 0;
            field.LeaveRequested += (s, e) => { left++; e.Handled = true; };
            field.Press(Key.Tab);
            Assert.Equal(1, left);   // Tab is not trapped by a place nobody can reach
        });

        [Theory]
        [InlineData("|", "y", 1)]
        [InlineData("ab|", "cd", 1)]
        [InlineData("a|b|", "cd", 2)]
        public void A_non_editable_break_counts_toward_the_line_limit_and_the_editable_text_behind_it_stays_hidden(string locked, string editable, int maxLines) => Headless.Run(() =>
        {
            // The limit counts lines whatever their editability (as in FieldWorks), so host data that puts the only editable text
            // behind a locked break at the limit is hidden and cannot be edited from the keyboard (ADR 0024).
            string text = locked.Replace("|", Break) + editable;
            var field = LockedThenEditable(locked.Replace("|", Break), editable, maxLines);
            var pl = field.ParagraphLayoutFor(0);
            Assert.True(pl.Truncated);
            Assert.Equal(maxLines, pl.Lines.Count);
            Assert.True(pl.VisibleEnd <= locked.Length);
            for (int o = 0; o <= text.Length; o++)
                foreach (var act in new System.Action<RichTextField>[] { f => f.Type("x"), f => f.Press(Key.Delete), f => f.Press(Key.Back) })
                {
                    field.Editor.SetCaret(new TextPosition(0, o));
                    act(field);
                    Assert.Equal(text, Text(field));
                }
        });

        [Fact]
        public void The_visible_end_is_not_an_editable_place_when_a_wrap_falls_on_the_boundary_of_locked_and_hidden_editable_text() => Headless.Run(() =>
        {
            // 40 locked characters then 40 editable ones at MaxLines 1: at some widths the one line holds exactly the
            // locked run, so the visible end is the start of the editable run, whose text is hidden.
            int found = 0;
            for (int width = 200; width <= 700; width++)
            {
                var p = new Paragraph { DefaultWs = "en" };
                var locked = new Run { Ws = "en", Text = new string((char)97, 40) };
                locked.Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableNo);
                p.Runs.Add(locked);
                p.Runs.Add(new Run { Ws = "en", Text = new string((char)98, 40) });
                p.Props.SetInt(TextPropertyNumbers.MaxLines, 1);
                var field = Fixtures.Field(Doc(p), width);
                var pl = field.ParagraphLayoutFor(0);
                if (!pl.Truncated || pl.VisibleEnd != 40) continue;   // the wrap is somewhere else at this width
                found++;
                var visibleEnd = new TextPosition(0, 40);
                Assert.False(field.Editor.IsEditableAt(visibleEnd), $"width {width}");
                Assert.False(field.Editor.TryGetEditablePlace(EditablePlace.Next, out _), $"width {width}");
                Assert.False(field.Editor.TryGetEditablePlace(EditablePlace.Last, out _), $"width {width}");
                field.Editor.SetCaret(new TextPosition(0, 0));
                Assert.Equal(new TextPosition(0, 0), field.Editor.Caret);   // no editable place to move to
                field.Editor.SetCaret(visibleEnd);
                Assert.False(field.Editor.CanEdit);
                field.Type("x");
                Assert.Equal(80, field.Editor.Document.Paragraphs[0].Length);
                int left = 0;
                field.LeaveRequested += (s, e) => { left++; e.Handled = true; };
                field.Press(Key.Tab);
                Assert.Equal(1, left);   // Tab leaves: the caret is not offered where nothing can be typed
            }
            Assert.True(found > 0);   // some width wraps on the boundary in the test font
        });

        static Paragraph Limit(string text, int maxLines)
        {
            var p = Para("en", "en", text);
            p.Props.SetInt(TextPropertyNumbers.MaxLines, maxLines);
            return p;
        }

        static string[] Texts(RichTextField field) => field.Editor.Document.Paragraphs.Select(x => x.Text).ToArray();

        [Fact]
        public void Shift_Enter_over_a_selection_that_spans_limited_paragraphs_is_refused() => Headless.Run(() =>
        {
            var field = Fixtures.Field(Doc(Limit("", 1), Limit("ab", 1)), 520);
            field.Editor.SetCaret(new TextPosition(1, 0));
            field.Press(Key.Left, KeyModifiers.Shift);   // the selection is the boundary; the caret sits in the first paragraph
            field.Press(Key.Enter, KeyModifiers.Shift);
            Assert.Equal(new[] { "", "ab" }, Texts(field));
            var forward = Fixtures.Field(Doc(Limit("", 1), Limit("ab", 1)), 520);   // and the other way round: caret in the second paragraph
            forward.Editor.SetCaret(new TextPosition(0, 0));
            forward.Press(Key.Right, KeyModifiers.Shift);
            forward.Press(Key.Enter, KeyModifiers.Shift);
            Assert.Equal(new[] { "", "ab" }, Texts(forward));
        });

        const double NarrowWidth = 150;
        const string WrappingText = "aaaaaaaaa bbb ccccccc ddddd eeeeeeeeeee ff gggggggg hhhh iiiiiiiii jjj kkkkkkk lllll mmmmmmmmmmm nn oooooooo pppp";

        static RichTextField NarrowLimited(int maxLines, int caret)
        {
            var field = Fixtures.Field(Doc(Limit(WrappingText, maxLines)), NarrowWidth);
            field.Editor.SetCaret(new TextPosition(0, caret));
            return field;
        }

        [Fact]
        public void Shift_Enter_in_wrapped_text_is_judged_by_the_lines_the_result_lays_out_to_at_every_offset() => Headless.Run(() =>
        {
            int now = Fixtures.Field(OneParagraph(WrappingText), NarrowWidth).ParagraphLayoutFor(0).Lines.Count;
            Assert.True(now >= 4, "the sample wraps onto several lines: " + now);
            int allowedAtLimit = 0, refusedAtLimit = 0;
            for (int o = 0; o <= WrappingText.Length; o++)
            {
                string broken = WrappingText.Substring(0, o) + Break + WrappingText.Substring(o);
                int after = Fixtures.Field(OneParagraph(broken), NarrowWidth).ParagraphLayoutFor(0).Lines.Count;   // what the break leaves
                foreach (int max in new[] { now, now + 1 })
                {
                    var field = NarrowLimited(max, o);
                    field.Press(Key.Enter, KeyModifiers.Shift);
                    bool allowed = Text(field) == broken;
                    Assert.True(allowed == after <= max, "break at " + o + ", limit " + max + ": the result needs " + after + " lines");
                    if (allowed)
                    {
                        var laid = field.ParagraphLayoutFor(0);
                        Assert.False(laid.Truncated || laid.HiddenTrailingBreak, "a break at " + o + " hid text");
                    }
                    else Assert.Equal(WrappingText, Text(field));
                    if (max == now) { if (allowed) allowedAtLimit++; else refusedAtLimit++; }
                }
            }
            // At the limit a break that reflows the text into the same number of lines is fine; one that needs another line is not.
            Assert.True(allowedAtLimit > 0 && refusedAtLimit > 0, allowedAtLimit + " allowed, " + refusedAtLimit + " refused at the limit");
        });

        [Fact]
        public void Shift_Enter_over_a_selection_spanning_paragraphs_is_allowed_when_the_laid_out_result_fits_the_limit() => Headless.Run(() =>
        {
            var field = Fixtures.Field(Doc(Limit("ab", 2), Limit("cd", 2)), 520);
            field.Editor.Anchor = new TextPosition(0, 2);
            field.Editor.SetCaret(new TextPosition(1, 1), true);
            field.Press(Key.Enter, KeyModifiers.Shift);
            Assert.Equal(new[] { "ab" + Break + "d" }, Texts(field));   // two lines under a two-line limit
        });

        [Fact]
        public void Shift_Enter_over_a_selection_spanning_paragraphs_is_refused_when_the_laid_out_result_wraps_past_the_limit() => Headless.Run(() =>
        {
            var field = Fixtures.Field(Doc(Limit("ab", 2), Limit(WrappingText, 2)), NarrowWidth);
            field.Editor.Anchor = new TextPosition(0, 2);
            field.Editor.SetCaret(new TextPosition(1, 0), true);
            field.Press(Key.Enter, KeyModifiers.Shift);
            Assert.Equal(new[] { "ab", WrappingText }, Texts(field));
        });

        static RichTextField BoundarySelected(Paragraph first, Paragraph second)
        {
            var field = Fixtures.Field(Doc(first, second), 520);
            field.Editor.SetCaret(new TextPosition(1, 0));
            field.Press(Key.Left, KeyModifiers.Shift);   // the selection is the paragraph boundary
            Assert.True(field.Editor.HasSelection);
            return field;
        }

        [Fact]
        public void Shift_Enter_across_an_empty_unlimited_paragraph_and_a_one_line_one_is_refused_because_the_one_line_paragraph_survives() => Headless.Run(() =>
        {
            var field = BoundarySelected(Para("en", "en", ""), Limit("ab", 1));
            field.Press(Key.Enter, KeyModifiers.Shift);
            Assert.Equal(new[] { "", "ab" }, Texts(field));   // the result, a break then ab, would hide "ab" in the surviving one-line paragraph
        });

        [Fact]
        public void Shift_Enter_across_an_empty_one_line_paragraph_and_an_unlimited_one_is_allowed_because_the_unlimited_paragraph_survives() => Headless.Run(() =>
        {
            var field = BoundarySelected(Limit("", 1), Para("en", "en", "ab"));
            field.Press(Key.Enter, KeyModifiers.Shift);
            Assert.Equal(new[] { Break + "ab" }, Texts(field));
        });

        [Fact]
        public void Shift_Enter_over_a_selection_spanning_paragraphs_is_refused_when_the_first_has_two_full_lines() => Headless.Run(() =>
        {
            var field = Fixtures.Field(Doc(Limit("ab" + Break + "cd", 2), Limit("ef", 2)), 520);
            field.Editor.SetCaret(new TextPosition(1, 0));
            field.Press(Key.Left, KeyModifiers.Shift);
            field.Press(Key.Enter, KeyModifiers.Shift);
            Assert.Equal(new[] { "ab" + Break + "cd", "ef" }, Texts(field));
        });

        [Fact]
        public void Shift_Enter_in_an_empty_one_line_paragraph_is_refused_but_an_empty_two_line_paragraph_takes_it() => Headless.Run(() =>
        {
            var one = Limited("", 0, 1);
            one.Press(Key.Enter, KeyModifiers.Shift);
            Assert.Equal("", Text(one));
            var two = Limited("", 0, 2);   // an empty paragraph is one line, so one more fits
            two.Press(Key.Enter, KeyModifiers.Shift);
            Assert.Equal(Break, Text(two));
        });

        static void AssertRecoveredByDelete(RichTextField field, string afterDelete, int caret)
        {
            field.Editor.SetCaret(new TextPosition(0, caret));
            Assert.True(field.ParagraphLayoutFor(0).Truncated);
            field.Press(Key.Delete);
            Assert.Equal(afterDelete, Text(field));
            Assert.False(field.ParagraphLayoutFor(0).Truncated);
            field.Type("z");
            Assert.Contains("z", Text(field));
        }

        [Fact]
        public void Host_text_with_a_break_the_limit_hides_is_recovered_by_Delete_at_the_visible_end() => Headless.Run(() =>
        {
            AssertRecoveredByDelete(Limited("ab" + Break + "cd", 2, 1), "abcd", 2);
            var start = Limited(Break + "y", 0, 1);
            Assert.Equal(0, start.ParagraphLayoutFor(0).VisibleEnd);
            AssertRecoveredByDelete(start, "y", 0);
        });

        [Fact]
        public void Typed_U2028_at_the_limit_makes_text_that_Delete_brings_back() => Headless.Run(() =>
        {
            var field = Limited("ab", 1, 1);
            field.Type(Break);
            Assert.Equal("a" + Break + "b", Text(field));
            AssertRecoveredByDelete(field, "ab", field.Editor.Caret.Offset);
        });

        [Fact]
        public void Pasted_U2028_at_the_limit_makes_text_that_Delete_brings_back() => Headless.Run(async () =>
        {
            var (window, field) = Fixtures.InWindow(Doc(Limit("ab", 1)));
            try
            {
                field.Editor.SetCaret(new TextPosition(0, 2));
                await ClipboardCompat.SetAsync(global::Avalonia.Controls.TopLevel.GetTopLevel(field), "x" + Break + "y");
                await field.PasteAsync();
                Assert.Equal("abx" + Break + "y", Text(field));
                AssertRecoveredByDelete(field, "abxy", field.Editor.Caret.Offset);
            }
            finally { window.Close(); }
        });

        [Fact]
        public void Backspacing_to_the_start_of_hidden_text_leaves_a_state_Delete_recovers() => Headless.Run(() =>
        {
            var field = Limited("abx" + Break + "y", 3, 1);
            for (int i = 0; i < 3; i++) field.Press(Key.Back);
            Assert.Equal(Break + "y", Text(field));
            AssertRecoveredByDelete(field, "y", 0);
        });

        /// <summary>The union of the glyph boxes of the first line's text, in paragraph coordinates.</summary>
        static Rect InkBox(ParagraphLayout pl)
        {
            var li = pl.Lines[0];
            Rect? union = null;
            foreach (var tb in li.Line.GetTextBounds(li.Start, pl.TextEnd(li) - li.Start))
            {
                var r = new Rect(li.X + tb.Rectangle.X, li.TextY + tb.Rectangle.Y, tb.Rectangle.Width, tb.Rectangle.Height);
                union = union.HasValue ? union.Value.Union(r) : r;
            }
            return union!.Value;
        }

        [Fact]
        public void Selecting_only_a_hard_break_in_a_right_to_left_paragraph_ending_in_Latin_draws_the_block_left_of_the_text() => Headless.Run(() =>
        {
            var pl = Field("ab" + Break + "cd", 0, rtl: true).ParagraphLayoutFor(0);
            var block = Assert.Single(pl.SelectionRects(2, 3));
            var ink = InkBox(pl);
            Assert.True(block.X >= 0 && block.Right <= 520.01, "the block is inside the field");
            Assert.True(block.Right <= ink.X + 0.01, "the break sits at the line's end in the paragraph direction, left of the text, not over the last glyph");
            Assert.Equal(pl.Lines[0].TextY, block.Y, 3);
        });

        [Fact]
        public void Selecting_only_a_hard_break_in_a_right_to_left_paragraph_ending_in_Arabic_draws_the_block_left_of_the_text() => Headless.Run(() =>
        {
            var pl = Field("\u0627\u0628" + Break + "\u062c\u062f", 0, rtl: true).ParagraphLayoutFor(0);
            var block = Assert.Single(pl.SelectionRects(2, 3));
            Assert.True(block.X >= 0 && block.Right <= 520.01, "the block is inside the field");
            Assert.True(block.Right <= InkBox(pl).X + 0.01, "left of the text");
        });

        [Fact]
        public void Selecting_only_a_hard_break_in_a_left_to_right_paragraph_ending_in_Arabic_draws_the_block_right_of_the_text() => Headless.Run(() =>
        {
            var p = Para("en", "ar", "\u0627\u0628" + Break + "\u062c\u062f");
            var field = Fixtures.Field(Doc(p), 520);
            var pl = field.ParagraphLayoutFor(0);
            var block = Assert.Single(pl.SelectionRects(2, 3));
            Assert.True(block.X >= InkBox(pl).Right - 0.01, "right of the text, where the break is in a left-to-right paragraph");
        });
    }
}
