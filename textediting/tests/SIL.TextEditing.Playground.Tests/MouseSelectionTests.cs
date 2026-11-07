using System.Linq;
using Avalonia.Input;
using Xunit;

namespace SIL.TextEditing.Playground.Tests
{
    /// <summary>Mouse selection in the playground's field, in the Latin and Arabic scenario, read back through the
    /// inspector the way a tester reads it.</summary>
    public class MouseSelectionTests
    {
        [Fact]
        public void Double_click_on_an_arabic_word_selects_it_and_the_inspector_shows_the_range() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                string t = s.ParagraphText(0);
                int at = t.IndexOf("كتاب") + 1;
                s.MouseClick(s.PointAt(0, at), 2);
                int start = t.IndexOf("كتاب");
                Assert.Contains("selection 0:" + start + " -> 0:" + (start + 4), s.Inspector);
            }
        });

        [Fact]
        public void Triple_click_in_the_all_arabic_paragraph_selects_the_paragraph() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                int len = s.ParagraphText(2).Length;
                s.MouseClick(s.PointAt(2, 3), 3);
                Assert.Contains("selection 2:0 -> 2:" + len, s.Inspector);
            }
        });

        [Fact]
        public void Dragging_across_paragraphs_and_pressing_delete_joins_them_and_keeps_the_first_identity() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                var firstId = s.ParagraphKey(0);
                int before = s.Editor.Document.Paragraphs.Count;
                s.MouseDrag(s.PointAt(0, 4), s.PointAt(3, 6));
                s.Editor.Ordered(out var a, out var b);
                Assert.Equal(0, a.Paragraph);
                Assert.Equal(3, b.Paragraph);
                s.Press(Key.Delete);
                Assert.Equal(before - 3, s.Editor.Document.Paragraphs.Count);
                Assert.Equal(firstId, s.ParagraphKey(0));
                Assert.StartsWith("The ", s.ParagraphText(0));
            }
        });

        [Fact]
        public void Shift_click_and_ctrl_shift_arrows_extend_the_selection() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                s.MouseClick(s.PointAt(3, 2));
                s.MouseClick(s.PointAt(3, 10), 1, RawInputModifiers.Shift);
                Assert.Contains("selection 3:2 -> 3:", s.Inspector);
                Assert.Equal(new TextPosition(3, 2), s.Editor.Anchor);
                s.Press(Key.Right, KeyModifiers.Control | KeyModifiers.Shift);
                Assert.True(s.Editor.Caret.Offset > 10);
                Assert.Equal(new TextPosition(3, 2), s.Editor.Anchor);
            }
        });
    }
}
