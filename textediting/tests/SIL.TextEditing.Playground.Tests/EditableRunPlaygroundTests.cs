using Avalonia.Controls;
using Avalonia.Input;
using Xunit;

namespace SIL.TextEditing.Playground.Tests
{
    public class EditableRunPlaygroundTests
    {
        [Fact]
        public void Arrow_keys_step_over_read_only_text_and_typing_goes_to_editable_text() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.ReadOnlyRuns))
            {
                s.Caret(0, 0);
                Assert.Equal(7, s.Editor.Caret.Offset);   // "Gloss: " is read-only
                s.Press(Key.Right, times: 14);
                Assert.Equal(21, s.Editor.Caret.Offset);  // the end of "edit this part"
                s.Press(Key.Right);
                Assert.Equal(29, s.Editor.Caret.Offset);  // stepped over " [fixed]"
                s.Press(Key.Left);
                Assert.Equal(21, s.Editor.Caret.Offset);
                s.Type("!");
                Assert.Equal("Gloss: edit this part! [fixed] and this", s.ParagraphText(0));
            }
        });

        [Fact]
        public void Typing_and_delete_are_refused_in_a_read_only_paragraph_but_the_text_can_be_selected() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.ReadOnlyRuns))
            {
                s.Select(2, 0, 10);
                Assert.True(s.Editor.HasSelection);
                s.Type("x");
                s.Press(Key.Delete);
                s.Press(Key.Back);
                Assert.Equal("Entirely read-only paragraph.", s.ParagraphText(2));
            }
        });

        [Fact]
        public void The_read_only_field_check_box_stops_typing_and_Enter_and_the_inspector_still_follows_the_caret() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                s.Caret(2, 0);
                s.Find<CheckBox>(PlaygroundView.Names.ReadOnly).IsChecked = true;
                Headless.Pump();
                Assert.True(s.Field.IsReadOnly);
                string before = s.ParagraphText(2);
                s.Type("x");
                s.Press(Key.Enter);
                s.Press(Key.Left);   // forward in a right-to-left paragraph
                Assert.Equal(before, s.ParagraphText(2));
                Assert.Equal(4, s.Editor.Document.Paragraphs.Count);
                Assert.Equal(1, s.Editor.Caret.Offset);
                Assert.Contains("field read-only: on", s.Log);

                s.Find<CheckBox>(PlaygroundView.Names.ReadOnly).IsChecked = false;
                Headless.Pump();
                s.Type("x");
                Assert.StartsWith("ف" + "x", s.ParagraphText(2));
            }
        });

        [Fact]
        public void Formatting_a_selection_that_holds_read_only_text_is_refused() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.ReadOnlyRuns))
            {
                s.Editor.Anchor = new TextPosition(0, 0);
                s.Editor.SetCaret(new TextPosition(0, 12), true);
                s.Click(PlaygroundView.Names.Bold);
                Assert.Contains("refused", s.Log);
                Assert.Empty(s.View.Undo.Done);
            }
        });
    }
}
