using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>A justified line draws each glyph once, in pieces between its spaces, and looks as it did when it was Avalonia's whole line drawn clipped to each
    /// word (issue 44).</summary>
    public class JustifiedDrawTests
    {
        const string English = "The quick brown fox jumps over the lazy dog while the five boxing wizards jump quickly and a mad boxer shot a quick, gloved jab to the jaw of his dizzy opponent.";
        const string Arabic = "هذه فقرة عربية طويلة بما يكفي لتلتف على عدة أسطر عندما تكون النافذة ضيقة وتبين أين تقع الهوامش والمسافات البادئة في الاتجاه من اليمين إلى اليسار وهي تنتهي هنا";

        sealed class Canvas : Control
        {
            public Action<DrawingContext>? Body;
            public override void Render(DrawingContext c) { c.DrawRectangle(Brushes.White, null, new Rect(Bounds.Size)); Body?.Invoke(c); }
        }

        static Fixtures.Shot Shot(double w, double h, Action<DrawingContext> body, double scale = 1)
        {
            var c = new Canvas { Width = w, Height = h, Body = body };
            c.Measure(new Size(w, h));
            c.Arrange(new Rect(0, 0, w, h));
            return new Fixtures.Shot(c, scale);
        }

        /// <summary>The way a justified line was drawn before: the whole of Avalonia's line, once per space, clipped to the word and moved to where it now is.</summary>
        static void WholeLineClipped(DrawingContext context, LineInfo li, double top)
        {
            const double Far = 100000;
            var j = li.Stretch!;
            int n = j.Spaces.Count;
            double previous = -Far;
            for (int k = 0; k <= n; k++)
            {
                double guard = k < n ? j.Guard(k) : 0;
                double end = k < n ? j.Spaces[k].Key + guard : Far;
                if (end > previous)
                    using (context.PushTransform(Matrix.CreateTranslation(j.RightAnchor ? -j.Extra * (n - k) : j.Extra * k, 0)))
                    using (context.PushClip(new Rect(li.X + previous, top + li.TextY - 50000, end - previous, 100000)))
                        li.Line.Draw(context, new Point(li.X, top + li.TextY));
                if (k == n) break;
                previous = end;
            }
        }

        [Theory]
        [InlineData(English, false, false)]
        [InlineData(English, false, true)]
        [InlineData(Arabic, true, false)]
        [InlineData("dog فقرة fox طويلة jumps هذه ill (جمل) 12 it dog فقرة fox طويلة jumps هذه ill (جمل) 12 it dog فقرة fox طويلة jumps هذه ill (جمل) 12 it dog فقرة fox طويلة jumps هذه ill (جمل) 12 it", false, true)]
        public void A_justified_line_looks_as_it_did_when_drawn_whole_once_per_space(string text, bool rtl, bool bold) => Headless.Run(() =>
        {
            foreach (double scale in new[] { 1.0, 1.5, 2.0 }) Compare(text, rtl, bold, scale);
        });

        [Theory]
        [InlineData(1.0)]
        [InlineData(1.5)]
        [InlineData(2.0)]
        public void An_italic_coloured_justified_line_looks_as_it_did_down_to_every_pixel_the_hooks_of_its_letters_that_reach_into_the_next_space_included(double scale) => Headless.Run(() =>
            Compare("The jug of jam, a jazz jig and a quick jab by the judge, who jumped just to jot a joke for jolly jurors enjoying jars of jelly. The jug of jam, a jazz jig and a quick jab by the judge, who jumped just to jot a joke for jolly jurors enjoying jars of jelly.", false, false, scale, italic: true, colour: true));

        /// <summary>Whether an x (from the control) is within a pixel of a space of a line stretched to the right, from where it was to where its end is.</summary>
        static bool InASpace(LineInfo li, Justification j, double x)
        {
            for (int k = 0; k < j.Spaces.Count; k++)
                if (x >= li.X + j.Spaces[k].Key + j.Extra * k - 1 && x <= li.X + j.Spaces[k].Value + j.Extra * (k + 1) + 1) return true;
            return false;
        }

        static void Compare(string text, bool rtl, bool bold, double scale, bool italic = false, bool colour = false)
        {
            var p = Para(rtl ? "ar" : "en", rtl ? "ar" : "en", text.Substring(0, text.Length / 2), "en", text.Substring(text.Length / 2));
            if (bold) p.Runs[1].Props.SetInt(TextPropertyNumbers.Bold, TextPropertyNumbers.ToggleOn);
            if (italic) foreach (var r in p.Runs) r.Props.SetInt(TextPropertyNumbers.Italic, TextPropertyNumbers.ToggleOn);
            if (colour) p.Runs[1].Props.SetInt(TextPropertyNumbers.ForeColor, 0x0000FF);   // BGR: red
            p.Rtl = rtl;
            p.Props.SetInt(TextPropertyNumbers.Align, 5);
            var pl = Fixtures.Field(Doc(p), 260).ParagraphLayoutFor(0);
            int justified = 0;
            foreach (var li in pl.Lines)
            {
                if (!li.IsJustified) continue;
                justified++;
                var now = Shot(300, li.Height + 20, c => li.Draw(c, 5 - li.Y), scale);
                var was = Shot(300, li.Height + 20, c => WholeLineClipped(c, li, 5 - li.Y), scale);
                // Every pixel is the same, except in italics: the old drawing cut the whole line at the middle of each space, and the hook of a letter that reaches
                // past that into the space beside it (the tip of the "j" of "jug") was drawn where it is not stretched, apart from its letter. Each letter is
                // drawn on its own and its ink stops at the middle of the space (PieceBounds), so that tip is gone: a pixel or two, only in the columns of a space,
                // between its natural edge and the edge it is stretched to, and only ink that is missing, never ink in a new place.
                var j = li.Stretch!;
                int ink = 0, differ = 0, hooks = 0;
                for (int y = 0; y < now.Height; y++)
                    for (int x = 0; x < now.Width; x++)
                    {
                        if (now.IsInk(x, y) || was.IsInk(x, y)) ink++;
                        if (now.Pixel(x, y) == was.Pixel(x, y)) continue;
                        if (italic && InASpace(li, j, x / scale) && now.Pixel(x, y) == Colors.White) hooks++; else differ++;
                    }
                Assert.True(ink > 100 || colour);
                Assert.True(differ == 0, "scale " + scale + " line " + li.Start + ": " + differ + " pixels differ in colour or coverage");
                Assert.True(hooks <= 4 * scale * scale * j.Spaces.Count, "scale " + scale + " line " + li.Start + ": " + hooks + " pixels differ at the spaces");
            }
            Assert.True(justified > 3);
        }

        [Fact]
        public void A_justified_line_hands_each_glyph_to_the_renderer_once_not_once_per_space() => Headless.Run(() =>
        {
            var p = Para("en", "en", English + " " + English);
            p.Props.SetInt(TextPropertyNumbers.Align, 5);
            var pl = Fixtures.Field(Doc(p), 320).ParagraphLayoutFor(0);
            foreach (var li in pl.Lines)
            {
                if (!li.IsJustified) continue;
                int glyphs = 0;
                foreach (var tr in li.Line.TextRuns) if (tr is global::Avalonia.Media.TextFormatting.ShapedTextRun s) glyphs += s.GlyphRun.GlyphInfos.Count;
                using var probe = LayoutProbe.Start();
                Shot(400, li.Height + 20, c => li.Draw(c, 5 - li.Y));
                Assert.True(probe.GlyphsDrawn <= glyphs, "line " + li.Start + ": " + probe.GlyphsDrawn + " glyphs drawn for " + glyphs + " in the line and " + li.Stretch!.Spaces.Count + " spaces");
            }
        });
    }
}
