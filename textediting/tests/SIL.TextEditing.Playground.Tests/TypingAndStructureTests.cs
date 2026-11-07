using Avalonia.Input;
using Xunit;

namespace SIL.TextEditing.Playground.Tests
{
    public class TypingAndStructureTests
    {
        [Fact]
        public void Typing_inside_an_arabic_run_takes_ws_ar_and_the_label_and_inspector_show_it() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                int inside = s.ParagraphText(0).IndexOf("كتاب") + 2;
                s.Caret(0, inside);
                Assert.StartsWith("ws: ar", s.WsLabel);
                Assert.Contains("ws at caret ar", s.Inspector);
                Assert.Contains("would switch keyboard to ar", s.Log);

                s.Type("x");
                Assert.Contains("ws=ar \"كتxاب\"", s.Inspector);
                Assert.Contains("caret 0:" + (inside + 1), s.Inspector);

                s.Caret(0, 3);
                Assert.StartsWith("ws: en", s.WsLabel);
                Assert.Contains("would switch keyboard to en", s.Log);
            }
        });

        [Fact]
        public void Gaining_focus_logs_the_keyboard_the_host_would_switch_to_even_when_the_writing_system_did_not_change() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                int inside = s.ParagraphText(0).IndexOf("كتاب") + 2;
                s.Caret(0, inside);
                s.Find<global::Avalonia.Controls.TextBox>(PlaygroundView.Names.EventLog).Focus();   // the keyboard of another control is in effect
                Headless.Pump();
                int before = System.Linq.Enumerable.Count(s.View.EventLog, l => l.Contains("focus gained, ws ar: would switch keyboard to ar"));
                s.Field.Focus();
                Headless.Pump();
                Assert.Equal(before + 1, System.Linq.Enumerable.Count(s.View.EventLog, l => l.Contains("focus gained, ws ar: would switch keyboard to ar")));
            }
        });

        [Fact]
        public void The_writing_system_buttons_set_what_is_typed_next_and_the_label_follows() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Empty))
            {
                Assert.StartsWith("ws: en - English (analysis)", s.WsLabel);

                s.Click(PlaygroundView.Names.DefaultVernacular);
                Assert.StartsWith("ws: swh - Swahili (vernacular)", s.WsLabel);
                s.Type("habari");
                Assert.Contains("ws=swh \"habari\"", s.Inspector);

                s.Click(PlaygroundView.Names.WritingSystem("fr"));
                Assert.StartsWith("ws: fr", s.WsLabel);
                s.Type(" ami");
                Assert.Contains("ws=fr \" ami\"", s.Inspector);

                s.Click(PlaygroundView.Names.DefaultAnalysis);
                Assert.StartsWith("ws: en", s.WsLabel);
                Assert.Contains("would switch keyboard to swh", s.Log);
                Assert.Contains("would switch keyboard to fr", s.Log);
            }
        });

        [Fact]
        public void A_writing_system_button_with_a_selection_changes_the_selected_text_and_undo_restores_it() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                s.Select(0, 4, 8);
                s.Click(PlaygroundView.Names.WritingSystem("hi"));
                Assert.Contains("ws=hi \"word\"", s.Inspector);
                s.Click(PlaygroundView.Names.Undo);
                Assert.DoesNotContain("ws=hi", s.Inspector);
                Assert.Contains("ws=en \"The word \"", s.Inspector);
            }
        });

        [Fact]
        public void Split_and_join_keep_paragraph_key_in_the_inspector() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                var first = s.ParagraphKey(0);
                var second = s.ParagraphKey(1);
                int count = s.Editor.Document.Paragraphs.Count;

                s.Caret(0, 5);
                s.Press(Key.Enter);
                Assert.Equal(count + 1, s.Editor.Document.Paragraphs.Count);
                var right = s.ParagraphKey(1);
                Assert.Equal(first, s.ParagraphKey(0));
                Assert.Null(right);
                Assert.Equal(second, s.ParagraphKey(2));
                Assert.Contains("[0] key=" + first, s.Inspector);
                Assert.Contains("[1] key=(none)", s.Inspector);
                Assert.Contains("[2] key=" + second, s.Inspector);
                Assert.Contains("paragraphs " + (count + 1), s.Inspector);
                Assert.Contains("caret 1:0", s.Inspector);

                s.Press(Key.Back);
                Assert.Equal(count, s.Editor.Document.Paragraphs.Count);
                Assert.Equal(first, s.ParagraphKey(0));
                Assert.DoesNotContain("key=(none)", s.Inspector);
                Assert.Contains("[0] key=" + first, s.Inspector);
                Assert.Contains("[1] key=" + second, s.Inspector);
                Assert.Contains("caret 0:5", s.Inspector);
            }
        });

        [Fact]
        public void Enter_at_the_start_of_a_paragraph_leaves_the_original_identity_on_the_right() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                var original = s.ParagraphKey(0);
                s.Caret(0, 0);
                s.Press(Key.Enter);
                Assert.Null(s.ParagraphKey(0));
                Assert.Equal(string.Empty, s.ParagraphText(0));
                Assert.Equal(original, s.ParagraphKey(1));
                Assert.Contains("[1] key=" + original, s.Inspector);
            }
        });

        [Fact]
        public void Arrow_keys_move_the_caret_and_the_inspector_follows() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                s.Caret(0, 0);
                s.Press(Key.Right, times: 3);
                Assert.Contains("caret 0:3", s.Inspector);
                s.Press(Key.Right, KeyModifiers.Shift, 2);
                Assert.Contains("selection 0:3 -> 0:5", s.Inspector);
                s.Press(Key.Left);
                Assert.Contains("caret 0:3   anchor 0:3   selection none", s.Inspector);
                s.Press(Key.Left);
                Assert.Contains("caret 0:2", s.Inspector);
            }
        });
    }
}
