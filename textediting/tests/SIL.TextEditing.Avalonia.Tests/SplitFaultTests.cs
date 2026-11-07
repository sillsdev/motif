using System;
using System.Collections.Generic;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>Avalonia 11.3 throws "Cannot split: requested length N consumes entire run" for more than a zero width joiner: for a space followed by a combining
    /// mark or a variation selector in a paragraph with Arabic or Hebrew in it. Typed text must not crash the layout (issue 112, follow-up of its review).</summary>
    public class SplitFaultTests
    {
        static string C(int codePoint) => char.ConvertFromUtf32(codePoint);

        const string Arabic = "جمل";
        const string Hebrew = "שלום";

        public static IEnumerable<object[]> Cases()
        {
            yield return new object[] { true, new[] { "ar", Arabic + " " + C(0x300) + "x" } };
            yield return new object[] { false, new[] { "ar", Arabic + " ", "en", C(0x300) + "x" } };
            yield return new object[] { true, new[] { "ar", Arabic + " ", "en", C(0x300) + "x" } };
            yield return new object[] { false, new[] { "en", "abc def " + C(0x300) + Arabic } };
            yield return new object[] { true, new[] { "he", Hebrew + " " + C(0x300) + "x" } };
            yield return new object[] { false, new[] { "ar", Arabic + "  " + C(0x300) + "1" } };
            yield return new object[] { true, new[] { "ar", Arabic + "  " + C(0x300) + "1" } };
            yield return new object[] { true, new[] { "ar", Arabic + " " + C(0xFE0F) + "x" } };
            yield return new object[] { true, new[] { "ar", Arabic + " " + C(0xE0100) + "x" } };
            yield return new object[] { true, new[] { "ar", Arabic + " " + C(0x34F) + "x" } };
            yield return new object[] { false, new[] { "ar", Arabic + " " + C(0x34F) + C(0x300) + "x " + C(0xFE0F) + " " + C(0x301) + "y" } };
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void A_space_followed_by_a_mark_or_a_variation_selector_in_a_paragraph_with_Arabic_or_Hebrew_lays_out_at_every_width_though_Avalonia_11_3_throws_Cannot_split(bool rtl, string[] wsAndText) => Headless.Run(() =>
        {
            int laid = 0;
            for (double width = 15; width <= 330; width += 1)
            {
                var p = Para(wsAndText[0], wsAndText);
                p.Rtl = rtl;
                var pl = Fixtures.Field(Doc(p), width).ParagraphLayoutFor(0);
                Assert.True(pl.Lines.Count >= 1);
                Assert.Equal(p.Length, pl.Lines[pl.Lines.Count - 1].End);
                for (int i = 0; i <= p.Length; i++) pl.CaretRect(i);   // the text is the real text: every offset has a caret
                laid++;
            }
            Assert.Equal(316, laid);
        });

        [Fact]
        public void A_label_with_a_zero_width_joiner_and_Arabic_and_a_mark_after_a_space_does_not_crash_the_layout() => Headless.Run(() =>
        {
            for (double width = 40; width <= 200; width += 5)
            {
                var p = Para("en", "en", "abc def ghi jkl");
                p.Props.SetInt(TextPropertyNumbers.BulNumScheme, BulletNumbers.Arabic);
                p.Props.SetStr(TextPropertyNumbers.BulNumTxtBefore, Arabic + " " + C(0x300) + "‍");
                var pl = Fixtures.Field(Doc(p), width).ParagraphLayoutFor(0);
                Assert.True(pl.Lines.Count >= 1);
            }
        });

        [Fact]
        public void A_line_limit_ellipsis_beside_such_text_does_not_crash_the_layout() => Headless.Run(() =>
        {
            for (double width = 40; width <= 200; width += 5)
            {
                var p = Para("ar", "ar", Arabic + " " + C(0x300) + "x " + Arabic + " " + C(0x300) + "y " + Arabic + " " + Arabic + " " + Arabic);
                p.Props.SetInt(TextPropertyNumbers.MaxLines, 1);
                Assert.True(Fixtures.Field(Doc(p), width).ParagraphLayoutFor(0).Lines.Count >= 1);
            }
        });

        [Fact]
        public void The_swap_keeps_every_offset_and_a_mark_after_a_space_stays_and_the_space_is_what_changes()
        {
            string text = Arabic + " " + C(0x300) + "x " + C(0xFE0F) + C(0x301) + " " + C(0xE0100) + "y";
            foreach (int swap in new[] { 1, 2, 3, 4, 5 })
                Assert.Equal(text.Length, ParagraphLayout.SwapFormatCharacters(text, swap).Length);
            string three = ParagraphLayout.SwapFormatCharacters(text, 3), four = ParagraphLayout.SwapFormatCharacters(text, 4);
            Assert.Equal(' ', three[3]);              // the space before the mark
            Assert.Equal(C(0x300)[0], three[4]);            // the mark itself is kept
            Assert.Equal('◌', four[3]);
            Assert.Equal('⁠', three[7]);              // a variation selector after a space has no glyph: hidden
            Assert.DoesNotContain(C(0x300), ParagraphLayout.SwapFormatCharacters(text, 5));
        }

        [Theory]
        [InlineData(0x300, 0x323)]
        [InlineData(0x301, 0x323)]
        [InlineData(0x20DD, 0x323)]
        public void A_mark_after_a_space_in_a_paragraph_with_Arabic_is_drawn_though_the_formatter_is_given_it_on_another_base(int mark, int other) => Headless.Run(() =>
        {
            for (double width = 100; width <= 300; width += 20)
            {
                bool[,] Ink(string markText)
                {
                    var p = Para("ar", "ar", Arabic + markText + "x");
                    p.Rtl = true;
                    var shot = Fixtures.Render(Fixtures.Field(Doc(p), width), 2);
                    var ink = new bool[shot.Width, shot.Height];
                    for (int y = 0; y < shot.Height; y++)
                        for (int x = 0; x < shot.Width; x++)
                            ink[x, y] = shot.IsInk(x, y, 200);
                    return ink;
                }
                int Differ(bool[,] a, bool[,] b)
                {
                    int n = 0;
                    for (int y = 0; y < a.GetLength(1); y++)
                        for (int x = 0; x < a.GetLength(0); x++)
                            if (a[x, y] != b[x, y]) n++;
                    return n;
                }
                // Two different marks on the same space: if a mark is drawn the pictures differ, and only by the marks' own ink, because whatever stands in for the
                // space (a no-break space, or on 11.3 a dotted circle) is the same in both. A mark that is not drawn leaves the two pictures alike (the old way out of the
                // fault put a word joiner, which has no glyph, where the mark is, and hid it so).
                // Three different marks must all give different pictures: two that are not drawn would give the same one (the bare stand-in for the space).
                var a = Ink(" " + C(mark)); var b = Ink(" " + C(other)); var c = Ink(" " + C(0x20DD == mark ? 0x301 : 0x20DD));
                Assert.True(Differ(a, b) > 0 && Differ(a, c) > 0 && Differ(b, c) > 0, "width " + width + ": each mark's own ink is drawn");
                var bare = Ink(" ");     // and none of them is just the bare space
                Assert.True(Differ(a, bare) > 0 && Differ(b, bare) > 0 && Differ(c, bare) > 0, "width " + width + ": each mark differs from the bare space");
            }
        });
    }
}
