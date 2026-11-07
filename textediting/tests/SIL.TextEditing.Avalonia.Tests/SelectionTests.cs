using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>Mouse and keyboard selection on the surface: caret placement and affinity, shift-click, drag, word and
    /// paragraph selection, word, line and document keys, soft wraps, and deleting across paragraphs. The mouse goes
    /// through the headless window's input pipeline, so the field's own pointer handlers run.</summary>
    public class SelectionTests
    {
        const string Latin = "alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu nu xi omicron pi rho sigma";
        const string Arabic = "مرحبا بالعالم كيف حالك اليوم هذا نص عربي طويل للاختبار والتحقق من الالتفاف بين الأسطر";

        static Paragraph Ltr(string text = Latin) => Para("en", "en", text);

        static Paragraph Rtl(string text = Arabic)
        {
            var p = Para("ar", "ar", text);
            p.Rtl = true;
            return p;
        }

        static (Window Window, RichTextField Field) Open(Document doc, double width = 230)
        {
            var (w, f) = Fixtures.InWindow(doc, width);
            Headless.Pump();
            return (w, f);
        }

        // ---- mouse helpers; the field fills the window, so window and field coordinates agree ----

        static void ResetClicks(Window w)
        {
            var far = new Point(w.Width + 400, w.Height + 400);
            w.MouseDown(far, MouseButton.Left);
            w.MouseUp(far, MouseButton.Left);
        }

        static void Click(Window w, Point p, int count = 1, RawInputModifiers mods = RawInputModifiers.None)
        {
            for (int i = 0; i < count; i++)
            {
                w.MouseDown(p, MouseButton.Left, mods);
                w.MouseUp(p, MouseButton.Left, mods);
            }
            Headless.Pump();
        }

        static void Drag(Window w, Point from, Point to, int clicksBefore = 0)
        {
            for (int i = 0; i < clicksBefore; i++) { w.MouseDown(from, MouseButton.Left); w.MouseUp(from, MouseButton.Left); }
            w.MouseDown(from, MouseButton.Left);
            w.MouseMove(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2), RawInputModifiers.LeftMouseButton);
            w.MouseMove(to, RawInputModifiers.LeftMouseButton);
            w.MouseUp(to, MouseButton.Left);
            Headless.Pump();
        }

        /// <summary>Where the character at a model index is drawn: its bounds, with the paragraph and line offsets applied.</summary>
        static Rect CharRect(RichTextField f, int para, int index)
        {
            var pl = f.ParagraphLayoutFor(para);
            var li = pl.Lines[pl.LineIndexOf(index)];
            // Leading and trailing edge of the one-character cluster (GetTextBounds is unreliable for one character in RTL).
            double x0 = li.Line.GetDistanceFromCharacterHit(new CharacterHit(index)), x1 = li.Line.GetDistanceFromCharacterHit(new CharacterHit(index, 1));
            return new Rect(Math.Min(x0, x1), f.ParagraphTop(para) + li.Y, Math.Abs(x1 - x0), li.Line.Height);
        }

        static Point Center(RichTextField f, int para, int index) { var r = CharRect(f, para, index); return new Point(r.X + r.Width / 2, r.Y + r.Height / 2); }
        static Point Quarter(RichTextField f, int para, int index, bool visualLeft) { var r = CharRect(f, para, index); return new Point(r.X + r.Width * (visualLeft ? 0.25 : 0.75), r.Y + r.Height / 2); }

        static string Selected(RichTextField f) => string.Join("\n", f.Editor.HasSelection ? f.Editor.GetFragment().Select(p => p.Text) : Enumerable.Empty<string>());
        static string Pos(RichTextField f) => f.Editor.Anchor + "->" + f.Editor.Caret;

        static void AssertWrapped(RichTextField f, int para, int lines)
        {
            Assert.True(f.ParagraphLayoutFor(para).Lines.Count >= lines, "paragraph " + para + " has " + f.ParagraphLayoutFor(para).Lines.Count + " lines");
        }

        // ---------------------------------------------------------------- click

        [Fact]
        public void Click_places_the_caret_on_the_nearer_side_of_a_character_in_left_to_right_text() => Headless.Run(() =>
        {
            var (w, f) = Open(Doc(Ltr("hello world")), 400);
            ResetClicks(w);
            Click(w, Quarter(f, 0, 6, true));
            Assert.Equal(new TextPosition(0, 6), f.Editor.Caret);
            Assert.False(f.Editor.HasSelection);
            ResetClicks(w);
            Click(w, Quarter(f, 0, 6, false));
            Assert.Equal(new TextPosition(0, 7), f.Editor.Caret);
            w.Close();
        });

        [Fact]
        public void Click_places_the_caret_on_the_nearer_side_of_a_character_in_right_to_left_text() => Headless.Run(() =>
        {
            var (w, f) = Open(Doc(Rtl("مرحبا بالعالم")), 400);
            ResetClicks(w);
            Click(w, Quarter(f, 0, 2, false)); // visual right half of an RTL character is its leading edge
            Assert.Equal(new TextPosition(0, 2), f.Editor.Caret);
            ResetClicks(w);
            Click(w, Quarter(f, 0, 2, true));
            Assert.Equal(new TextPosition(0, 3), f.Editor.Caret);
            w.Close();
        });

        [Fact]
        public void Click_at_a_bidi_run_boundary_keeps_the_side_that_was_clicked() => Headless.Run(() =>
        {
            // "ab عربي cd": offset 3 is drawn twice, at the right end of the Arabic block (leading edge of its first
            // letter) and at the left end (trailing edge of the space). The click chooses which.
            var (w, f) = Open(Doc(Ltr("ab عربي cd")), 400);
            double rightEnd = CharRect(f, 0, 3).Right, leftEnd = CharRect(f, 0, 6).X;
            Assert.True(rightEnd > leftEnd + 5);
            double y = CharRect(f, 0, 3).Y + 8;

            ResetClicks(w);
            Click(w, new Point(rightEnd - 2, y));          // right half of the first Arabic letter
            Assert.Equal(new TextPosition(0, 3), f.Editor.Caret);
            Assert.True(Math.Abs(f.CaretRect().X - rightEnd) < 2, "caret x " + f.CaretRect().X + " expected " + rightEnd);

            ResetClicks(w);
            Click(w, new Point(leftEnd - 2, y));           // just left of the Arabic block: end of the space
            Assert.Equal(new TextPosition(0, 3), f.Editor.Caret);
            Assert.True(Math.Abs(f.CaretRect().X - leftEnd) < 2, "caret x " + f.CaretRect().X + " expected " + leftEnd);
            w.Close();
        });

        [Fact]
        public void Click_after_the_last_character_of_a_wrapped_line_leaves_the_caret_on_that_line() => Headless.Run(() =>
        {
            var (w, f) = Open(Doc(Ltr()));
            AssertWrapped(f, 0, 3);
            var pl = f.ParagraphLayoutFor(0);
            var l0 = pl.Lines[0];
            ResetClicks(w);
            Click(w, new Point(w.Width - 1, l0.Y + l0.Line.Height / 2));
            Assert.Equal(pl.ToModel(pl.TextEnd(l0)), f.Editor.Caret.Offset);
            Assert.Equal(pl.Lines[1].Start, f.Editor.Caret.Offset);
            Assert.True(f.CaretRect().Y < pl.Lines[1].Y - 1, "caret y " + f.CaretRect().Y + " line 2 y " + pl.Lines[1].Y);
            w.Close();
        });

        [Fact]
        public void Shift_click_extends_the_selection_from_the_anchor() => Headless.Run(() =>
        {
            var (w, f) = Open(Doc(Ltr("one two three four")), 400);
            ResetClicks(w);
            Click(w, Quarter(f, 0, 4, true));
            ResetClicks(w);
            Click(w, Quarter(f, 0, 12, true), 1, RawInputModifiers.Shift);
            Assert.Equal("0:4->0:12", Pos(f));
            Assert.Equal("two thre", Selected(f));
            ResetClicks(w);
            Click(w, Quarter(f, 0, 1, true), 1, RawInputModifiers.Shift);
            Assert.Equal("0:4->0:1", Pos(f));
            w.Close();
        });

        // ---------------------------------------------------------------- drag

        [Fact]
        public void Drag_selects_within_a_paragraph_in_either_direction() => Headless.Run(() =>
        {
            var (w, f) = Open(Doc(Ltr("one two three four")), 400);
            ResetClicks(w);
            Drag(w, Quarter(f, 0, 4, true), Quarter(f, 0, 9, true));
            Assert.Equal("two t", Selected(f));
            ResetClicks(w);
            Drag(w, Quarter(f, 0, 9, true), Quarter(f, 0, 4, true));
            Assert.Equal("0:9->0:4", Pos(f));
            Assert.Equal("two t", Selected(f));
            w.Close();
        });

        [Theory]
        [InlineData("ltr")]
        [InlineData("rtl")]
        public void Drag_selects_across_paragraphs(string dir) => Headless.Run(() =>
        {
            var make = dir == "ltr" ? (Func<string, Paragraph>)(t => Ltr(t)) : t => Rtl(t);
            string a = dir == "ltr" ? "first paragraph here" : "مرحبا بالعالم كيف حالك", b = dir == "ltr" ? "second one" : "هذا نص عربي", c = dir == "ltr" ? "third paragraph" : "للاختبار والتحقق";
            var (w, f) = Open(Doc(make(a), make(b), make(c)), 600);
            ResetClicks(w);
            // From the visual middle of paragraph 0 down to the middle of paragraph 2.
            Drag(w, Center(f, 0, 7), Center(f, 2, 5));
            Assert.True(f.Editor.HasSelection);
            Assert.Equal(0, f.Editor.Anchor.Paragraph);
            Assert.Equal(2, f.Editor.Caret.Paragraph);
            var sel = Selected(f).Split('\n');
            Assert.Equal(3, sel.Length);
            Assert.Equal(b, sel[1]);
            Assert.True(sel[0].Length > 3 && sel[0].Length < a.Length, sel[0]);
            Assert.EndsWith(sel[0], a);
            Assert.StartsWith(sel[2], c);
            Assert.True(sel[2].Length > 0);
            // Dragging upward reverses anchor and caret.
            ResetClicks(w);
            Drag(w, Center(f, 2, 5), Center(f, 0, 7));
            Assert.Equal(2, f.Editor.Anchor.Paragraph);
            Assert.Equal(0, f.Editor.Caret.Paragraph);
            Assert.Equal(3, Selected(f).Split('\n').Length);
            w.Close();
        });

        [Fact]
        public void Drag_past_the_ends_of_the_field_selects_to_the_document_edges() => Headless.Run(() =>
        {
            var (w, f) = Open(Doc(Ltr("first"), Ltr("second")), 400);
            ResetClicks(w);
            Drag(w, Center(f, 0, 2), new Point(300, 390));
            Assert.Equal(new TextPosition(1, 6), f.Editor.Caret);
            ResetClicks(w);
            Drag(w, Center(f, 1, 2), new Point(-5, -20));
            Assert.Equal(new TextPosition(0, 0), f.Editor.Caret);
            w.Close();
        });

        // ---------------------------------------------------------------- double and triple click

        [Fact]
        public void Double_click_selects_the_word_white_space_or_punctuation_under_the_pointer() => Headless.Run(() =>
        {
            var (w, f) = Open(Doc(Ltr("It's a beta, ok")), 400);
            ResetClicks(w);
            Click(w, Center(f, 0, 1), 2);
            Assert.Equal("It's", Selected(f));      // the apostrophe between letters stays in the word
            ResetClicks(w);
            Click(w, Center(f, 0, 9), 2);
            Assert.Equal("beta", Selected(f));
            ResetClicks(w);
            Click(w, Center(f, 0, 12), 2);
            Assert.Equal(" ", Selected(f));
            ResetClicks(w);
            Click(w, Center(f, 0, 11), 2);
            Assert.Equal(",", Selected(f));
            w.Close();
        });

        [Fact]
        public void Double_click_on_the_empty_last_line_after_a_hard_break_selects_nothing_and_keeps_the_break() => Headless.Run(() =>
        {
            var (w, f) = Open(Doc(Ltr("ab\u2028")), 400);
            ResetClicks(w);
            var pl = f.ParagraphLayoutFor(0);
            Assert.Equal(2, pl.Lines.Count);
            Click(w, new Point(10, pl.Lines[1].Y + pl.Lines[1].Height / 2), 2);
            Assert.False(f.Editor.HasSelection, Pos(f));
            Assert.Equal(new TextPosition(0, 3), f.Editor.Caret);
            Assert.Equal("ab\u2028", f.Editor.Document.Paragraphs[0].Text);
            w.Close();
        });

        [Fact]
        public void Double_click_on_the_right_half_of_a_words_last_letter_still_selects_that_word() => Headless.Run(() =>
        {
            var (w, f) = Open(Doc(Ltr("one two three")), 400);
            ResetClicks(w);
            Click(w, Quarter(f, 0, 6, false), 2);
            Assert.Equal("two", Selected(f));
            w.Close();
        });

        [Fact]
        public void Double_click_selects_an_arabic_word_in_a_right_to_left_paragraph() => Headless.Run(() =>
        {
            var (w, f) = Open(Doc(Rtl("مرحبا بالعالم كيف")), 400);
            ResetClicks(w);
            Click(w, Center(f, 0, 8), 2);
            Assert.Equal("بالعالم", Selected(f));
            Assert.Equal("0:6->0:13", Pos(f));
            w.Close();
        });

        [Fact]
        public void Double_click_in_a_wrapped_paragraph_selects_the_word_on_the_clicked_line() => Headless.Run(() =>
        {
            var (w, f) = Open(Doc(Ltr()));
            AssertWrapped(f, 0, 3);
            var pl = f.ParagraphLayoutFor(0);
            int idx = pl.Lines[2].Start + 1;
            WordBreaks.WordAt(Latin, idx, out int s, out int e);
            ResetClicks(w);
            Click(w, Center(f, 0, idx), 2);
            Assert.Equal(Latin.Substring(s, e - s), Selected(f));
            w.Close();
        });

        [Fact]
        public void Double_click_then_drag_extends_the_selection_by_whole_words() => Headless.Run(() =>
        {
            var (w, f) = Open(Doc(Ltr("one two three four five")), 500);
            ResetClicks(w);
            // press, release, press again (second press is the double click), move, release.
            Drag(w, Center(f, 0, 5), Center(f, 0, 15), 1);
            Assert.Equal("two three four", Selected(f));
            ResetClicks(w);
            Drag(w, Center(f, 0, 9), Center(f, 0, 1), 1);
            Assert.Equal("one two three", Selected(f));
            w.Close();
        });

        [Theory]
        [InlineData("ltr")]
        [InlineData("rtl")]
        public void Triple_click_selects_the_whole_paragraph(string dir) => Headless.Run(() =>
        {
            var make = dir == "ltr" ? (Func<string, Paragraph>)(t => Ltr(t)) : t => Rtl(t);
            string mid = dir == "ltr" ? "the middle paragraph wraps onto more lines than one" : "هذه الفقرة الوسطى تلتف على أكثر من سطر واحد في هذا الاختبار";
            var (w, f) = Open(Doc(make("first"), make(mid), make("last")), 200);
            AssertWrapped(f, 1, 2);
            ResetClicks(w);
            Click(w, Center(f, 1, 6), 3);
            Assert.Equal(mid, Selected(f));
            Assert.Equal("1:0->1:" + mid.Length, Pos(f));
            // Dragging after a triple click extends by paragraphs.
            ResetClicks(w);
            Drag(w, Center(f, 1, 6), Center(f, 2, 1), 2);
            Assert.Equal(mid + "\nlast", Selected(f));
            w.Close();
        });

        // ---------------------------------------------------------------- keyboard

        [Theory]
        [InlineData("ltr")]
        [InlineData("rtl")]
        public void Shift_arrows_extend_and_arrows_collapse(string dir) => Headless.Run(() =>
        {
            var p = dir == "ltr" ? Ltr("abcdef ghi") : Rtl("مرحبا بالعالم");
            var f = Fixtures.Field(Doc(p), 400);
            f.Editor.SetCaret(new TextPosition(0, 3));
            // Arrows are logical (ADR 0018): Right is forward in LTR, backward in RTL.
            f.Press(Key.Right, KeyModifiers.Shift);
            f.Press(Key.Right, KeyModifiers.Shift);
            int expected = dir == "ltr" ? 5 : 1;
            Assert.Equal(new TextPosition(0, 3), f.Editor.Anchor);
            Assert.Equal(new TextPosition(0, expected), f.Editor.Caret);
            f.Press(Key.Left, KeyModifiers.Shift);
            Assert.Equal(new TextPosition(0, dir == "ltr" ? 4 : 2), f.Editor.Caret);
            Assert.True(f.Editor.HasSelection);
            f.Press(Key.Left);
            Assert.False(f.Editor.HasSelection);
        });

        [Fact]
        public void Ctrl_arrows_jump_by_word_and_cross_paragraphs() => Headless.Run(() =>
        {
            var f = Fixtures.Field(Doc(Ltr("one  two, three"), Ltr("four")), 400);
            f.Editor.SetCaret(new TextPosition(0, 0));
            int[] forward = { 5, 8, 10, 15 };
            foreach (var o in forward) { f.Press(Key.Right, KeyModifiers.Control); Assert.Equal(new TextPosition(0, o), f.Editor.Caret); }
            f.Press(Key.Right, KeyModifiers.Control);
            Assert.Equal(new TextPosition(1, 0), f.Editor.Caret);
            f.Press(Key.Left, KeyModifiers.Control);
            Assert.Equal(new TextPosition(0, 15), f.Editor.Caret);
            f.Press(Key.Left, KeyModifiers.Control);
            Assert.Equal(new TextPosition(0, 10), f.Editor.Caret);
            f.Press(Key.Left, KeyModifiers.Control | KeyModifiers.Shift);
            Assert.Equal(new TextPosition(0, 8), f.Editor.Caret);
            Assert.Equal(new TextPosition(0, 10), f.Editor.Anchor);
            Assert.Equal(", ", Selected(f));
        });

        [Fact]
        public void Ctrl_arrows_in_a_right_to_left_paragraph_follow_the_visual_direction() => Headless.Run(() =>
        {
            const string t = "مرحبا بالعالم كيف";
            var f = Fixtures.Field(Doc(Rtl(t)), 400);
            f.Editor.SetCaret(new TextPosition(0, 0));
            // Ctrl+Left moves to the next word (to the left on screen); Ctrl+Right goes back.
            f.Press(Key.Left, KeyModifiers.Control);
            Assert.Equal(new TextPosition(0, 6), f.Editor.Caret);
            f.Press(Key.Left, KeyModifiers.Control);
            Assert.Equal(new TextPosition(0, 14), f.Editor.Caret);
            f.Press(Key.Right, KeyModifiers.Control);
            Assert.Equal(new TextPosition(0, 6), f.Editor.Caret);
            f.Press(Key.Left, KeyModifiers.Control | KeyModifiers.Shift);
            Assert.Equal("بالعالم ", Selected(f));
        });

        [Fact]
        public void Home_and_end_go_to_the_ends_of_the_visual_line_and_ctrl_home_end_to_the_document() => Headless.Run(() =>
        {
            var f = Fixtures.Field(Doc(Ltr("first"), Ltr(), Ltr("last")), 230);
            var pl = f.ParagraphLayoutFor(1);
            Assert.True(pl.Lines.Count >= 3);
            f.Editor.SetCaret(new TextPosition(1, pl.Lines[1].Start + 2));
            f.Press(Key.Home);
            Assert.Equal(new TextPosition(1, pl.Lines[1].Start), f.Editor.Caret);
            f.Press(Key.End);
            Assert.Equal(new TextPosition(1, pl.TextEnd(pl.Lines[1])), f.Editor.Caret);
            f.Press(Key.End, KeyModifiers.Shift);
            f.Press(Key.Home, KeyModifiers.Shift);
            Assert.Equal(new TextPosition(1, pl.TextEnd(pl.Lines[1])), f.Editor.Anchor);
            Assert.Equal(new TextPosition(1, pl.Lines[1].Start), f.Editor.Caret);
            f.Press(Key.Home, KeyModifiers.Control);
            Assert.Equal(new TextPosition(0, 0), f.Editor.Caret);
            Assert.False(f.Editor.HasSelection);
            f.Press(Key.End, KeyModifiers.Control);
            Assert.Equal(new TextPosition(2, 4), f.Editor.Caret);
            f.Press(Key.Home, KeyModifiers.Control | KeyModifiers.Shift);
            Assert.Equal("0:0", f.Editor.Caret.ToString());
            Assert.Equal("2:4", f.Editor.Anchor.ToString());
        });

        [Fact]
        public void Home_and_end_in_a_right_to_left_paragraph_use_the_logical_line_ends() => Headless.Run(() =>
        {
            var f = Fixtures.Field(Doc(Rtl()), 230);
            var pl = f.ParagraphLayoutFor(0);
            Assert.True(pl.Lines.Count >= 3);
            f.Editor.SetCaret(new TextPosition(0, pl.Lines[1].Start + 3));
            f.Press(Key.Home);
            Assert.Equal(pl.Lines[1].Start, f.Editor.Caret.Offset);
            Assert.True(f.CaretRect().X > 100, "line start is at the right: " + f.CaretRect().X);
            f.Press(Key.End);
            Assert.Equal(pl.TextEnd(pl.Lines[1]), f.Editor.Caret.Offset);
            Assert.True(f.CaretRect().X < 100, "line end is at the left: " + f.CaretRect().X);
            Assert.True(f.CaretRect().Y < pl.Lines[2].Y - 1);
        });

        [Fact]
        public void Ctrl_a_selects_everything_across_paragraphs() => Headless.Run(() =>
        {
            var f = Fixtures.Field(Doc(Ltr("one"), Rtl("مرحبا"), Ltr("three")), 400);
            f.Editor.SetCaret(new TextPosition(1, 2));
            f.Press(Key.A, KeyModifiers.Control);
            Assert.Equal("0:0->2:5", Pos(f));
            Assert.Equal("one\nمرحبا\nthree", Selected(f));
        });

        // ---------------------------------------------------------------- soft wraps and vertical movement

        [Theory]
        [InlineData("ltr")]
        [InlineData("rtl")]
        public void End_at_a_soft_wrap_stays_at_the_end_of_the_line_and_up_and_down_respect_it(string dir) => Headless.Run(() =>
        {
            var f = Fixtures.Field(Doc(dir == "ltr" ? Ltr() : Rtl()), 230);
            var pl = f.ParagraphLayoutFor(0);
            Assert.True(pl.Lines.Count >= 3);
            f.Editor.SetCaret(new TextPosition(0, pl.Lines[1].Start + 1));
            f.Press(Key.End);
            int wrapOffset = pl.Lines[2].Start;
            Assert.Equal(wrapOffset, f.Editor.Caret.Offset);                 // the same offset as the next line's start...
            Assert.True(Math.Abs(f.CaretRect().Y - pl.Lines[1].Y) < 1, "...but drawn on line 2: " + f.CaretRect().Y);
            double x = f.CaretRect().X;

            f.Press(Key.Up);                                                // up from the end of line 2 goes to line 1
            Assert.True(Math.Abs(f.CaretRect().Y - pl.Lines[0].Y) < 1, "up lands on line 1: " + f.CaretRect().Y);
            Assert.True(f.Editor.Caret.Offset <= pl.Lines[1].Start);

            f.Press(Key.Down);
            Assert.True(Math.Abs(f.CaretRect().Y - pl.Lines[1].Y) < 1, "down lands on line 2: " + f.CaretRect().Y);
            f.Press(Key.Down);                                              // and on to line 3, at the same x
            Assert.True(Math.Abs(f.CaretRect().Y - pl.Lines[2].Y) < 1, "down lands on line 3: " + f.CaretRect().Y);
            double nearest = pl.VisualStops(pl.Lines[2]).Select(st => st.X).OrderBy(sx => Math.Abs(sx - x)).First();
            Assert.True(Math.Abs(f.CaretRect().X - nearest) < 1, "x " + f.CaretRect().X + " nearest stop to " + x + " is " + nearest);

            // Home on the next line puts the caret at the same offset but on that line.
            f.Press(Key.Home);
            Assert.Equal(pl.Lines[2].Start, f.Editor.Caret.Offset);
            Assert.True(Math.Abs(f.CaretRect().Y - pl.Lines[2].Y) < 1);
            f.Press(dir == "ltr" ? Key.Left : Key.Right);                   // backwards from the start of a line: to the end of the previous line
            Assert.True(Math.Abs(f.CaretRect().Y - pl.Lines[1].Y) < 1, "left goes back to line 2: " + f.CaretRect().Y);
        });

        [Fact]
        public void Shift_end_at_a_soft_wrap_selects_to_the_line_end_not_into_the_next_line() => Headless.Run(() =>
        {
            var f = Fixtures.Field(Doc(Ltr()), 230);
            var pl = f.ParagraphLayoutFor(0);
            f.Editor.SetCaret(new TextPosition(0, 0));
            f.Press(Key.End, KeyModifiers.Shift);
            Assert.True(Math.Abs(f.CaretRect().Y - pl.Lines[0].Y) < 1);
            Assert.Equal(pl.Lines[1].Start, f.Editor.Caret.Offset);
            var rects = pl.SelectionRects(0, f.Editor.Caret.Offset);
            Assert.True(rects.All(r => r.Y < pl.Lines[1].Y - 1));
        });

        [Fact]
        public void Up_and_down_keep_the_preferred_x_across_a_short_line_and_paragraphs() => Headless.Run(() =>
        {
            var f = Fixtures.Field(Doc(Ltr("wwwwwwwwwwwwwwww"), Ltr("ab"), Ltr("wwwwwwwwwwwwwwww")), 600);
            f.Editor.SetCaret(new TextPosition(0, 12));
            double x = f.CaretRect().X;
            f.Press(Key.Down);
            Assert.Equal(new TextPosition(1, 2), f.Editor.Caret);          // clamped to the short line
            f.Press(Key.Down);
            Assert.Equal(2, f.Editor.Caret.Paragraph);
            Assert.True(Math.Abs(f.CaretRect().X - x) < 8, "x " + f.CaretRect().X + " vs " + x);
            f.Press(Key.Up);
            f.Press(Key.Up);
            Assert.Equal(0, f.Editor.Caret.Paragraph);
            Assert.True(Math.Abs(f.CaretRect().X - x) < 8);
            f.Press(Key.Up);                                                // first line: to the paragraph start
            Assert.Equal(new TextPosition(0, 0), f.Editor.Caret);
            f.Press(Key.Down, KeyModifiers.Shift);
            Assert.Equal(new TextPosition(0, 0), f.Editor.Anchor);
            Assert.Equal(1, f.Editor.Caret.Paragraph);
        });

        [Fact]
        public void Typing_clears_the_preferred_x() => Headless.Run(() =>
        {
            var f = Fixtures.Field(Doc(Ltr("wwwwwwwwwwwwwwww"), Ltr("ab"), Ltr("wwwwwwwwwwwwwwww")), 600);
            f.Editor.SetCaret(new TextPosition(0, 12));
            f.Press(Key.Down);
            f.Type("x");
            f.Press(Key.Down);
            Assert.Equal(2, f.Editor.Caret.Paragraph);
            Assert.True(f.Editor.Caret.Offset <= 4, "goal x should have been forgotten: " + f.Editor.Caret);
        });

        // ---------------------------------------------------------------- cross-paragraph selection and deletion

        [Fact]
        public void Deleting_across_paragraphs_keeps_the_identity_the_paragraph_rules_name() => Headless.Run(() =>
        {
            // Both sides keep text: the first paragraph's identity survives.
            var doc = Doc(Ltr("first paragraph"), Ltr("second paragraph"), Ltr("third paragraph"));
            var ids = doc.Paragraphs.Select(p => p.Key).ToList();
            var (w, f) = Open(doc, 600);
            ResetClicks(w);
            Drag(w, Quarter(f, 0, 5, true), Quarter(f, 2, 6, true));
            f.Press(Key.Back);
            Headless.Pump();
            Assert.Single(doc.Paragraphs);
            Assert.Equal("firstparagraph", doc.Paragraphs[0].Text);
            Assert.Equal(ids[0], doc.Paragraphs[0].Key);
            Assert.Equal(new TextPosition(0, 5), f.Editor.Caret);
            w.Close();

            // The head is empty and the tail is not: the last paragraph's identity survives.
            var doc2 = Doc(Ltr("first paragraph"), Ltr("second paragraph"));
            var ids2 = doc2.Paragraphs.Select(p => p.Key).ToList();
            var (w2, f2) = Open(doc2, 600);
            ResetClicks(w2);
            Drag(w2, new Point(0, 2), Quarter(f2, 1, 7, true));
            Assert.Equal(new TextPosition(0, 0), f2.Editor.Anchor);
            f2.Press(Key.Delete);
            Headless.Pump();
            Assert.Single(doc2.Paragraphs);
            Assert.Equal("paragraph", doc2.Paragraphs[0].Text);
            Assert.Equal(ids2[1], doc2.Paragraphs[0].Key);
            w2.Close();
        });

        [Fact]
        public void Typing_over_a_cross_paragraph_selection_replaces_it_in_one_paragraph() => Headless.Run(() =>
        {
            var doc = Doc(Ltr("alpha beta"), Rtl("مرحبا بالعالم"), Ltr("gamma delta"));
            var (w, f) = Open(doc, 600);
            f.Editor.SetCaret(new TextPosition(0, 6));
            f.Editor.SetCaret(new TextPosition(2, 6), true);
            f.Type("X");
            Assert.Single(doc.Paragraphs);
            Assert.Equal("alpha Xdelta", doc.Paragraphs[0].Text);
            w.Close();
        });

        // ---------------------------------------------------------------- review additions

        [Fact]
        public void Right_click_outside_the_selection_moves_the_caret_and_inside_it_keeps_the_selection() => Headless.Run(() =>
        {
            var (w, f) = Open(Doc(Ltr("one two three four")), 400);
            ResetClicks(w);
            Drag(w, Quarter(f, 0, 4, true), Quarter(f, 0, 9, true));
            Assert.Equal("0:4->0:9", Pos(f));
            ResetClicks(w);
            w.MouseDown(Center(f, 0, 6), MouseButton.Right);
            w.MouseUp(Center(f, 0, 6), MouseButton.Right);
            Assert.Equal("0:4->0:9", Pos(f));
            ResetClicks(w);
            w.MouseDown(Quarter(f, 0, 14, true), MouseButton.Right);
            w.MouseUp(Quarter(f, 0, 14, true), MouseButton.Right);
            Assert.Equal("0:14->0:14", Pos(f));
            Assert.False(f.Editor.HasSelection);
            w.Close();
        });

        [Fact]
        public void A_paragraph_that_is_only_an_inline_object_can_be_clicked_selected_and_walked() => Headless.Run(() =>
        {
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(Run.Object("en", "\u0008pic"));
            var (w, f) = Open(Doc(Ltr("before"), p, Ltr("after")), 400);
            ResetClicks(w);
            Click(w, Center(f, 1, 0), 2);
            Assert.Equal(1, Selected(f).Length);
            ResetClicks(w);
            Click(w, Center(f, 1, 0), 3);
            Assert.Equal("1:0->1:1", Pos(f));
            f.Press(Key.Home);
            Assert.Equal(new TextPosition(1, 0), f.Editor.Caret);
            f.Press(Key.End);
            Assert.Equal(new TextPosition(1, 1), f.Editor.Caret);
            f.Press(Key.Down);
            Assert.Equal(2, f.Editor.Caret.Paragraph);
            f.Press(Key.Up);
            Assert.Equal(1, f.Editor.Caret.Paragraph);
            f.Editor.SetCaret(new TextPosition(1, 1));
            f.Press(Key.Left, KeyModifiers.Control);
            Assert.Equal(new TextPosition(1, 0), f.Editor.Caret);
            f.Press(Key.Left, KeyModifiers.Control);
            Assert.Equal(new TextPosition(0, 6), f.Editor.Caret);
            w.Close();
        });

        [Fact]
        public void A_wrapped_line_made_only_of_trailing_spaces_keeps_End_on_that_line() => Headless.Run(() =>
        {
            string text = "alpha" + new string(' ', 60) + "beta";
            var f = Fixtures.Field(Doc(Ltr(text)), 150);
            var pl = f.ParagraphLayoutFor(0);
            Assert.True(pl.Lines.Count >= 2, "lines " + pl.Lines.Count);
            for (int i = 0; i < pl.Lines.Count - 1; i++)
            {
                f.Editor.SetCaret(new TextPosition(0, pl.Lines[i].Start));
                f.Press(Key.End);
                // The caret goes after the spaces (the end of the line) and is drawn on that line.
                Assert.Equal(pl.Lines[i + 1].Start, f.Editor.Caret.Offset);
                Assert.True(Math.Abs(f.CaretRect().Y - pl.Lines[i].Y) < 1, "line " + i + " caret y " + f.CaretRect().Y);
                f.Press(Key.Home);
                Assert.Equal(pl.Lines[i].Start, f.Editor.Caret.Offset);
            }
            f.Editor.SetCaret(new TextPosition(0, 0));
            for (int k = 0; k < 6; k++) f.Press(Key.Down);
            Assert.Equal(pl.Lines.Count - 1, pl.LineIndexOf(f.Editor.Caret.Offset, null));
        });

        [Fact]
        public void Double_click_then_drag_backwards_anchors_at_the_end_of_the_first_word() => Headless.Run(() =>
        {
            var (w, f) = Open(Doc(Ltr("one two three four five")), 500);
            ResetClicks(w);
            Drag(w, Center(f, 0, 9), Center(f, 0, 1), 1);
            Assert.Equal("0:13->0:0", Pos(f));
            w.Close();
        });

        [Fact]
        public void Shift_double_click_extends_by_characters_from_the_anchor() => Headless.Run(() =>
        {
            var (w, f) = Open(Doc(Ltr("one two three four")), 400);
            ResetClicks(w);
            Click(w, Quarter(f, 0, 4, true));
            ResetClicks(w);
            Click(w, Quarter(f, 0, 10, true), 2, RawInputModifiers.Shift);
            Assert.Equal("0:4->0:10", Pos(f));
            w.Close();
        });

        [Fact]
        public void A_programmatic_caret_move_to_the_same_offset_forgets_the_wrap_affinity() => Headless.Run(() =>
        {
            var f = Fixtures.Field(Doc(Ltr()), 230);
            var pl = f.ParagraphLayoutFor(0);
            f.Editor.SetCaret(new TextPosition(0, pl.Lines[1].Start + 1));
            f.Press(Key.End);
            Assert.True(Math.Abs(f.CaretRect().Y - pl.Lines[1].Y) < 1);
            f.Editor.SetCaret(f.Editor.Caret);                      // host or undo puts the caret at the same offset
            Assert.True(Math.Abs(f.CaretRect().Y - pl.Lines[2].Y) < 1, "caret y " + f.CaretRect().Y);
        });
    }
}
