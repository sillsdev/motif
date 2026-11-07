using System;
using System.Linq;
using Avalonia;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>Complex scripts wrap and shape through HarfBuzz. These tests use Windows fonts (Nirmala UI, Segoe UI,
    /// Leelawadee UI, Constantia) and so need a Windows machine.</summary>
    public class ShapingTests
    {
        public static readonly object[][] Samples =
        {
            new object[] { "ta", "தமிழ் மொழி க்ஷ கொள்ளை கொண்டு" },
            new object[] { "hi", "क्षत्रिय संस्कृत भाषा नीली हिन्दी विश्व" },
            new object[] { "ar", "العربية لغة جميلة ومتصلة الحروف بشكل كبير" },
            new object[] { "th", "ภาษาไทยไม่มีช่องว่างระหว่างคำเลยต้องตัดคำ" },
        };

        [Theory, MemberData(nameof(Samples))]
        public void Script_wraps_within_the_width(string ws, string text) => Headless.Run(() =>
        {
            var p = Para(ws, ws, text);
            p.Rtl = ws == "ar";
            var field = Fixtures.Field(Doc(p), 210);
            var layout = field.ParagraphLayoutFor(0);
            Assert.True(layout.Lines.Count > 1, ws + " lines " + layout.Lines.Count);
            Assert.All(layout.Lines, l => Assert.True(l.Line.Width <= 211, ws + " line width " + l.Line.Width));
        });

        [Fact]
        public void Complex_script_clusters_are_not_split_by_arrow_keys() => Headless.Run(() =>
        {
            // Devanagari "ksha + ra + i + ya" starts with a four code point conjunct (KA VIRAMA SSA, then vowel signs).
            var field = Fixtures.Field(Doc(Para("hi", "hi", "क्षि")), 400);
            var layout = field.ParagraphLayoutFor(0);
            var stops = layout.Stops(layout.Lines[0]).Select(k => k.Key).ToList();
            // Whatever the shaper decides, no stop may fall between a consonant and its vowel sign.
            Assert.DoesNotContain(3, stops);
            Assert.Equal(0, stops[0]);
            Assert.Equal(4, stops[stops.Count - 1]);
        });

        [Fact]
        public void An_opentype_feature_from_the_writing_system_changes_the_shaped_width() => Headless.Run(() =>
        {
            double WidthWith(string? features)
            {
                var p = Para("en", "en", "0123456789");
                var layout = ParagraphLayout.Build(p, 2000, new WritingSystemRegistry().Add(WritingSystemRegistry.Define("en", "Constantia", 28, false, features)), null, global::Avalonia.Media.Brushes.Black);
                return layout.Width;
            }
            double plain = WidthWith(null), lining = WidthWith("lnum=1"), oldstyle = WidthWith("onum=1");
            Assert.True(Math.Abs(plain - lining) > 0.5 || Math.Abs(plain - oldstyle) > 0.5 || Math.Abs(lining - oldstyle) > 0.5,
                "widths " + plain + " " + lining + " " + oldstyle);
        });

        [Fact]
        public void Run_properties_bold_and_size_reach_the_layout() => Headless.Run(() =>
        {
            double WidthWith(Action<Run> format)
            {
                var p = Para("en", "en", "wide wide wide");
                format(p.Runs[0]);
                return ParagraphLayout.Build(p, 2000, WritingSystemRegistry.CreateDefault(), null, global::Avalonia.Media.Brushes.Black).Width;
            }
            double normal = WidthWith(r => { });
            double bold = WidthWith(r => r.Props.SetInt(TextPropertyNumbers.Bold, TextPropertyNumbers.ToggleOn));
            double big = WidthWith(r => r.Props.SetInt(TextPropertyNumbers.FontSize, 32000, TextPropertyNumbers.VariationMilliPoint));
            Assert.True(bold > normal, "bold " + bold + " normal " + normal);
            Assert.True(big > normal * 1.5, "big " + big + " normal " + normal);
        });
    }
}
