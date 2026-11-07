using System.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using Avalonia;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>A layout fuzz with bidirectional and other format characters in the text: whatever is typed, the layout, the caret, the selection boxes,
    /// hit testing and drawing must not throw (issues 106 and 112). The default size is small; set TE_FUZZ_PARAGRAPHS to run it as long as you like.</summary>
    public class BidiFormatFuzzTests
    {
        static readonly string[] Words = { "alpha", "fox", "Wonderful", "ill", "12", "ét", "هذه", "فقرة", "عربية", "طويلة", "(جمل)", "گچپژ", "é", "שלום", "א" };
        static readonly string[] Spaces = { " ", "   ", " ", " " };
        static readonly string[] Formats =
        {
            "‏", "‎", "؜", "‪", "‫", "‬", "‭", "‮", "⁦", "⁧", "⁨", "⁩",
            "‍", "‌", "​", "⁠", "﻿",
            "̀", "́", "͏", "️", "︀", char.ConvertFromUtf32(0xE0100), "⃝", "ְ"
        };
        static readonly string[] Wss = { "en", "ar", "fr", "he" };

        static int Count()
        {
            var env = Environment.GetEnvironmentVariable("TE_FUZZ_PARAGRAPHS");
            return int.TryParse(env, out int n) && n > 0 ? n : 120;
        }

        static string Text(Random rng)
        {
            var sb = new StringBuilder();
            int tokens = rng.Next(2, 14);
            for (int i = 0; i < tokens; i++)
            {
                sb.Append(Words[rng.Next(Words.Length)]);
                if (rng.Next(5) == 0) for (int k = rng.Next(1, 4); k > 0; k--) sb.Append(Formats[rng.Next(Formats.Length)]);
                sb.Append(Spaces[rng.Next(Spaces.Length)]);
                if (rng.Next(5) == 0) for (int k = rng.Next(1, 4); k > 0; k--) sb.Append(Formats[rng.Next(Formats.Length)]);
            }
            return sb.ToString();
        }

        [Theory]
        [InlineData(7)]
        [InlineData(11)]
        public void Paragraphs_with_bidi_format_characters_lay_out_report_caret_boxes_and_hits_and_draw_without_throwing(int seed) => Headless.Run(() =>
        {
            var rng = new Random(seed);
            int n = Count();
            var failures = new List<string>();
            for (int c = 0; c < n; c++)
            {
                var p = Para("en");
                int runs = rng.Next(1, 6);
                for (int i = 0; i < runs; i++)
                {
                    var r = new Run { Ws = Wss[rng.Next(Wss.Length)], Text = Text(rng) };
                    if (rng.Next(4) == 0) r.Props.SetInt(TextPropertyNumbers.Bold, TextPropertyNumbers.ToggleOn);
                    p.Runs.Add(r);
                }
                p.Rtl = rng.Next(2) == 0;
                p.Props.SetInt(TextPropertyNumbers.Align, rng.Next(6));
                if (rng.Next(3) == 0) p.Props.SetInt(TextPropertyNumbers.MaxLines, rng.Next(1, 4));
                double width = rng.Next(30, 421);
                string what = "seed " + seed + " case " + c + " width " + width + " rtl " + p.Rtl;
                try
                {
                    var field = Fixtures.Field(Doc(p), width);
                    var pl = field.ParagraphLayoutFor(0);
                    int length = pl.EffectiveText.Length;
                    pl.SelectionRects(0, length);
                    for (int a = 0; a + 5 <= length; a += 5) pl.SelectionRects(a, a + 5);
                    for (int o = 0; o <= length; o++) pl.CaretRect(o);
                    foreach (var line in pl.Lines)
                    {
                        pl.VisualStops(line);
                        for (double x = -5; x <= width + 5; x += 7) pl.HitTestDetailed(new Point(x, line.Y + line.Height / 2));
                    }
                    field.Editor.SetCaret(new TextPosition(0, 0));
                    field.Editor.SetCaret(new TextPosition(0, p.Length), true);
                    Fixtures.Render(field);
                }
                catch (Exception e)
                {
                    failures.Add(what + ": " + e.GetType().Name + ": " + e.Message + " @ " + string.Join(" < ", (e.StackTrace ?? "").Split((char)10).Where(l => l.Contains("SIL.TextEditing")).Take(3).Select(l => l.Trim().Split((char)40)[0])));
                }
            }
            Assert.True(failures.Count == 0, failures.Count + " of " + n + " paragraphs threw:\n" + string.Join("\n", failures));
        });
    }
}
