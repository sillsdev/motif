using System.Collections.Generic;
using Avalonia;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Xunit;

namespace SIL.TextEditing.Playground.Tests
{
    /// <summary>The character drawing properties of the "Drawing properties" scenario, read back from the text runs the field's layout
    /// actually holds (the font, size, brushes and decorations Avalonia was given).</summary>
    public class DrawingCharacterTests
    {
        internal static TextRunProperties RunWith(Session s, int paragraph, string token)
        {
            var pl = s.Field.ParagraphLayoutFor(paragraph);
            foreach (var li in pl.Lines)
                foreach (var run in li.Line.TextRuns)
                    if (run.Text.Span.ToString().Contains(token)) return run.Properties!;
            throw new System.InvalidOperationException("No text run containing \"" + token + "\" in paragraph " + paragraph);
        }

        static Color ColorOf(IBrush? brush) => ((ISolidColorBrush)brush!).Color;

        [Fact]
        public void Bold_weights_italic_and_style_driven_weights_reach_the_typeface() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Drawing))
            {
                Assert.Equal(FontWeight.Bold, RunWith(s, 1, "bold").Typeface.Weight);
                Assert.Equal(FontWeight.Light, RunWith(s, 1, "weight 300").Typeface.Weight);
                Assert.Equal(FontWeight.Black, RunWith(s, 1, "weight 900").Typeface.Weight);
                Assert.Equal(FontStyle.Italic, RunWith(s, 1, "italic").Typeface.Style);
                Assert.Equal(FontWeight.Bold, RunWith(s, 1, "bold italic").Typeface.Weight);
                Assert.Equal(FontWeight.Bold, RunWith(s, 1, "Strong (a style)").Typeface.Weight);
                Assert.Equal(FontWeight.Normal, RunWith(s, 1, "Weights: normal").Typeface.Weight);
            }
        });

        [Fact]
        public void The_host_style_resolver_is_asked_per_writing_system_and_its_answer_is_drawn() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Drawing))
            {
                double english = RunWith(s, 1, "Strong (a style)").FontRenderingEmSize;
                double arabic = RunWith(s, 1, "قوي").FontRenderingEmSize;
                Assert.Equal(16, english);
                Assert.Equal(18 * 1.1, arabic, 3);
                Assert.Equal(22, RunWith(s, 0, "Drawing").FontRenderingEmSize);
                Assert.Equal(FontWeight.Bold, RunWith(s, 0, "Drawing").Typeface.Weight);

                var before = s.Field.LayoutCount;
                s.Field.StyleResolver = null;
                s.Field.ParagraphLayoutFor(0);
                Assert.True(s.Field.LayoutCount > before);
                Assert.Equal(16, RunWith(s, 0, "Drawing").FontRenderingEmSize);
                Assert.Equal(FontWeight.Normal, RunWith(s, 0, "Drawing").Typeface.Weight);
            }
        });

        [Fact]
        public void Font_sizes_absolute_relative_and_named_use_the_current_size() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Drawing))
            {
                Assert.Equal(24, RunWith(s, 2, "24 point").FontRenderingEmSize);
                Assert.Equal(8, RunWith(s, 2, "half size").FontRenderingEmSize);
                Assert.Equal(14, RunWith(s, 2, "named large").FontRenderingEmSize);
                Assert.Equal(14, RunWith(s, 2, "named smaller").FontRenderingEmSize);
                Assert.Equal(14, RunWith(s, 2, "Large (a style)").FontRenderingEmSize);
            }
        });

        [Fact]
        public void Font_families_resolve_the_default_font_markup_and_feature_lists_add_to_the_writing_systems() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Drawing))
            {
                Assert.Equal("Times New Roman", RunWith(s, 2, "<default serif>").Typeface.FontFamily.Name);
                Assert.Equal("Arial", RunWith(s, 2, "<default sans serif>").Typeface.FontFamily.Name);
                Assert.Equal("Courier New", RunWith(s, 2, "<default monospace>").Typeface.FontFamily.Name);
                Assert.Equal("Comic Sans MS", RunWith(s, 2, "Comic Sans MS").Typeface.FontFamily.Name);
                Assert.Null(RunWith(s, 6, "(default)").FontFeatures);
                Assert.Contains(RunWith(s, 6, "(tnum)").FontFeatures!, f => f.Tag == "tnum");
            }
        });

        [Fact]
        public void Fore_and_back_colours_are_BGR_and_a_special_value_means_the_default() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Drawing))
            {
                Assert.Equal(Color.FromRgb(255, 0, 0), ColorOf(RunWith(s, 3, "red text").ForegroundBrush));
                var green = RunWith(s, 3, "green on pale yellow");
                Assert.Equal(Color.FromRgb(0, 128, 0), ColorOf(green.ForegroundBrush));
                Assert.Equal(ColorOf(s.Field.Foreground), ColorOf(RunWith(s, 3, "transparent colour").ForegroundBrush));
                var warning = RunWith(s, 3, "Warning (a style)");
                Assert.Equal(Color.FromRgb(255, 0, 0), ColorOf(warning.ForegroundBrush));
            }
        });

        /// <summary>The rectangle of the first occurrence of <paramref name="token"/> in a paragraph, in the field's coordinates (one line of it).</summary>
        internal static Rect BoxOf(Session s, int paragraph, string token, int skip = 0, int length = -1)
        {
            var pl = s.Field.ParagraphLayoutFor(paragraph);
            int at = s.ParagraphText(paragraph).IndexOf(token, System.StringComparison.Ordinal) + skip;
            Assert.True(at >= skip, "no " + token);
            var li = pl.Lines[pl.LineIndexOf(at)];
            int n = length >= 0 ? length : token.Length - skip;
            var bounds = li.Line.GetTextBounds(at, System.Math.Min(n, li.End - at));
            double x0 = bounds.Min(b => b.Rectangle.X), x1 = bounds.Max(b => b.Rectangle.Right);
            double top = s.Field.ParagraphTop(paragraph) + li.TextY;
            return new Rect(li.X + x0, top, x1 - x0, li.Line.Height);
        }

        static readonly Color PaleYellowRgb = Color.FromRgb(255, 255, 153);

        [Fact]
        public void A_run_background_is_painted_and_a_selected_run_stays_visibly_selected_over_it() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Drawing))
            {
                var box = BoxOf(s, 3, "green on pale yellow");
                var other = BoxOf(s, 3, "transparent colour");
                var shot = new DrawingParagraphTests.Shot(s);
                Assert.Equal(PaleYellowRgb, shot.At(box.X + 1, box.Y + 1));
                Assert.Equal(PaleYellowRgb, shot.At(box.Right - 2, box.Y + 1));
                Assert.Equal(Color.FromRgb(255, 255, 255), shot.At(other.X + 1, other.Y + 1));
                int at = s.ParagraphText(3).IndexOf("green on pale yellow", System.StringComparison.Ordinal);
                s.Select(3, at, at + "green on pale yellow".Length);
                var selected = new DrawingParagraphTests.Shot(s);
                var pixel = selected.At(box.X + 1, box.Y + 1);
                Assert.NotEqual(PaleYellowRgb, pixel);
                Assert.NotEqual(Color.FromRgb(255, 255, 255), pixel);
                Assert.NotEqual(PaleYellowRgb, selected.At(box.Right - 2, box.Y + 1));
                // The unselected neighbour keeps its look.
                Assert.Equal(Color.FromRgb(255, 255, 255), selected.At(other.X + 1, other.Y + 1));
            }
        });

        [Fact]
        public void A_run_background_from_a_style_is_painted_too() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Drawing))
            {
                var box = BoxOf(s, 3, "Warning (a style)");
                Assert.Equal(PaleYellowRgb, new DrawingParagraphTests.Shot(s).At(box.X + 1, box.Y + 1));
            }
        });

        [Fact]
        public void Pixels_put_a_superscript_above_and_a_subscript_below_the_baseline_of_the_text_beside_it() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Drawing))
            {
                var shot = new DrawingParagraphTests.Shot(s);
                // The superscript and the subscript are small (the font's own size for them), so their strokes are light: a lighter ink counts here.
                int Bottom(Rect box)
                {
                    for (int y = (int)box.Bottom + 7; y >= (int)box.Y - 8; y--)
                        for (int x = (int)box.X + 1; x < (int)box.Right - 1; x++)
                            if (shot.IsInk(x, y, 200)) return y;
                    return -1;
                }
                int bottomH = Bottom(BoxOf(s, 5, "H2O", 0, 1));
                int bottomSub = Bottom(BoxOf(s, 5, "H2O", 1, 1));
                int bottomC = Bottom(BoxOf(s, 5, "mc2", 1, 1));
                int bottomSup = Bottom(BoxOf(s, 5, "mc2", 2, 1));
                Assert.True(bottomH >= 0 && bottomSub >= 0 && bottomC >= 0 && bottomSup >= 0);
                Assert.True(bottomSup <= bottomC - 3, "superscript ink ends at " + bottomSup + ", the text beside it at " + bottomC);
                Assert.True(bottomSub >= bottomH + 1, "subscript ink ends at " + bottomSub + ", the text beside it at " + bottomH);
            }
        });

        /// <summary>The ink runs (start, length) along the row below the letters of a run, where its underline is.</summary>
        static List<int> UnderlineSegments(DrawingParagraphTests.Shot shot, Rect box)
        {
            int bestRow = -1, best = 0;
            for (int y = (int)(box.Y + box.Height * 0.75); y < box.Bottom + 3; y++)
            {
                int n = 0;
                for (int x = (int)box.X + 1; x < box.Right - 1; x++) if (shot.IsInk(x, y, 220)) n++;
                if (n > best) { best = n; bestRow = y; }
            }
            var runs = new List<int>();
            if (bestRow < 0) return runs;
            int current = 0;
            for (int x = (int)box.X + 1; x < box.Right - 1; x++)
            {
                if (shot.IsInk(x, bestRow, 220)) current++;
                else if (current > 0) { runs.Add(current); current = 0; }
            }
            if (current > 0) runs.Add(current);
            return runs;
        }

        [Fact]
        public void Pixels_draw_a_dashed_underline_in_dashes_a_dotted_one_in_dots_and_a_double_one_unbroken() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Drawing))
            {
                var shot = new DrawingParagraphTests.Shot(s);
                var dashed = UnderlineSegments(shot, BoxOf(s, 4, "dashed"));
                var dotted = UnderlineSegments(shot, BoxOf(s, 4, "dotted"));
                var dbl = UnderlineSegments(shot, BoxOf(s, 4, "double"));
                Assert.True(dashed.Count >= 3, "dashed: " + string.Join(",", dashed));
                Assert.Contains(dashed, n => n >= 5);
                Assert.True(dotted.Count >= dashed.Count, "dotted: " + string.Join(",", dotted));
                Assert.All(dotted, n => Assert.True(n <= 3));
                Assert.Single(dbl);
            }
        });

        [Fact]
        public void Underline_types_draw_dotted_dashed_single_double_and_strikethrough_in_the_under_colour() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Drawing))
            {
                // The field draws underlines and strikethrough itself (a gap at each descender is Avalonia's): Avalonia is given none, and the run says
                // what to draw. UnderlineDescenderTests and the pixel tests above look at the picture.
                var layout = s.Field.ParagraphLayoutFor(4);
                int Kind(string token) => layout.DecorationAt(layout.EffectiveText.IndexOf(token, System.StringComparison.Ordinal))?.LineKind ?? 0;
                IBrush? Colour(string token) => layout.DecorationAt(layout.EffectiveText.IndexOf(token, System.StringComparison.Ordinal))?.LineBrush;
                Assert.Equal(1, Kind("dotted"));
                Assert.Equal(2, Kind("dashed"));
                Assert.Equal(3, Kind("single"));
                Assert.Equal(4, Kind("double"));
                Assert.Equal(5, Kind("strikethrough"));
                Assert.Equal(Color.FromRgb(0, 0, 255), ColorOf(Colour("blue single")));
                Assert.Equal(4, Kind("Misspelled"));
                Assert.Equal(Color.FromRgb(255, 0, 0), ColorOf(Colour("Misspelled")));
                Assert.Equal(0, Kind("Lines: "));
                foreach (var token in new[] { "dotted", "dashed", "single", "double", "strikethrough", "blue single", "Misspelled", "Lines: " }) Assert.Null(RunWith(s, 4, token).TextDecorations);
            }
        });

        [Fact]
        public void Superscript_and_subscript_are_smaller_than_the_text_beside_them_and_their_baseline_is_shifted_not_aligned_to_the_line() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Drawing))
            {
                var sup = RunWith(s, 5, "super");
                var sub = RunWith(s, 5, "sub");
                var plain = RunWith(s, 5, "Position: H");
                // The size is the font's own, from its OS/2 table (SuperscriptFontTests pins it against the font files), and well under the text beside it.
                Assert.InRange(sup.FontRenderingEmSize, 4, 16 * 0.8);
                Assert.InRange(sub.FontRenderingEmSize, 4, 16 * 0.8);
                Assert.Equal(16, plain.FontRenderingEmSize);
                Assert.Equal(BaselineAlignment.Baseline, sup.BaselineAlignment);
                Assert.Equal(BaselineAlignment.Baseline, sub.BaselineAlignment);
                var layout = s.Field.ParagraphLayoutFor(5);
                string text = s.ParagraphText(5);
                Assert.True(layout.Lines[0].Pieces(text.IndexOf("super", System.StringComparison.Ordinal), text.IndexOf("super", System.StringComparison.Ordinal) + 5).Single().Value > 0);
                Assert.True(layout.Lines[0].Pieces(text.IndexOf("sub,", System.StringComparison.Ordinal), text.IndexOf("sub,", System.StringComparison.Ordinal) + 3).Single().Value < 0);
            }
        });

        // Justification is the one drawing property not drawn yet (an ADR 0005 exception tracked as an issue; it is pinned in DrawingParagraphTests).

        static bool IsRed(DrawingParagraphTests.Shot shot, int x, int y) { var c = shot.At(x, y); return c.R > 200 && c.G < 90 && c.B < 90; }

        /// <summary>For each column of the box that has red ink in the rows below the baseline, the topmost such row; and the number of red
        /// pixels in the rows above the baseline.</summary>
        static (List<int> Tops, int Above, int Columns) Squiggle(DrawingParagraphTests.Shot shot, Session s, int paragraph, int at, int length)
        {
            var pl = s.Field.ParagraphLayoutFor(paragraph);
            var li = pl.Lines[pl.LineIndexOf(at)];
            var bounds = li.Line.GetTextBounds(at, System.Math.Min(length, li.End - at));
            double x0 = bounds.Min(b => b.Rectangle.X) + li.X, x1 = bounds.Max(b => b.Rectangle.Right) + li.X;
            int baseline = (int)System.Math.Round(s.Field.ParagraphTop(paragraph) + li.TextY + li.Line.Baseline);
            var tops = new List<int>();
            int above = 0;
            for (int x = (int)x0 + 1; x < (int)x1 - 1; x++)
            {
                for (int y = baseline - 14; y < baseline; y++) if (IsRed(shot, x, y)) above++;   // (the line above has its own squiggle further up)
                for (int y = baseline; y < baseline + 12; y++) if (IsRed(shot, x, y)) { tops.Add(y - baseline); break; }
            }
            return (tops, above, System.Math.Max(0, (int)x1 - 1 - ((int)x0 + 1)));
        }


        /// <summary>The row of the lowest ink inside a box grown by a few rows above and below.</summary>
        static int InkBottomRow(DrawingParagraphTests.Shot shot, Rect box) => shot.InkBottom(new Rect(box.X, box.Y - 8, box.Width, box.Height + 16));

        [Fact]
        public void A_raised_run_is_drawn_four_pixels_higher_and_a_lowered_run_four_pixels_lower_than_the_text_beside_it() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Drawing))
            {
                var shot = new DrawingParagraphTests.Shot(s);
                Assert.Equal(4000, s.Editor.Document.Paragraphs[5].Runs.Single(r => r.Text == "up").Props.IntProps[TextPropertyNumbers.Offset].Value);
                Assert.Equal(-4000, s.Editor.Document.Paragraphs[5].Runs.Single(r => r.Text == "down").Props.IntProps[TextPropertyNumbers.Offset].Value);
                // The p of "up" (raised 4 pt) and the p of "pt" beside it: the same letter, 4 pixels apart; and the o of "down" (lowered) and of "ordinary".
                // (They are on the same line, so the rows are comparable; a pixel allows for rounding.)
                var paragraph = s.Field.ParagraphLayoutFor(5);
                string text = s.ParagraphText(5);
                Assert.Equal(paragraph.LineIndexOf(text.IndexOf(": up", System.StringComparison.Ordinal) + 3), paragraph.LineIndexOf(text.IndexOf("pt", System.StringComparison.Ordinal)));
                int upP = InkBottomRow(shot, BoxOf(s, 5, ": up", 3, 1)), plainP = InkBottomRow(shot, BoxOf(s, 5, "pt", 0, 1));
                Assert.InRange(plainP - upP, 3, 5);
                // (The o, whose bowl is dark in the middle of its box wherever the letter falls between pixels, and not the n, whose stems are at its edges.)
                int downN = InkBottomRow(shot, BoxOf(s, 5, "down", 1, 1)), plainN = InkBottomRow(shot, BoxOf(s, 5, "ordinary run", 0, 1));
                Assert.InRange(downN - plainN, 3, 5);
            }
        });

        /// <summary>The highest row (relative to the text's own baseline) with red ink in the columns of a range of text.</summary>
        static int HighestRed(DrawingParagraphTests.Shot shot, Session s, int paragraph, int at, int length)
        {
            var pl = s.Field.ParagraphLayoutFor(paragraph);
            var li = pl.Lines[pl.LineIndexOf(at)];
            var bounds = li.Line.GetTextBounds(at, length);
            double x0 = bounds.Min(b => b.Rectangle.X) + li.X, x1 = bounds.Max(b => b.Rectangle.Right) + li.X;
            int baseline = (int)System.Math.Round(s.Field.ParagraphTop(paragraph) + li.TextY + li.Line.Baseline);
            for (int dy = -12; dy < 12; dy++)
                for (int x = (int)x0 + 1; x < (int)x1 - 1; x++)
                    if (IsRed(shot, x, baseline + dy)) return dy;
            return int.MaxValue;
        }

        [Fact]
        public void The_squiggle_under_a_raised_run_is_raised_with_it() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Drawing))
            {
                var run = s.Editor.Document.Paragraphs[4].Runs.Single(r => r.Text == "scribble");
                int at = s.ParagraphText(4).IndexOf("scribble", System.StringComparison.Ordinal);
                int plain = HighestRed(new DrawingParagraphTests.Shot(s), s, 4, at, "scribble".Length);
                run.Props.SetInt(TextPropertyNumbers.Offset, 4000, TextPropertyNumbers.VariationMilliPoint);
                s.Field.RefreshFromDocument();
                Headless.Pump();
                int raised = HighestRed(new DrawingParagraphTests.Shot(s), s, 4, at, "scribble".Length);
                Assert.InRange(plain - raised, 3, 5);   // the wave moved up with the run: 4 rows nearer the baseline of the text beside it, or past it
            }
        });
        [Fact]
        public void The_squiggle_underline_is_a_zigzag_below_the_baseline_in_the_under_colour_and_none_above_it() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Drawing))
            {
                var shot = new DrawingParagraphTests.Shot(s);
                int at = s.ParagraphText(4).IndexOf("scribble", System.StringComparison.Ordinal);
                var (tops, above, _) = Squiggle(shot, s, 4, at, "scribble".Length);
                Assert.Equal(0, above);
                Assert.True(tops.Count >= 25, "red ink under most columns of the word: " + tops.Count);
                Assert.True(tops.Min() >= 0, "below the baseline");
                Assert.True(tops.Max() - tops.Min() >= 2, "it rises and falls: rows " + string.Join(",", tops));
                Assert.True(tops.Max() <= 8, "close under the text: " + tops.Max());
                // The letters of the word are still the text colour, and the run before it (a plain one) has no red below it.
                var before = Squiggle(shot, s, 4, s.ParagraphText(4).IndexOf("Lines", System.StringComparison.Ordinal), 5);
                Assert.Empty(before.Tops);
            }
        });

        [Fact]
        public void A_squiggled_run_that_wraps_over_lines_is_squiggled_on_each_of_them() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Drawing, 330, 700))
            {
                var pl = s.Field.ParagraphLayoutFor(4);
                const string Token = "a squiggled run that is long enough to wrap onto a second line";
                int at = s.ParagraphText(4).IndexOf(Token, System.StringComparison.Ordinal);
                Assert.True(at >= 0, "the run is in the paragraph");
                int end = at + Token.Length;
                int first = pl.LineIndexOf(at), last = pl.LineIndexOf(end - 1);
                Assert.True(last > first, "the run is on more than one line");
                var shot = new DrawingParagraphTests.Shot(s);
                int checkedParts = 0;
                for (int i = first; i <= last; i++)
                {
                    int from = System.Math.Max(at, pl.Lines[i].Start), to = System.Math.Min(end, pl.Lines[i].End);
                    var part = Squiggle(shot, s, 4, from, to - from);
                    Assert.Equal(0, part.Above);
                    if (part.Columns < 8) continue;   // a word or two left over at the end of a line
                    checkedParts++;
                    Assert.True(part.Tops.Count >= part.Columns * 0.75, "line " + i + ": red ink under " + part.Tops.Count + " of " + part.Columns + " columns");
                    Assert.True(part.Tops.Max() - part.Tops.Min() >= 2, "line " + i + " rows " + string.Join(",", part.Tops));
                }
                Assert.True(checkedParts >= 2, "at least two lines of the run were checked");
            }
        });
        [Fact]
        public void The_character_style_box_sets_the_named_style_on_the_selection_and_none_clears_it() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                s.Select(0, 4, 8);
                var box = s.Find<ComboBox>(PlaygroundView.Names.CharStyle);
                box.SelectedItem = "Warning";
                Headless.Pump();
                var p0 = s.Editor.Document.Paragraphs[0];
                var run = p0.Runs.Single(r => r.Props.StrProps.ContainsKey(TextPropertyNumbers.NamedStyle));
                Assert.Equal("word", run.Text);
                Assert.Equal("Warning", run.Props.StrProps[TextPropertyNumbers.NamedStyle]);
                Assert.Equal(Color.FromRgb(255, 0, 0), ColorOf(RunWith(s, 0, "word").ForegroundBrush));
                Assert.Null(box.SelectedItem);

                box.SelectedItem = SampleStyles.None;
                Headless.Pump();
                Assert.DoesNotContain(s.Editor.Document.Paragraphs[0].Runs, r => r.Props.StrProps.ContainsKey(TextPropertyNumbers.NamedStyle));
            }
        });
    }
}
