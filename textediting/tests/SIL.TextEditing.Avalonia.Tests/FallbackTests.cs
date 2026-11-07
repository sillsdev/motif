using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>A writing system the host did not register is drawn at FieldWorks' 10 point default (owner decision, 2026-11-04).</summary>
    public class FallbackTests
    {
        [Fact]
        public void An_unregistered_writing_system_is_10_point_Segoe_UI()
        {
            var info = new WritingSystemRegistry().Get("zz");
            Assert.Equal(10, info.Size);
            Assert.Equal("Segoe UI", info.Font);
            Assert.Equal("zz", info.Tag);
        }

        [Fact]
        public void A_registered_writing_system_keeps_its_own_size()
        {
            var registry = new WritingSystemRegistry().Add(WritingSystemRegistry.Define("en", "Segoe UI", 14));
            Assert.Equal(14, registry.Get("en").Size);
        }

        [Fact]
        public void A_field_with_no_registered_writing_systems_lays_text_out_at_10_point() => Headless.Run(() =>
        {
            var plain = new RichTextField(OneParagraph("Hello world"));
            Fixtures.Layout(plain, 400);
            var sixteen = Fixtures.Field(OneParagraph("Hello world"), 400);
            double ratio = plain.ParagraphLayoutFor(0).Width / sixteen.ParagraphLayoutFor(0).Width;
            Assert.InRange(ratio, 0.55, 0.7);   // 10 / 16 = 0.625
        });

        [Fact]
        public void At_the_real_10_point_default_wrapped_text_has_carets_hits_and_selection_boxes_that_agree() => Headless.Run(() =>
        {
            const string text = "The quick brown fox jumps over the lazy dog while the five boxing wizards jump quickly and a mad boxer shot a gloved jab.";
            var field = new RichTextField(OneParagraph(text));   // no fixture: nothing is pinned to 16 point
            Fixtures.Layout(field, 150);
            var pl = field.ParagraphLayoutFor(0);
            Assert.True(pl.Lines.Count >= 4, "lines: " + pl.Lines.Count);
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == ' ') continue;
                var rects = pl.SelectionRects(i, i + 1);
                var box = Assert.Single(rects);
                Assert.True(box.Width > 1 && box.Width < 12, "a 10 point letter is narrow: " + box.Width);
                int hit = pl.HitTest(new global::Avalonia.Point(box.X + box.Width * 0.25, box.Y + box.Height / 2));
                Assert.Equal(i, hit);
                Assert.True(System.Math.Abs(pl.CaretRect(i).X - box.X) < 1.5, "caret at " + i);
            }
        });
    }
}
