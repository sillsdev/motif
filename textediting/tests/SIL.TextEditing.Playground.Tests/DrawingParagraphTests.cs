using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using SIL.TextEditing.Avalonia;
using Xunit;

namespace SIL.TextEditing.Playground.Tests
{
    /// <summary>The paragraph drawing properties of the "Paragraph formatting" scenario, read back from the layouts the field holds and from
    /// its rendered pixels. Lengths in points are drawn as device-independent pixels, as font sizes are.</summary>
    public class DrawingParagraphTests
    {
        // Paragraph indexes in Scenarios.ParagraphsDoc.
        const int Plain = 1, Centred = 2, Right = 3, Trailing = 4, Justified = 5, FirstLine = 6, Hanging = 7, Quote = 8, OwnCentred = 9,
            Double = 10, Exact = 11, Boxed = 12, TwoLines = 13, Number1 = 14, Number3 = 16, Roman1 = 17, Letter1 = 19, Bullet1 = 21, Arabic = 23, ArabicCentred = 24,
            ArabicNumber1 = 25, ArabicNumber2 = 26, Flush = 27, ArabicLong = 28, ArabicBullet = 29;

        static double Left(LineInfo li) => li.X + li.Line.Start;

        [Fact]
        public void Alignment_leading_centre_right_trailing_and_justify_place_the_lines() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs))
            {
                var plain = s.Field.ParagraphLayoutFor(Plain);
                Assert.True(plain.Lines.Count > 1);
                Assert.Equal(0, Left(plain.Lines[0]), 1);
                foreach (int index in new[] { Centred, OwnCentred })
                {
                    var c = s.Field.ParagraphLayoutFor(index);
                    var last = c.Lines[c.Lines.Count - 1];
                    double slack = c.ContentWidth - last.Line.Width;
                    Assert.True(slack > 20, "paragraph " + index);
                    Assert.Equal(c.ContentLeft + slack / 2, Left(last), 1);
                }
                foreach (int index in new[] { Right, Trailing })
                {
                    var r = s.Field.ParagraphLayoutFor(index);
                    var last = r.Lines[r.Lines.Count - 1];
                    Assert.Equal(r.ContentLeft + r.ContentWidth, Left(last) + last.Line.WidthIncludingTrailingWhitespace, 1);
                }
            }
        });

        const int ArabicJustified = 34;

        [Fact]
        public void Justified_lines_but_the_last_reach_both_edges_of_the_text_area_and_the_last_stays_at_the_leading_edge_in_both_directions() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs, 1100, 8000))
            {
                var shot = new Shot(s);
                foreach (int p in new[] { Justified, ArabicJustified })
                {
                    var pl = s.Field.ParagraphLayoutFor(p);
                    Assert.Equal(5, pl.Format.Align);
                    Assert.True(pl.Lines.Count > 2, "paragraph " + p);
                    double top = s.Field.ParagraphTop(p), left = pl.ContentLeft, right = pl.ContentLeft + pl.ContentWidth;
                    for (int i = 0; i < pl.Lines.Count; i++)
                    {
                        var li = pl.Lines[i];
                        var columns = shot.InkColumns(0, shot.Width, top + li.TextY + 1, top + li.TextY + li.Line.Height - 1);
                        Assert.NotEmpty(columns);
                        if (i < pl.Lines.Count - 1)
                        {
                            Assert.InRange(columns.Min(), left - 1, left + 8);   // (a few pixels of side bearing)
                            Assert.InRange(columns.Max(), right - 8, right + 1);
                        }
                        else if (pl.Rtl)
                        {
                            Assert.InRange(columns.Max(), right - 8, right + 1);   // the last line of a right-to-left paragraph starts at the right edge
                            Assert.True(columns.Min() > left + 20, "and does not reach the left edge");
                        }
                        else
                        {
                            Assert.InRange(columns.Min(), left - 1, left + 8);   // (a few pixels of side bearing)
                            Assert.True(columns.Max() < right - 20, "and does not reach the right edge");
                        }
                    }
                }
            }
        });

        [Fact]
        public void An_underline_runs_on_through_the_stretched_spaces_of_a_justified_line() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs, 1100, 8000))
            {
                foreach (var run in s.Editor.Document.Paragraphs[Justified].Runs) run.Props.SetInt(TextPropertyNumbers.Underline, TextPropertyNumbers.UnderlineSingle, 0);
                s.Field.RefreshFromDocument();
                Headless.Pump();
                var pl = s.Field.ParagraphLayoutFor(Justified);
                var shot = new Shot(s);
                double top = s.Field.ParagraphTop(Justified);
                var li = pl.Lines[0];
                // The row of the underline is the one under the baseline with the most ink.
                int baseline = (int)System.Math.Round(top + li.TextY + li.Line.Baseline), best = 0, row = -1;
                for (int y = baseline + 1; y < baseline + 8; y++)
                {
                    int n = 0;
                    for (int x = (int)pl.ContentLeft; x < (int)(pl.ContentLeft + pl.ContentWidth); x++) if (shot.IsInk(x, y, 200)) n++;
                    if (n > best) { best = n; row = y; }
                }
                Assert.True(row > 0);
                int width = (int)pl.ContentWidth;
                Assert.True(best >= width * 0.97, "the underline is " + best + " of " + width + " pixels wide, so it runs through the stretched spaces");
            }
        });

        [Fact]
        public void Mouse_clicks_and_the_caret_agree_with_the_stretched_lines_of_a_justified_paragraph() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs, 1100, 8000))
            {
                foreach (int p in new[] { Justified, ArabicJustified })
                {
                    var pl = s.Field.ParagraphLayoutFor(p);
                    foreach (var li in pl.Lines)
                        foreach (int offset in new[] { li.Start + 1, (li.Start + pl.TextEnd(li)) / 2, pl.TextEnd(li) - 2 })
                        {
                            s.MouseClick(Edge(s, p, offset));
                            Assert.Equal(new TextPosition(p, offset), s.Editor.Caret);
                        }
                }
            }
        });
        [Fact]
        public void An_arabic_paragraph_leads_from_the_right_and_a_hanging_indent_hangs_on_the_right() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs))
            {
                var a = s.Field.ParagraphLayoutFor(Arabic);
                Assert.True(a.Rtl);
                Assert.True(a.Lines.Count > 2);
                double right = a.LayoutWidth;
                // Leading indent 36 and hanging 36: the first line's right edge is 36 in, the others 72.
                // The visible text ends at the right edge; a full line's hanging space can push it a few pixels past.
                Assert.InRange(Left(a.Lines[0]) + a.Lines[0].Line.WidthIncludingTrailingWhitespace, right - 36 - 0.5, right - 36 + 6);
                Assert.InRange(Left(a.Lines[1]) + a.Lines[1].Line.WidthIncludingTrailingWhitespace, right - 72 - 0.5, right - 72 + 6);
                var c = s.Field.ParagraphLayoutFor(ArabicCentred);
                Assert.Equal(c.LayoutWidth / 2, Left(c.Lines[0]) + c.Lines[0].Line.Width / 2, 1);
            }
        });

        [Fact]
        public void First_line_and_hanging_indents_and_the_two_margins_move_the_text_area() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs))
            {
                var f = s.Field.ParagraphLayoutFor(FirstLine);
                Assert.Equal(24, f.Lines[0].X - f.Lines[1].X, 3);
                Assert.True(f.Lines[0].Line.Width <= f.ContentWidth - 24 + 0.5);
                var h = s.Field.ParagraphLayoutFor(Hanging);
                Assert.Equal(36, h.Lines[0].X, 3);
                Assert.Equal(72, h.Lines[1].X, 3);
                var q = s.Field.ParagraphLayoutFor(Quote);
                Assert.Equal(36, q.ContentLeft, 3);
                Assert.Equal(q.LayoutWidth - 72, q.ContentWidth, 3);
                foreach (var li in q.Lines) Assert.True(li.Line.Width <= q.ContentWidth + 0.5);
            }
        });

        [Fact]
        public void Space_before_and_after_add_to_the_paragraph_height_and_move_the_next_paragraph() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs))
            {
                var heading = s.Field.ParagraphLayoutFor(0);
                Assert.Equal(12, heading.TopInset, 3);
                Assert.Equal(6, heading.BottomInset, 3);
                Assert.Equal(heading.TopInset, heading.Lines[0].Y, 3);
                Assert.Equal(heading.TopInset + heading.Lines.Sum(l => l.Height) + heading.BottomInset, heading.Height, 3);
                Assert.Equal(heading.Height, s.Field.ParagraphTop(1), 3);
            }
        });

        /// <summary>The distance between the text of paragraph 1 and the text of paragraph 2 after giving them these spacings (points).</summary>
        static double GapBetween(Session s, double after, double before, double marginTop)
        {
            var paragraphs = s.Editor.Document.Paragraphs;
            paragraphs[Plain].Props.SetInt(TextPropertyNumbers.SpaceAfter, (int)(after * 1000), 1);
            paragraphs[Centred].Props.SetInt(TextPropertyNumbers.SpaceBefore, (int)(before * 1000), 1);
            paragraphs[Centred].Props.SetInt(TextPropertyNumbers.MarginTop, (int)(marginTop * 1000), 1);
            s.Field.RefreshFromDocument();
            Headless.Pump();
            var first = s.Field.ParagraphLayoutFor(Plain);
            var second = s.Field.ParagraphLayoutFor(Centred);
            double textBottom = s.Field.ParagraphTop(Plain) + first.Height - first.BottomInset;
            return s.Field.ParagraphTop(Centred) + second.Lines[0].Y - textBottom;
        }

        [Fact]
        public void The_gap_between_two_paragraphs_is_the_larger_of_the_margin_top_and_space_after_plus_space_before() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs))
            {
                double text = GapBetween(s, 6, 4, 0);
                Assert.Equal(10, text, 3);
                Assert.Equal(20, GapBetween(s, 6, 4, 20), 3);   // the margin top is the larger
                Assert.Equal(10, GapBetween(s, 6, 4, 8), 3);    // the sum is the larger
                Assert.Equal(8, GapBetween(s, 0, 0, 8), 3);
                Assert.Equal(6, GapBetween(s, 6, 0, 0), 3);
                // The first paragraph has nothing above it: the larger of its margin top and its space before.
                s.Editor.Document.Paragraphs[0].Props.SetInt(TextPropertyNumbers.MarginTop, 20000, 1);
                s.Field.RefreshFromDocument();
                Assert.Equal(20, s.Field.ParagraphLayoutFor(0).TopInset, 3);
            }
        });

        [Fact]
        public void Line_height_is_at_least_the_text_for_relative_and_exact_for_a_negative_value() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs))
            {
                var d = s.Field.ParagraphLayoutFor(Double);
                Assert.True(d.Lines.Count > 1);
                // Relative 200 per cent of the 1.4 line: 2 x 1.4 x 16 points.
                foreach (var li in d.Lines) Assert.Equal(44.8, li.Height, 3);
                var e = s.Field.ParagraphLayoutFor(Exact);
                foreach (var li in e.Lines) Assert.Equal(30, li.Height, 3);
                foreach (var li in e.Lines) Assert.Equal(li.Y + System.Math.Floor(e.Lines[0].Line.Baseline / e.Lines[0].Line.Height * 100) / 100 * 30, li.TextY + li.Line.Baseline, 3);   // the baseline keeps the text's own proportion of the slot
                // Hit testing and the caret follow the slot: a point low in the first slot is still the first line.
                var hit = e.HitTestDetailed(new Point(10, e.Lines[0].Y + 29));
                Assert.True(hit.Offset < e.Lines[1].Start);
                Assert.Equal(e.Lines[0].TextY, e.CaretRect(0).Y, 3);
            }
        });

        [Fact]
        public void A_line_limit_drops_the_lines_beyond_it() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs))
            {
                var t = s.Field.ParagraphLayoutFor(TwoLines);
                Assert.Equal(2, t.Lines.Count);
                Assert.True(t.Truncated);
                Assert.False(s.Field.ParagraphLayoutFor(Plain).Truncated);
            }
        });

        // Text a line limit hides cannot be reached: the caret stops at the end of the last line shown, as FieldWorks puts an
        // insertion point that would land in truncated text at the last visible position. The text stays in the model.

        [Fact]
        public void A_caret_set_into_text_a_line_limit_hides_is_drawn_at_the_end_of_the_last_visible_line() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs, 700, 3000))
            {
                var pl = s.Field.ParagraphLayoutFor(TwoLines);
                Assert.True(pl.Truncated);
                int visible = pl.VisibleEnd, length = pl.Paragraph.Length;
                Assert.InRange(visible, pl.Lines[1].Start + 1, length - 20);
                Assert.Equal(pl.Lines[1].End, visible);
                foreach (int hidden in new[] { visible + 1, length - 1, length })
                {
                    s.Caret(TwoLines, hidden);
                    Assert.Equal(new TextPosition(TwoLines, visible), s.Editor.Caret);
                    var r = s.Field.CaretRect();
                    var end = pl.CaretRect(visible);
                    Assert.Equal(end.X, r.X, 3);
                    Assert.Equal(s.Field.ParagraphTop(TwoLines) + pl.Lines[1].TextY, r.Y, 3);
                }
                Assert.Equal(length, s.Editor.Document.Paragraphs[TwoLines].Length);
            }
        });

        [Fact]
        public void Right_and_End_stop_at_the_end_of_the_visible_text_and_Right_there_goes_to_the_next_paragraph() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs, 700, 3000))
            {
                var pl = s.Field.ParagraphLayoutFor(TwoLines);
                int visible = pl.VisibleEnd;
                s.Caret(TwoLines, visible - 3);
                s.Press(global::Avalonia.Input.Key.Right, times: 3);
                Assert.Equal(new TextPosition(TwoLines, visible), s.Editor.Caret);
                s.Press(global::Avalonia.Input.Key.Right);
                Assert.Equal(new TextPosition(TwoLines + 1, 0), s.Editor.Caret);
                s.Press(global::Avalonia.Input.Key.Left);
                Assert.Equal(new TextPosition(TwoLines, visible), s.Editor.Caret);
                s.Caret(TwoLines, 5);
                s.Press(global::Avalonia.Input.Key.Down);
                s.Press(global::Avalonia.Input.Key.End);
                Assert.Equal(new TextPosition(TwoLines, visible), s.Editor.Caret);
                s.Press(global::Avalonia.Input.Key.Right, global::Avalonia.Input.KeyModifiers.Control);
                Assert.Equal(new TextPosition(TwoLines + 1, 0), s.Editor.Caret);
            }
        });

        [Fact]
        public void A_selection_into_hidden_text_ends_at_the_visible_end_and_draws_only_the_visible_lines() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs, 700, 3000))
            {
                var pl = s.Field.ParagraphLayoutFor(TwoLines);
                s.Select(TwoLines, 0, pl.Paragraph.Length);
                Assert.Equal(new TextPosition(TwoLines, 0), s.Editor.Anchor);
                Assert.Equal(new TextPosition(TwoLines, pl.VisibleEnd), s.Editor.Caret);
                var rects = pl.SelectionRects(0, pl.VisibleEnd);
                Assert.Equal(2, rects.Count);
                Assert.True(rects.All(r => r.Bottom <= pl.Height - pl.BottomInset + 0.01));
                Assert.Empty(pl.SelectionRects(pl.VisibleEnd, pl.Paragraph.Length));
                // Shift+Down from the last visible line extends into the next paragraph, not into the hidden text.
                s.Caret(TwoLines, 5);
                s.Press(global::Avalonia.Input.Key.Down);
                s.Press(global::Avalonia.Input.Key.Down, global::Avalonia.Input.KeyModifiers.Shift);
                Assert.Equal(TwoLines + 1, s.Editor.Caret.Paragraph);
            }
        });

        [Fact]
        public void Typing_Delete_Enter_composition_and_Backspace_joins_are_refused_at_the_end_of_the_visible_text_of_a_line_limited_paragraph() => Headless.Run(async () =>
        {
            using (var s = new Session(Scenarios.Paragraphs, 700, 3000))
            {
                var paragraphs = s.Editor.Document.Paragraphs;
                var pl = s.Field.ParagraphLayoutFor(TwoLines);
                int visible = pl.VisibleEnd, length = paragraphs[TwoLines].Length, count = paragraphs.Count;
                string text = paragraphs[TwoLines].Text;
                string next = paragraphs[TwoLines + 1].Text;
                s.Caret(TwoLines, visible);
                s.Type("Z");
                s.Press(global::Avalonia.Input.Key.Delete);
                s.Press(global::Avalonia.Input.Key.Delete, global::Avalonia.Input.KeyModifiers.Control);
                s.Press(global::Avalonia.Input.Key.Enter);
                s.Field.SetPreedit("ni", null);
                Assert.Null(s.Field.Preedit);
                Assert.Equal(text, paragraphs[TwoLines].Text);
                Assert.Equal(count, paragraphs.Count);
                Assert.Equal(new TextPosition(TwoLines, visible), s.Editor.Caret);
                // Paste goes nowhere either.
                s.Select(Plain, 0, 5);
                s.Click(PlaygroundView.Names.Copy);
                await s.WaitClipboard();
                s.Caret(TwoLines, visible);
                s.Click(PlaygroundView.Names.Paste);
                await s.WaitClipboard();
                Assert.Equal(text, paragraphs[TwoLines].Text);
                // Backspace at the start of the next paragraph would join it onto the hidden end: refused.
                s.Caret(TwoLines + 1, 0);
                s.Press(global::Avalonia.Input.Key.Back);
                Assert.Equal(count, paragraphs.Count);
                Assert.Equal(next, paragraphs[TwoLines + 1].Text);
                Assert.Equal(text, paragraphs[TwoLines].Text);
                // Elsewhere editing works as before: inside the visible text, and joins onto ordinary paragraphs.
                s.Caret(TwoLines, 3);
                s.Type("Q");
                Assert.Equal(length + 1, paragraphs[TwoLines].Length);
                s.Caret(TwoLines + 2, 0);
                s.Press(global::Avalonia.Input.Key.Back);
                Assert.Equal(count - 1, paragraphs.Count);
                // A selection ending at the visible end is replaced like any other.
                var again = s.Field.ParagraphLayoutFor(TwoLines);
                s.Select(TwoLines, again.VisibleEnd - 3, again.VisibleEnd);
                s.Type("W");
                Assert.Contains("W", paragraphs[TwoLines].Text);
            }
        });

        [Fact]
        public void Up_and_Down_move_between_the_visible_lines_and_the_paragraphs_around_a_line_limited_one() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs, 700, 3000))
            {
                var pl = s.Field.ParagraphLayoutFor(TwoLines);
                s.Caret(TwoLines + 1, 3);
                s.Press(global::Avalonia.Input.Key.Up);
                Assert.Equal(TwoLines, s.Editor.Caret.Paragraph);
                Assert.Equal(1, pl.LineIndexOf(s.Editor.Caret.Offset));
                Assert.True(s.Editor.Caret.Offset <= pl.VisibleEnd);
                s.Press(global::Avalonia.Input.Key.Up);
                Assert.Equal(0, pl.LineIndexOf(s.Editor.Caret.Offset));
                s.Press(global::Avalonia.Input.Key.Down);
                s.Press(global::Avalonia.Input.Key.Down);
                Assert.Equal(TwoLines + 1, s.Editor.Caret.Paragraph);
                // Typing at the end of the visible text never leaves the caret in the hidden part.
                s.Caret(TwoLines, pl.VisibleEnd);
                s.Type("xyz");
                var after = s.Field.ParagraphLayoutFor(TwoLines);
                Assert.True(s.Editor.Caret.Offset <= after.VisibleEnd);
                Assert.Equal(TwoLines, s.Editor.Caret.Paragraph);
            }
        });

        /// <summary>A point of the field, in window coordinates.</summary>
        static Point InWindow(Session s, double x, double y) => s.Field.TranslatePoint(new Point(x, y), TopLevel.GetTopLevel(s.Field)!)!.Value;

        /// <summary>The middle of a line, vertically, in the field's coordinates.</summary>
        static double MidY(Session s, int paragraph, int line)
        {
            var li = s.Field.ParagraphLayoutFor(paragraph).Lines[line];
            return s.Field.ParagraphTop(paragraph) + li.TextY + li.Line.Height / 2;
        }

        // Right-to-left paragraphs with a hanging indent and a bullet or number, and a centred one: the expectations are facts about
        // the text, not read back from the layout's own caret rectangles. The Arabic here has no Latin inside it, so it does not meet
        // the Avalonia 11.3 hit-test fault of ADR 0015 (a Latin cluster inside Arabic), which BidiTests pins.
        [Fact]
        public void Mouse_clicks_in_right_to_left_paragraphs_with_alignment_hanging_indents_and_labels_hit_the_first_letter_at_the_right_end() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs, 700, 4500))
            {
                foreach (int p in new[] { Arabic, ArabicNumber1, ArabicLong, ArabicBullet, ArabicCentred })
                {
                    var pl = s.Field.ParagraphLayoutFor(p);
                    Assert.True(pl.Rtl);
                    var first = pl.Lines[0];
                    double right = Left(first) + first.Line.WidthIncludingTrailingWhitespace, left = right - first.Line.Width;   // the trailing space of an Arabic line is on its left
                    s.MouseClick(InWindow(s, right - 2, MidY(s, p, 0)));
                    Assert.Equal(new TextPosition(p, 0), s.Editor.Caret);
                    if (pl.Lines.Count > 1)
                    {
                        s.MouseClick(InWindow(s, left + 2, MidY(s, p, 0)));
                        Assert.Equal(p, s.Editor.Caret.Paragraph);
                        Assert.InRange(s.Editor.Caret.Offset, first.End - 3, first.End);
                        var second = pl.Lines[1];
                        s.MouseClick(InWindow(s, Left(second) + second.Line.WidthIncludingTrailingWhitespace - 2, MidY(s, p, 1)));
                        Assert.Equal(new TextPosition(p, second.Start), s.Editor.Caret);
                    }
                    // A click in the hanging space or beside a centred line, outside the text, lands at the end of that line.
                    s.MouseClick(InWindow(s, 1, MidY(s, p, 0)));
                    Assert.Equal(p, s.Editor.Caret.Paragraph);
                    Assert.InRange(s.Editor.Caret.Offset, pl.Lines.Count > 1 ? first.End - 3 : pl.Paragraph.Length - 1, pl.Paragraph.Length);
                }
            }
        });

        [Fact]
        public void Arrow_keys_and_Up_Down_in_right_to_left_paragraphs_with_alignment_hanging_indents_and_labels_move_the_way_the_text_runs() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs, 700, 4500))
            {
                foreach (int p in new[] { Arabic, ArabicNumber1, ArabicLong, ArabicBullet })
                {
                    var pl = s.Field.ParagraphLayoutFor(p);
                    // The text runs right to left: the caret after the first letter is left of the caret before it, and Left moves forward.
                    s.Caret(p, 0);
                    double x0 = s.Field.CaretRect().X;
                    s.Press(global::Avalonia.Input.Key.Left);
                    Assert.Equal(new TextPosition(p, 1), s.Editor.Caret);
                    Assert.True(s.Field.CaretRect().X < x0 - 1, "paragraph " + p);
                    s.Press(global::Avalonia.Input.Key.Right);
                    Assert.Equal(new TextPosition(p, 0), s.Editor.Caret);
                    // Down keeps the caret near the same x on the next line, Up comes back to the first line.
                    s.Caret(p, 4);
                    double x = s.Field.CaretRect().X;
                    s.Press(global::Avalonia.Input.Key.Down);
                    Assert.Equal(p, s.Editor.Caret.Paragraph);
                    Assert.Equal(1, pl.LineIndexOf(s.Editor.Caret.Offset));
                    Assert.True(Math.Abs(s.Field.CaretRect().X - x) < 30, "paragraph " + p + ": x " + x + " then " + s.Field.CaretRect().X);
                    s.Press(global::Avalonia.Input.Key.Up);
                    Assert.Equal(0, pl.LineIndexOf(s.Editor.Caret.Offset));
                    // Home and End are the logical start and end of the line.
                    s.Press(global::Avalonia.Input.Key.End);
                    Assert.Equal(new TextPosition(p, pl.Lines[0].End - pl.Lines[0].Line.NewLineLength), s.Editor.Caret);
                    s.Press(global::Avalonia.Input.Key.Home);
                    Assert.Equal(new TextPosition(p, 0), s.Editor.Caret);
                }
            }
        });

        /// <summary>The caret edge of an offset (not the middle of a character), in window coordinates.</summary>
        static Point Edge(Session s, int paragraph, int offset)
        {
            var r = s.Field.ParagraphLayoutFor(paragraph).CaretRect(offset);
            return s.Field.TranslatePoint(new Point(r.X, s.Field.ParagraphTop(paragraph) + r.Y + r.Height / 2), TopLevel.GetTopLevel(s.Field)!)!.Value;
        }

        /// <summary>The field's rendered pixels, read once.</summary>
        internal sealed class Shot
        {
            readonly byte[] _buffer;
            readonly int _stride;
            public Shot(Session s)
            {
                var field = s.Field;
                var size = new PixelSize((int)Math.Ceiling(field.Bounds.Width), (int)Math.Ceiling(field.Bounds.Height));
                Width = size.Width;
                Height = size.Height;
                using (var bitmap = new RenderTargetBitmap(size))
                {
                    bitmap.Render(field);
                    _stride = size.Width * 4;
                    _buffer = new byte[_stride * size.Height];
                    var handle = System.Runtime.InteropServices.GCHandle.Alloc(_buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
                    try { bitmap.CopyPixels(new PixelRect(0, 0, size.Width, size.Height), handle.AddrOfPinnedObject(), _buffer.Length, _stride); }
                    finally { handle.Free(); }
                }
            }

            public int Width { get; }
            public int Height { get; }

            public Color At(double x, double y)
            {
                int o = (int)y * _stride + (int)x * 4;
                return Color.FromRgb(_buffer[o + 2], _buffer[o + 1], _buffer[o]);
            }

            /// <summary>True when the pixel is dark: text ink on a white or pale page.</summary>
            public bool IsInk(double x, double y, int threshold = 140) { var c = At(x, y); return c.R < threshold && c.G < threshold && c.B < threshold; }

            /// <summary>The lowest row with ink inside a box (its inner columns), or -1.</summary>
            public int InkBottom(Rect box)
            {
                for (int y = Math.Min(Height - 1, (int)Math.Ceiling(box.Bottom) - 1); y >= (int)box.Y; y--)
                    for (int x = (int)Math.Floor(box.X) + 1; x < (int)Math.Ceiling(box.Right) - 1; x++)
                        if (IsInk(x, y)) return y;
                return -1;
            }

            /// <summary>The columns in [from, to) that have dark ink anywhere in the rows [top, bottom).</summary>
            public List<int> InkColumns(double from, double to, double top, double bottom)
            {
                var result = new List<int>();
                for (int x = Math.Max(0, (int)Math.Floor(from)); x < Math.Min(Width, (int)Math.Ceiling(to)); x++)
                    for (int y = Math.Max(0, (int)Math.Floor(top)); y < Math.Min(Height, (int)Math.Ceiling(bottom)); y++)
                        if (IsInk(x, y)) { result.Add(x); break; }
                return result;
            }
        }

        internal static Color Pixel(Session s, double x, double y) => new Shot(s).At(x, y);

        [Fact]
        public void A_paragraph_colour_fills_the_box_and_borders_are_drawn_in_the_border_colour() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs, 1100, 3000))
            {
                var pl = s.Field.ParagraphLayoutFor(Boxed);
                double top = s.Field.ParagraphTop(Boxed);
                Assert.Equal(1 + 4, pl.TopInset, 3);
                Assert.Equal(12 + 3 + 6, pl.ContentLeft, 3);
                double mid = top + pl.Height / 2;
                // Left margin 12, then a 3 point blue border, 6 points of padding.
                Assert.Equal(Color.FromRgb(0, 0, 255), Pixel(s, 13, mid));
                Assert.Equal(Color.FromRgb(0xEE, 0xEE, 0xEE), Pixel(s, 12 + 3 + 2, mid));
                Assert.Equal(Color.FromRgb(255, 255, 255), Pixel(s, 6, mid));
                var topEdge = Pixel(s, 200, top + 0.5);   // the one point border lies across two pixel rows
                Assert.True(topEdge.B > 200 && topEdge.R < 128, "top border " + topEdge);
                Assert.Equal(Color.FromRgb(0xEE, 0xEE, 0xEE), Pixel(s, 200, top + 3));
            }
        });

        [Fact]
        public void A_border_with_no_colour_of_its_own_is_drawn_in_the_field_foreground_not_black() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs, 1100, 3000))
            {
                s.Editor.Document.Paragraphs[Plain].Props.SetInt(TextPropertyNumbers.BorderTop, 4000, 1);
                s.Field.Foreground = Brushes.Red;
                s.Field.Background = Brushes.Black;
                s.Field.InvalidateAllLayouts();
                Headless.Pump();
                var pl = s.Field.ParagraphLayoutFor(Plain);
                double top = s.Field.ParagraphTop(Plain);
                Assert.Equal(Color.FromRgb(255, 0, 0), Pixel(s, 300, top + 1));
                Assert.Equal(Color.FromRgb(255, 0, 0), Pixel(s, 300, top + 3));
            }
        });

        [Fact]
        public void A_label_does_not_inherit_the_first_runs_superscript_underline_or_background() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs, 1100, 3000))
            {
                var plain = s.Field.ParagraphLayoutFor(Number1).LabelProperties!;
                var run = s.Editor.Document.Paragraphs[Number1].Runs[0];
                run.Props.SetInt(TextPropertyNumbers.Superscript, 1).SetInt(TextPropertyNumbers.Underline, TextPropertyNumbers.UnderlineDouble)
                    .SetInt(TextPropertyNumbers.BackColor, 0x99FFFF).SetInt(TextPropertyNumbers.Bold, TextPropertyNumbers.ToggleOn);
                s.Field.RefreshFromDocument();
                var label = s.Field.ParagraphLayoutFor(Number1).LabelProperties!;
                Assert.Equal(BaselineAlignment.Baseline, label.BaselineAlignment);
                Assert.Null(label.TextDecorations);
                Assert.Equal(plain.FontRenderingEmSize, label.FontRenderingEmSize);
                Assert.Equal(plain.Typeface.Weight, label.Typeface.Weight);
                // The text itself still has them.
                Assert.True(s.Field.ParagraphLayoutFor(Number1).Lines[0].Line.TextRuns[0].Properties!.FontRenderingEmSize < plain.FontRenderingEmSize * 0.8);
            }
        });

        [Fact]
        public void Bullets_and_numbers_count_runs_of_paragraphs_and_use_the_before_and_after_text() => Headless.Run(() =>
        {
            Assert.Equal(4, BulletNumbers.Sequence(i => new ParaFormat { BulNumScheme = BulletNumbers.Arabic }, 3));
            Assert.Equal(2, BulletNumbers.Sequence(i => new ParaFormat { BulNumScheme = i == 2 ? 0 : BulletNumbers.Arabic }, 4));
            Assert.Equal(8, BulletNumbers.Sequence(i => new ParaFormat { BulNumScheme = BulletNumbers.Arabic, BulNumStartAt = i >= 2 ? 7 : (int?)null }, 3));
            Assert.Equal("(iv)", BulletNumbers.Label(new ParaFormat { BulNumScheme = BulletNumbers.RomanLower, BulNumTextBefore = "(", BulNumTextAfter = ")" }, 4));
            Assert.Equal("MCMXCIV", BulletNumbers.Number(BulletNumbers.RomanUpper, 1994));
            Assert.Equal("AA", BulletNumbers.Number(BulletNumbers.LetterUpper, 27));
            Assert.Equal("z", BulletNumbers.Number(BulletNumbers.LetterLower, 26));
            Assert.Equal("07", BulletNumbers.Number(BulletNumbers.Arabic01, 7));
            Assert.Equal("•", BulletNumbers.Label(new ParaFormat { BulNumScheme = 101 }, 0));
            Assert.Equal("", BulletNumbers.Label(new ParaFormat(), 1));

            using (var s = new Session(Scenarios.Paragraphs, 1100, 3000))
            {
                var pl = s.Field.ParagraphLayoutFor(Number3);
                Assert.Equal(BulletNumbers.Arabic, pl.Format.BulNumScheme);
                Assert.Equal(3, BulletNumbers.Sequence(i => s.Field.ParagraphLayoutFor(i).Format, Number3));
                Assert.Equal(1, BulletNumbers.Sequence(i => s.Field.ParagraphLayoutFor(i).Format, Roman1));
                Assert.Equal(1, BulletNumbers.Sequence(i => s.Field.ParagraphLayoutFor(i).Format, Letter1));
                Assert.True(BulletNumbers.IsBullet(s.Field.ParagraphLayoutFor(Bullet1).Format.BulNumScheme));
                Assert.Equal("3.", BulletNumbers.Label(pl.Format, 3));
                // List Number: leading 36, hanging 24: the label sits at 36, in the hanging space; every line of text starts at 60.
                Assert.Equal(60, pl.ContentLeft, 3);
                Assert.Equal(60, pl.Lines[0].X, 3);
                Assert.Equal(36, pl.LabelRect.X, 3);
            }
        });

        const int WideLabel = 30, ArabicWideLabel = 31;

        [Fact]
        public void A_label_wider_than_the_line_takes_the_first_line_and_the_text_starts_on_the_next_in_both_directions() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs, 240, 6000))
            {
                // A label much longer than the style's, wider than the line.
                foreach (int q in new[] { WideLabel, ArabicWideLabel })
                    s.Editor.Document.Paragraphs[q].Props.SetStr(TextPropertyNumbers.BulNumTxtBefore, "Chapter number and then some more words ");
                s.Field.RefreshFromDocument();
                Headless.Pump();
                var shot = new Shot(s);
                foreach (int p in new[] { WideLabel, ArabicWideLabel })
                {
                    var pl = s.Field.ParagraphLayoutFor(p);
                    var label = pl.LabelRect;
                    Assert.True(label.Width - 24 > pl.ContentWidth, "the label (less its hang) " + label.Width + " is wider than the line " + pl.ContentWidth);
                    var first = pl.Lines[0];
                    Assert.True(label.Bottom <= first.TextY + 0.01, "paragraph " + p + " (content " + pl.ContentWidth + "): the label " + label + " is above the first line of text, at " + first.TextY);
                    Assert.True(first.Y >= label.Bottom - 0.01, "the text's first line starts below the label's line");
                    // The text starts at the hanging indent, on the full text width.
                    if (pl.Rtl) Assert.Equal(pl.ContentLeft + pl.ContentWidth, Left(first) + first.Line.WidthIncludingTrailingWhitespace, 6);
                    else Assert.Equal(pl.ContentLeft, Left(first), 1);
                    Assert.True(first.Line.Length > 1, "more than one letter on the line");
                    // The label sits at the first indent, on the leading side.
                    if (pl.Rtl) Assert.Equal(pl.ContentLeft + pl.ContentWidth - pl.Format.FirstIndent, label.Right, 1);
                    else Assert.Equal(pl.ContentLeft + pl.Format.FirstIndent, label.X, 1);
                    // Pixels: the label's ink is in its own row band and the text's ink below it, none of it beside the label.
                    double top = s.Field.ParagraphTop(p);
                    Assert.NotEmpty(shot.InkColumns(label.X, label.Right, top + label.Y, top + label.Bottom));
                    Assert.NotEmpty(shot.InkColumns(0, shot.Width, top + first.TextY, top + first.TextY + first.Line.Height));
                    // Hit testing and the caret use the text line, not the label's.
                    Assert.Equal(first.TextY, pl.CaretRect(0).Y, 3);
                    Assert.Equal(0, pl.HitTest(new Point(pl.Rtl ? pl.ContentLeft + pl.ContentWidth - 2 : first.X + 2, label.Y + 1)));   // a click beside the label, at the leading edge, is the start of the text
                }
            }
        });

        [Fact]
        public void A_label_that_fits_the_line_stays_on_the_first_line_even_when_the_first_word_does_not_fit_beside_it() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs, 240, 6000))
            {
                var pl = s.Field.ParagraphLayoutFor(WideLabel);
                Assert.True(pl.LabelRect.Width - 24 < pl.ContentWidth, "the label (less its hang) " + pl.LabelRect.Width + " fits the line " + pl.ContentWidth);
                Assert.Equal(pl.Lines[0].TextY, pl.LabelRect.Y, 3);
            }
        });

        const int ExactTen = 32;

        [Fact]
        public void An_exact_line_height_keeps_the_baseline_at_the_same_fraction_of_the_slot_so_text_larger_than_the_slot_overflows_both_ways() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs, 400, 7000))
            {
                var pl = s.Field.ParagraphLayoutFor(ExactTen);
                Assert.True(pl.Lines.Count > 2);
                foreach (var li in pl.Lines) Assert.Equal(10, li.Height, 3);
                Assert.True(pl.Lines[0].Line.Height > 15, "the text is taller than its slot");
                // The baseline sits at the proportion of the slot that the font's ascent is of the text's height, so lines are one slot apart.
                double fraction = System.Math.Floor(pl.Lines[0].Line.Baseline / pl.Lines[0].Line.Height * 100) / 100;   // cut to a whole per cent
                foreach (var li in pl.Lines) Assert.Equal(li.Y + fraction * 10, li.TextY + li.Line.Baseline, 3);
                Assert.Equal(10, pl.Lines[1].TextY + pl.Lines[1].Line.Baseline - (pl.Lines[0].TextY + pl.Lines[0].Line.Baseline), 3);
                // The caret keeps the text's own height and sits on the text.
                var caret = pl.CaretRect(0);
                Assert.Equal(pl.Lines[0].TextY, caret.Y, 3);
                Assert.Equal(pl.Lines[0].Line.Height, caret.Height, 3);
                // Pixels: the letters (no descenders) end on the baseline.
                var shot = new Shot(s);
                double top = s.Field.ParagraphTop(ExactTen);
                var first = pl.Lines[0];
                int baselineRow = (int)Math.Round(top + first.TextY + first.Line.Baseline);
                int inkBottom = shot.InkBottom(new Rect(first.X, baselineRow - 12, 200, 14));   // rows up to just below the baseline: the next line's letters start 10 rows lower
                Assert.InRange(inkBottom, baselineRow - 2, baselineRow + 1);
                // Not bottom-aligned in the slot (where the baseline would sit by the descent above the slot's bottom).
                double bottomAligned = top + first.Y + first.Height - (first.Line.Height - first.Line.Baseline);
                Assert.True(top + first.TextY + first.Line.Baseline > bottomAligned + 1.5, "baseline " + (top + first.TextY + first.Line.Baseline) + " vs bottom aligned " + bottomAligned);
            }
        });

        const int ArabicTwoLines = 33;

        [Fact]
        public void Pixels_show_three_dots_after_the_last_visible_line_of_a_cut_off_paragraph_on_its_trailing_side() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs, 700, 8000))
            {
                var shot = new Shot(s);
                foreach (int p in new[] { TwoLines, ArabicTwoLines })
                {
                    var pl = s.Field.ParagraphLayoutFor(p);
                    Assert.True(pl.Truncated && pl.HasEllipsis, "paragraph " + p);
                    var dots = pl.EllipsisRect;
                    double top = s.Field.ParagraphTop(p);
                    // The dots sit on the baseline: ink in their box, in the rows just above it, in three separate groups of columns.
                    double baseline = top + pl.Lines[pl.Lines.Count - 1].TextY + pl.Lines[pl.Lines.Count - 1].Line.Baseline;
                    var columns = new List<int>();
                    for (int x = (int)dots.X; x < (int)System.Math.Ceiling(dots.Right); x++)
                        for (int y = (int)(baseline - 4); y <= (int)(baseline + 1); y++)
                            if (shot.IsInk(x, y, 215)) { columns.Add(x); break; }   // (a dot is small and blurred, so a paler threshold than for letters)
                    Assert.NotEmpty(columns);
                    int groups = 1 + columns.Zip(columns.Skip(1), (a, b) => b - a).Count(gap => gap > 1);
                    Assert.Equal(3, groups);
                    Assert.Empty(shot.InkColumns(dots.X, dots.Right, top + dots.Y, baseline - 6));   // nothing tall: they are dots
                    // Nothing is drawn past the text area on the trailing side.
                    if (pl.Rtl) Assert.Empty(shot.InkColumns(0, pl.ContentLeft - 1, top, top + pl.Height));
                    else Assert.Empty(shot.InkColumns(pl.ContentLeft + pl.ContentWidth + 1, shot.Width, top, top + pl.Height));
                }
                // A paragraph that is not cut off has no dots.
                Assert.False(s.Field.ParagraphLayoutFor(Plain).HasEllipsis);
            }
        });

        static double TextLeft(ParagraphLayout pl) => Left(pl.Lines[0]);
        static double TextRight(ParagraphLayout pl) => Left(pl.Lines[0]) + pl.Lines[0].Line.WidthIncludingTrailingWhitespace;

        [Fact]
        public void The_label_of_a_list_paragraph_ends_before_its_first_line_of_text_starts_in_left_to_right_and_right_to_left_lists() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs, 1100, 3000))
            {
                foreach (int p in new[] { Number1, Number1 + 1, Number3, Roman1, Letter1, Bullet1, Bullet1 + 1, Flush })
                {
                    var pl = s.Field.ParagraphLayoutFor(p);
                    var label = pl.LabelRect;
                    Assert.False(pl.Rtl);
                    Assert.True(label.Right + 3 <= TextLeft(pl) + 0.01, "paragraph " + p + ": label " + label + " text from " + TextLeft(pl));
                    Assert.True(label.X >= 0);
                }
                foreach (int p in new[] { ArabicNumber1, ArabicNumber2, ArabicBullet })
                {
                    var pl = s.Field.ParagraphLayoutFor(p);
                    var label = pl.LabelRect;
                    Assert.True(pl.Rtl);
                    Assert.True(label.X >= TextRight(pl) + 3 - 0.01, "paragraph " + p + ": label " + label + " text to " + TextRight(pl));
                    Assert.True(label.Right <= pl.LayoutWidth + 0.01);
                }
                // With room in the hanging indent the first line lines up with the others; with none, the text starts after the label.
                var hang = s.Field.ParagraphLayoutFor(Number1);
                Assert.Equal(hang.ContentLeft, TextLeft(hang), 1);
                Assert.Equal(hang.ContentLeft, Left(hang.Lines[1]), 1);
                var flush = s.Field.ParagraphLayoutFor(Flush);
                Assert.Equal(36, flush.LabelRect.X, 3);
                Assert.Equal(flush.LabelRect.Right + 3, TextLeft(flush), 1);
                Assert.Equal(36, Left(flush.Lines[1]), 1);
                var arabic = s.Field.ParagraphLayoutFor(ArabicNumber1);
                Assert.Equal(arabic.LayoutWidth - 36, arabic.LabelRect.Right, 3);
            }
        });

        [Fact]
        public void Pixels_show_the_label_clear_of_the_first_letter_in_left_to_right_and_right_to_left_lists() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs, 1100, 4500))
            {
                var shot = new Shot(s);
                Assert.True(s.Field.ParagraphTop(ArabicBullet) < shot.Height, "the arabic list is inside the rendered area");
                foreach (int p in new[] { Number3, Roman1, Letter1, Bullet1, Flush })
                {
                    var pl = s.Field.ParagraphLayoutFor(p);
                    double top = s.Field.ParagraphTop(p) + pl.Lines[0].TextY, bottom = top + pl.Lines[0].Line.Height;
                    var label = pl.LabelRect;
                    var labelInk = shot.InkColumns(label.X - 1, label.Right + 1, top, bottom);
                    var textInk = shot.InkColumns(TextLeft(pl) - 1, TextLeft(pl) + 60, top, bottom);
                    Assert.NotEmpty(labelInk);
                    Assert.NotEmpty(textInk);
                    Assert.True(labelInk.Max() < textInk.Min(), "paragraph " + p + ": label ink to " + labelInk.Max() + ", text ink from " + textInk.Min());
                    // The columns between the label's ink and the text's ink are empty, and nothing is left of the label.
                    Assert.Empty(shot.InkColumns(labelInk.Max() + 1, textInk.Min(), top, bottom));
                    Assert.Empty(shot.InkColumns(0, label.X - 1, top, bottom));
                }
                foreach (int p in new[] { ArabicNumber1, ArabicBullet })
                {
                    var pl = s.Field.ParagraphLayoutFor(p);
                    double top = s.Field.ParagraphTop(p) + pl.Lines[0].TextY, bottom = top + pl.Lines[0].Line.Height;
                    var label = pl.LabelRect;
                    var labelInk = shot.InkColumns(label.X - 1, label.Right + 1, top, bottom);
                    var textInk = shot.InkColumns(TextRight(pl) - 60, TextRight(pl) + 8, top, bottom);
                    Assert.NotEmpty(labelInk);
                    Assert.NotEmpty(textInk);
                    Assert.True(labelInk.Min() > textInk.Max(), "paragraph " + p + ": label ink from " + labelInk.Min() + ", text ink to " + textInk.Max());
                    Assert.Empty(shot.InkColumns(textInk.Max() + 1, labelInk.Min(), top, bottom));
                }
            }
        });

        [Fact]
        public void The_label_of_every_paragraph_is_its_place_in_the_run_and_Enter_in_a_list_renumbers_the_paragraphs_below() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs, 1100, 3000))
            {
                var count = s.Editor.Document.Paragraphs.Count;
                string[] before = new string[count];
                for (int i = 0; i < count; i++)
                {
                    var pl = s.Field.ParagraphLayoutFor(i);
                    int number = BulletNumbers.IsNumber(pl.Format.BulNumScheme) ? BulletNumbers.Sequence(k => s.Field.ParagraphLayoutFor(k).Format, i) : 0;
                    Assert.Equal(BulletNumbers.Label(pl.Format, number), pl.LabelText);
                    before[i] = pl.LabelText;
                }
                Assert.Equal(new[] { "1.", "2.", "3." }, new[] { before[Number1], before[Number1 + 1], before[Number3] });
                s.Caret(Number1, s.Editor.Document.Paragraphs[Number1].Length);
                s.Press(global::Avalonia.Input.Key.Enter);
                Assert.Equal(count + 1, s.Editor.Document.Paragraphs.Count);
                Assert.Equal(new[] { "1.", "2.", "3.", "4." }, new[] { s.Field.ParagraphLayoutFor(Number1).LabelText, s.Field.ParagraphLayoutFor(Number1 + 1).LabelText, s.Field.ParagraphLayoutFor(Number1 + 2).LabelText, s.Field.ParagraphLayoutFor(Number1 + 3).LabelText });
                Assert.Equal("(i)", s.Field.ParagraphLayoutFor(Roman1 + 1).LabelText);
            }
        });

        [Fact]
        public void The_paragraph_style_box_restyles_the_caret_paragraph_and_the_layout_follows() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs))
            {
                s.Caret(Plain, 3);
                var before = s.Field.ParagraphLayoutFor(Plain);
                int beforeLines = before.Lines.Count;
                Assert.Equal(0, before.Lines[0].X, 3);
                s.Find<ComboBox>(PlaygroundView.Names.ParaStyle).SelectedItem = "Block Quote";
                Headless.Pump();
                var after = s.Field.ParagraphLayoutFor(Plain);
                Assert.Equal(36, after.Lines[0].X, 3);
                Assert.True(after.Lines.Count >= beforeLines);
                s.MouseClick(Edge(s, Plain, 3));
                Assert.Equal(3, s.Editor.Caret.Offset);
            }
        });

        [Fact]
        public void Mouse_hits_and_vertical_movement_agree_with_indents_and_line_slots() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs, 1100, 3000))
            {
                foreach (int p in new[] { FirstLine, Hanging, Double, Exact, Centred, Right })
                {
                    var pl = s.Field.ParagraphLayoutFor(p);
                    foreach (int offset in new[] { 5, pl.Lines[1].Start + 3, pl.Paragraph.Length - 2 })
                    {
                        s.MouseClick(Edge(s, p, offset));
                        Assert.Equal(new TextPosition(p, offset), s.Editor.Caret);
                    }
                    s.Caret(p, pl.Lines[0].Start + 4);
                    s.Press(global::Avalonia.Input.Key.Down);
                    Assert.Equal(p, s.Editor.Caret.Paragraph);
                    Assert.Equal(1, pl.LineIndexOf(s.Editor.Caret.Offset));
                    s.Press(global::Avalonia.Input.Key.Up);
                    Assert.Equal(0, pl.LineIndexOf(s.Editor.Caret.Offset));
                }
            }
        });
    }
}
