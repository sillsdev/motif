using System.Collections.Generic;
using Avalonia.Input;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>ADR 0021: Tab, Shift+Tab, Ctrl+Tab and arrows at the edge of the field ask the host to move focus.</summary>
    public class LeaveRequestedTests
    {
        static (RichTextField Field, List<LeaveRequestedEventArgs> Seen) Watch(Document doc, bool handle = false)
        {
            var field = Fixtures.Field(doc);
            var seen = new List<LeaveRequestedEventArgs>();
            field.LeaveRequested += (o, e) => { seen.Add(e); e.Handled = handle; };
            return (field, seen);
        }

        [Fact]
        public void Tab_and_Shift_Tab_ask_to_leave_forwards_and_backwards_and_the_key_keeps_bubbling_unless_the_host_handles_it() => Headless.Run(() =>
        {
            var (field, seen) = Watch(OneParagraph("abc"));
            Assert.False(field.HandleKey(Key.Tab, KeyModifiers.None));
            Assert.False(field.HandleKey(Key.Tab, KeyModifiers.Shift));
            Assert.Equal(LeaveDirection.Next, seen[0].Direction);
            Assert.Equal(LeaveDirection.Previous, seen[1].Direction);
            Assert.All(seen, e => { Assert.Equal(LeaveKey.Tab, e.Key); Assert.False(e.ToEnd); Assert.True(double.IsNaN(e.GoalX)); });

            var (handled, _) = Watch(OneParagraph("abc"), handle: true);
            Assert.True(handled.HandleKey(Key.Tab, KeyModifiers.None));
        });

        [Fact]
        public void Ctrl_Tab_goes_to_the_last_place_and_Ctrl_Shift_Tab_to_the_first_and_each_leaves_only_from_there() => Headless.Run(() =>
        {
            var (field, seen) = Watch(OneParagraph("abc"));
            field.Editor.SetCaret(new TextPosition(0, 1));
            Assert.True(field.HandleKey(Key.Tab, KeyModifiers.Control));
            Assert.Equal(new TextPosition(0, 3), field.Editor.Caret);
            Assert.Empty(seen);
            field.HandleKey(Key.Tab, KeyModifiers.Control);
            Assert.True(seen[0].ToEnd && seen[0].Direction == LeaveDirection.Next);

            Assert.True(field.HandleKey(Key.Tab, KeyModifiers.Control | KeyModifiers.Shift));
            Assert.Equal(new TextPosition(0, 0), field.Editor.Caret);
            Assert.Single(seen);
            field.HandleKey(Key.Tab, KeyModifiers.Control | KeyModifiers.Shift);
            Assert.True(seen[1].ToEnd && seen[1].Direction == LeaveDirection.Previous);
        });

        [Fact]
        public void Tab_and_Shift_Tab_move_to_the_next_and_previous_editable_place_and_leave_only_when_there_is_none() => Headless.Run(() =>
        {
            var p = new Paragraph { DefaultWs = "en" };
            var label = new Run { Ws = "en", Text = "Gloss: " };
            label.Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableNo);
            p.Runs.Add(label);
            p.Runs.Add(new Run { Ws = "en", Text = "abc" });
            var fixedText = new Run { Ws = "en", Text = " [fixed]" };
            fixedText.Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableNo);
            p.Runs.Add(fixedText);
            p.Runs.Add(new Run { Ws = "en", Text = "xyz" });
            var (field, seen) = Watch(Doc(p));
            field.Editor.SetCaret(new TextPosition(0, 8));

            Assert.True(field.HandleKey(Key.Tab, KeyModifiers.None));          // the field keeps the key
            Assert.Equal(new TextPosition(0, 18), field.Editor.Caret);
            Assert.Empty(seen);
            Assert.False(field.HandleKey(Key.Tab, KeyModifiers.None));         // no place after the last: leave
            Assert.Equal(LeaveDirection.Next, seen[0].Direction);

            Assert.True(field.HandleKey(Key.Tab, KeyModifiers.Shift));
            Assert.Equal(new TextPosition(0, 10), field.Editor.Caret);
            Assert.Single(seen);
            Assert.False(field.HandleKey(Key.Tab, KeyModifiers.Shift));
            Assert.Equal(LeaveDirection.Previous, seen[1].Direction);

            Assert.True(field.HandleKey(Key.Tab, KeyModifiers.Control));
            Assert.Equal(new TextPosition(0, 21), field.Editor.Caret);
            Assert.True(field.HandleKey(Key.Tab, KeyModifiers.Control | KeyModifiers.Shift));
            Assert.Equal(new TextPosition(0, 7), field.Editor.Caret);
            Assert.Equal(2, seen.Count);
        });

        [Fact]
        public void Alt_Tab_is_not_ours() => Headless.Run(() =>
        {
            var (field, seen) = Watch(OneParagraph("abc"));
            Assert.False(field.HandleKey(Key.Tab, KeyModifiers.Alt));
            Assert.Empty(seen);
        });

        [Fact]
        public void Left_at_the_start_and_Right_at_the_end_ask_to_leave_but_inside_the_text_they_do_not() => Headless.Run(() =>
        {
            var (field, seen) = Watch(OneParagraph("abc"));
            field.Editor.SetCaret(new TextPosition(0, 1));
            field.Press(Key.Left);
            Assert.Empty(seen);
            field.Press(Key.Left);
            Assert.Single(seen);
            Assert.Equal((LeaveDirection.Previous, LeaveKey.Left), (seen[0].Direction, seen[0].Key));
            field.Editor.SetCaret(new TextPosition(0, 3));
            field.Press(Key.Right);
            Assert.Equal((LeaveDirection.Next, LeaveKey.Right), (seen[1].Direction, seen[1].Key));
            Assert.Equal(2, seen.Count);
        });

        [Fact]
        public void Arrows_that_collapse_a_selection_or_extend_it_do_not_leave() => Headless.Run(() =>
        {
            var (field, seen) = Watch(OneParagraph("abc"));
            field.Editor.SetCaret(new TextPosition(0, 0));
            field.Editor.SetCaret(new TextPosition(0, 2), true);
            field.Press(Key.Left);   // collapses to the start of the selection
            Assert.Empty(seen);
            field.Editor.SetCaret(new TextPosition(0, 3));
            field.Press(Key.Right, KeyModifiers.Shift);
            Assert.Empty(seen);
        });

        [Fact]
        public void In_a_right_to_left_paragraph_Left_at_the_logical_end_leaves_forwards() => Headless.Run(() =>
        {
            var doc = Basic();
            var (field, seen) = Watch(doc);
            var arabic = doc.Paragraphs[1];
            arabic.Rtl = true;
            field.Editor.SetCaret(new TextPosition(1, arabic.Length));
            field.Press(Key.Left);
            Assert.Equal((LeaveDirection.Next, LeaveKey.Left), (seen[0].Direction, seen[0].Key));
            field.Press(Key.Right);   // moves one back in the logical text
            Assert.Single(seen);
        });

        [Fact]
        public void Up_on_the_first_line_and_Down_on_the_last_ask_to_leave_with_the_caret_x_and_an_unhandled_request_still_moves_to_the_end() => Headless.Run(() =>
        {
            var (field, seen) = Watch(OneParagraph("abcdef"));
            field.Editor.SetCaret(new TextPosition(0, 3));
            field.Press(Key.Up);
            Assert.Equal((LeaveDirection.Previous, LeaveKey.Up), (seen[0].Direction, seen[0].Key));
            Assert.True(seen[0].GoalX > 0);
            Assert.Equal(0, field.Editor.Caret.Offset);    // unhandled: the old behaviour
            field.Editor.SetCaret(new TextPosition(0, 3));
            field.Press(Key.Down);
            Assert.Equal(LeaveDirection.Next, seen[1].Direction);
            Assert.Equal(6, field.Editor.Caret.Offset);
        });

        [Fact]
        public void A_handled_Up_leaves_the_caret_where_it_was_and_Shift_Up_never_asks() => Headless.Run(() =>
        {
            var (field, seen) = Watch(OneParagraph("abcdef"), handle: true);
            field.Editor.SetCaret(new TextPosition(0, 3));
            field.Press(Key.Up);
            Assert.Equal(3, field.Editor.Caret.Offset);
            field.Press(Key.Up, KeyModifiers.Shift);
            Assert.Single(seen);
        });

        [Fact]
        public void Up_and_Down_between_paragraphs_stay_inside_the_field() => Headless.Run(() =>
        {
            var (field, seen) = Watch(Doc(Para("en", "en", "one"), Para("en", "en", "two")));
            field.Editor.SetCaret(new TextPosition(0, 1));
            field.Press(Key.Down);
            Assert.Equal(1, field.Editor.Caret.Paragraph);
            field.Press(Key.Up);
            Assert.Equal(0, field.Editor.Caret.Paragraph);
            Assert.Empty(seen);
        });
    }
}
