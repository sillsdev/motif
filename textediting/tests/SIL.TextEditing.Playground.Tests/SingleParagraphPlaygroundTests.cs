using Avalonia.Controls;
using Avalonia.Input;
using Xunit;

namespace SIL.TextEditing.Playground.Tests
{
    public class SingleParagraphPlaygroundTests
    {
        [Fact]
        public void The_check_box_makes_Enter_go_to_the_host_instead_of_splitting_the_paragraph() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                int count = s.Editor.Document.Paragraphs.Count;
                s.Caret(0, 1);
                s.Press(Key.Enter);
                Assert.Equal(count + 1, s.Editor.Document.Paragraphs.Count);   // off: Enter splits

                s.Find<CheckBox>(PlaygroundView.Names.SingleParagraph).IsChecked = true;
                Headless.Pump();
                Assert.True(s.Field.SingleParagraph);
                Assert.Contains("single paragraph: on", s.Log);
                s.Caret(0, 1);
                s.Press(Key.Enter);
                Assert.Equal(count + 1, s.Editor.Document.Paragraphs.Count);
                Assert.Contains("Enter passed to the host", s.Log);
                string beforeBreak = s.ParagraphText(0);
                s.Press(Key.Enter, KeyModifiers.Shift);   // FieldWorks: a hard line break in the paragraph, not a request to the host
                Assert.DoesNotContain("Enter passed to the host (Shift)", s.Log);
                Assert.Equal(count + 1, s.Editor.Document.Paragraphs.Count);
                Assert.Contains(DocumentEditor.LineSeparator, s.ParagraphText(0));
                Assert.NotEqual(beforeBreak, s.ParagraphText(0));

                string before = s.ParagraphText(0);
                s.Type("x\ny");
                Assert.Equal(count + 1, s.Editor.Document.Paragraphs.Count);
                Assert.Contains("x y", s.ParagraphText(0));
                Assert.NotEqual(before, s.ParagraphText(0));
            }
        });

        [Fact]
        public void The_flag_is_per_field_and_follows_focus_in_the_check_box() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                s.Find<CheckBox>(PlaygroundView.Names.SingleParagraph).IsChecked = true;
                Headless.Pump();
                s.Pick(Scenarios.ReadOnlyRuns);   // a new scenario builds new fields, so the mode is off again
                Assert.False(s.Field.SingleParagraph);
                Assert.False(s.Find<CheckBox>(PlaygroundView.Names.SingleParagraph).IsChecked);
            }
        });
    }
}
