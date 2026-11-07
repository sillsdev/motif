using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using SIL.TextEditing.Tests;
using Xunit;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>ADR 0030: the completion popup. Ctrl+Space or a trigger character asks the service for the items at the caret; Up, Down, Enter, Tab and Esc work it
    /// while the keyboard stays in the field; the answer is used only for the text and the caret it was asked about.</summary>
    public class LanguageCompletionTests
    {
        static readonly string[] Words = { "alpha", "alps", "alto", "beta", "gamma" };

        /// <summary>Completes the word before the caret from <see cref="Words"/>; the item replaces what is typed of it.</summary>
        static FakeLanguageService Service() => new FakeLanguageService
        {
            Triggers = new[] { '[' },
            Completions = (text, position, kind) =>
            {
                int caret = position.Character;
                int start = text.LastIndexOf(' ', Math.Max(caret - 1, 0), Math.Max(caret, 1)) + 1;
                if (caret == 0) start = 0;
                string typed = text.Substring(start, caret - start);
                return Words.Where(w => w.StartsWith(typed, StringComparison.Ordinal)).Select(w => new CompletionItem(w, CompletionItemKind.Value, "word", w, new LanguageRange(start, caret))).ToList();
            },
        };

        static LanguageRig Rig(string text, int caret, FakeLanguageService? service = null)
        {
            var r = new LanguageRig(text, service ?? Service());
            r.Field.Focus();
            Headless.Pump();
            r.Field.Editor.SetCaret(new TextPosition(0, caret));
            r.Idle();
            return r;
        }

        static void Times(int n, Action action)
        {
            for (int i = 0; i < n; i++) action();
        }

        static string Text(LanguageRig r) => r.Field.Editor.Document.Paragraphs[0].Text;

        static string[] Labels(LanguageRig r) => r.Field.CompletionItems.Select(i => i.Label).ToArray();

        [Fact]
        public void Ctrl_Space_asks_the_service_at_the_caret_and_opens_the_popup_with_a_named_row_for_each_item() => Headless.Run(() =>
        {
            var r = Rig("x al", 4);
            Assert.False(r.Field.IsCompletionOpen);
            r.Field.Press(Key.Space, KeyModifiers.Control);
            Assert.True(r.Field.IsCompletionOpen);
            Assert.Equal(new[] { "alpha", "alps", "alto" }, Labels(r));
            Assert.Equal(0, r.Field.CompletionIndex);
            Assert.Equal(new[] { CompletionTriggerKind.Invoked }, r.Service.CompletionKinds.ToArray());
            Assert.Equal(new[] { "alpha, word", "alps, word", "alto, word" }, r.Field.CompletionRowNames().ToArray());
            Assert.True(r.Field.IsFocused);               // the keyboard stays with the field (that this window has no theme, so no overlay layer for the popup, changes nothing here)
            r.Close();
        });

        [Fact]
        public void Ctrl_Space_with_no_completions_opens_nothing_and_an_empty_answer_closes_an_open_popup() => Headless.Run(() =>
        {
            var r = Rig("zz", 2);
            r.Field.Press(Key.Space, KeyModifiers.Control);
            Assert.False(r.Field.IsCompletionOpen);
            r.Field.Editor.TypeText(" a");
            r.Field.Press(Key.Space, KeyModifiers.Control);
            Assert.True(r.Field.IsCompletionOpen);
            r.Field.Editor.TypeText("x");
            r.Idle();
            Assert.False(r.Field.IsCompletionOpen);
            r.Close();
        });

        [Fact]
        public void Down_and_Up_move_the_highlight_and_stop_at_the_ends_and_the_caret_stays_put() => Headless.Run(() =>
        {
            var r = Rig("al", 2);
            r.Field.Press(Key.Space, KeyModifiers.Control);
            r.Field.Press(Key.Down);
            Assert.Equal(1, r.Field.CompletionIndex);
            Times(5, () => r.Field.Press(Key.Down));
            Assert.Equal(2, r.Field.CompletionIndex);
            r.Field.Press(Key.Up);
            Assert.Equal(1, r.Field.CompletionIndex);
            Times(5, () => r.Field.Press(Key.Up));
            Assert.Equal(0, r.Field.CompletionIndex);
            Assert.Equal(new TextPosition(0, 2), r.Field.Editor.Caret);
            Assert.True(r.Field.IsCompletionOpen);
            r.Close();
        });

        [Fact]
        public void PageDown_moves_a_page_of_rows_and_the_popup_shows_only_a_window_of_them() => Headless.Run(() =>
        {
            var many = Enumerable.Range(0, 20).Select(i => new CompletionItem("item" + i.ToString("00"))).ToList();
            var r = Rig("", 0, new FakeLanguageService { Completions = (t, p, k) => many });
            r.Field.Press(Key.Space, KeyModifiers.Control);
            Assert.Equal(RichTextField.CompletionRows, r.Field.CompletionRowNames().Count);
            r.Field.Press(Key.PageDown);
            Assert.Equal(RichTextField.CompletionRows, r.Field.CompletionIndex);
            Assert.Equal("item01", r.Field.CompletionRowNames()[0]);          // the window followed the highlight
            Times(9, () => r.Field.Press(Key.PageDown));
            Assert.Equal(19, r.Field.CompletionIndex);
            Assert.Equal("item19", r.Field.CompletionRowNames().Last());
            Times(9, () => r.Field.Press(Key.PageUp));
            Assert.Equal(0, r.Field.CompletionIndex);
            Assert.Equal("item00", r.Field.CompletionRowNames()[0]);
            r.Close();
        });

        [Fact]
        public void Enter_accepts_the_highlighted_item_in_place_of_what_is_typed_as_one_undo_step_and_does_not_reach_the_host() => Headless.Run(() =>
        {
            var r = Rig("x al y", 4);
            int enters = 0;
            r.Field.EnterRequested += m => enters++;
            r.Field.Press(Key.Space, KeyModifiers.Control);
            r.Field.Press(Key.Down);
            r.Field.Press(Key.Enter);
            Assert.Equal("x alps y", Text(r));
            Assert.Equal(new TextPosition(0, 6), r.Field.Editor.Caret);
            Assert.False(r.Field.IsCompletionOpen);
            Assert.Equal(0, enters);
            Assert.Equal(1, r.Undo.DoneSteps);
            Assert.True(r.Undo.Undo());
            Assert.Equal("x al y", Text(r));
            r.Close();
        });

        [Fact]
        public void Tab_accepts_too_and_does_not_leave_the_field() => Headless.Run(() =>
        {
            var r = Rig("al", 2);
            int leaves = 0;
            r.Field.LeaveRequested += (o, e) => leaves++;
            r.Field.Press(Key.Space, KeyModifiers.Control);
            r.Field.Press(Key.Tab);
            Assert.Equal("alpha", Text(r));
            Assert.Equal(0, leaves);
            r.Field.Press(Key.Tab);                       // with the popup closed Tab leaves, as it always did
            Assert.Equal(1, leaves);
            r.Close();
        });

        [Fact]
        public void Esc_closes_the_popup_and_is_the_hosts_again_when_it_is_closed() => Headless.Run(() =>
        {
            var r = Rig("al", 2);
            Assert.False(r.Field.HandleKey(Key.Escape, KeyModifiers.None));
            r.Field.Press(Key.Space, KeyModifiers.Control);
            Assert.True(r.Field.HandleKey(Key.Escape, KeyModifiers.None));
            Assert.False(r.Field.IsCompletionOpen);
            Assert.Equal("al", Text(r));
            Assert.False(r.Field.HandleKey(Key.Escape, KeyModifiers.None));
            r.Close();
        });

        [Fact]
        public void Enter_with_the_popup_closed_goes_to_the_host_as_before() => Headless.Run(() =>
        {
            var r = Rig("al", 2);
            int enters = 0;
            r.Field.EnterRequested += m => enters++;
            r.Field.Press(Key.Enter);
            Assert.Equal(1, enters);
            r.Close();
        });

        [Fact]
        public void Typing_with_the_popup_open_asks_again_when_idle_and_the_list_narrows_or_closes() => Headless.Run(() =>
        {
            var r = Rig("al", 2);
            r.Field.Press(Key.Space, KeyModifiers.Control);
            Assert.Equal(3, r.Field.CompletionItems.Count);
            r.Field.Editor.TypeText("p");
            Assert.True(r.Field.IsCompletionOpen);                  // the old list stays until the new one comes
            r.Idle();
            Assert.Equal(new[] { "alpha", "alps" }, Labels(r));
            Assert.Equal(CompletionTriggerKind.Refresh, r.Service.CompletionKinds.Last());
            r.Field.Editor.TypeText("h");
            r.Idle();
            Assert.Equal(new[] { "alpha" }, Labels(r));
            Assert.Equal(0, r.Field.CompletionIndex);
            r.Field.Editor.TypeText("x");
            r.Idle();
            Assert.False(r.Field.IsCompletionOpen);
            r.Close();
        });

        [Fact]
        public void Moving_the_caret_by_any_other_means_closes_the_popup() => Headless.Run(() =>
        {
            foreach (var key in new[] { Key.Left, Key.Right, Key.Home, Key.End })
            {
                var r = Rig("al", 1);
                r.Field.Press(Key.Space, KeyModifiers.Control);
                Assert.True(r.Field.IsCompletionOpen);
                r.Field.Press(key);
                Assert.False(r.Field.IsCompletionOpen);
                r.Close();
            }
            var s = Rig("al", 2);
            s.Field.Press(Key.Space, KeyModifiers.Control);
            s.Field.Editor.SetCaret(new TextPosition(0, 0));
            Assert.False(s.Field.IsCompletionOpen);
            s.Close();
        });

        [Fact]
        public void Selecting_text_or_losing_focus_or_becoming_read_only_closes_the_popup() => Headless.Run(() =>
        {
            var r = Rig("al", 2);
            r.Field.Press(Key.Space, KeyModifiers.Control);
            r.Field.Press(Key.Left, KeyModifiers.Shift);
            Assert.False(r.Field.IsCompletionOpen);
            r.Field.Editor.SetCaret(new TextPosition(0, 2));
            r.Field.Press(Key.Space, KeyModifiers.Control);
            Assert.True(r.Field.IsCompletionOpen);
            r.Field.IsReadOnly = true;
            Assert.False(r.Field.IsCompletionOpen);
            r.Field.Press(Key.Space, KeyModifiers.Control);       // and a read-only field opens none
            Assert.False(r.Field.IsCompletionOpen);
            r.Field.IsReadOnly = false;
            r.Field.Press(Key.Space, KeyModifiers.Control);
            Assert.True(r.Field.IsCompletionOpen);
#if NET10_0_OR_GREATER
            r.Window.FocusManager!.Focus(null);
#else
            r.Window.FocusManager!.ClearFocus();
#endif
            Headless.Pump();
            Assert.False(r.Field.IsCompletionOpen);
            r.Close();
        });

        [Fact]
        public void A_trigger_character_opens_the_popup_after_it_is_typed() => Headless.Run(() =>
        {
            var service = Service();
            service.Completions = (t, p, k) => new[] { new CompletionItem("[V]", CompletionItemKind.Class, "Vowels", "[V]", new LanguageRange(p.Character - 1, p.Character)) };
            var r = Rig("", 0, service);
            r.Field.Type("a");
            Assert.False(r.Field.IsCompletionOpen);
            r.Field.Type("[");
            Assert.True(r.Field.IsCompletionOpen);
            Assert.Equal(CompletionTriggerKind.TriggerCharacter, r.Service.CompletionKinds.Last());
            r.Field.Press(Key.Enter);
            Assert.Equal("a[V]", Text(r));
            r.Close();
        });

        [Fact]
        public void An_answer_for_a_caret_or_a_text_that_has_moved_on_is_dropped() => Headless.Run(() =>
        {
            var r = Rig("al", 2, new FakeLanguageService { Manual = true });
            r.Field.Press(Key.Space, KeyModifiers.Control);
            var (_, source) = Assert.Single(r.Service.PendingCompletions);
            r.Field.Editor.SetCaret(new TextPosition(0, 0));
            source.SetResult(new[] { new CompletionItem("alpha") });
            Assert.False(r.Field.IsCompletionOpen);

            r.Field.Editor.SetCaret(new TextPosition(0, 2));
            r.Field.Press(Key.Space, KeyModifiers.Control);
            r.Field.Editor.TypeText("x");
            r.Service.PendingCompletions[1].Source.SetResult(new[] { new CompletionItem("alpha") });
            Assert.False(r.Field.IsCompletionOpen);

            r.Field.Press(Key.Space, KeyModifiers.Control);
            r.Service.PendingCompletions[2].Source.SetResult(new[] { new CompletionItem("alpha") });
            Assert.True(r.Field.IsCompletionOpen);                  // asked and answered about the same text and caret
            r.Close();
        });

        [Fact]
        public void A_later_request_makes_an_earlier_answer_stale() => Headless.Run(() =>
        {
            var r = Rig("al", 2, new FakeLanguageService { Manual = true });
            r.Field.Press(Key.Space, KeyModifiers.Control);
            r.Field.Press(Key.Space, KeyModifiers.Control);
            r.Service.PendingCompletions[0].Source.SetResult(new[] { new CompletionItem("first") });
            Assert.False(r.Field.IsCompletionOpen);
            r.Service.PendingCompletions[1].Source.SetResult(new[] { new CompletionItem("second") });
            Assert.Equal(new[] { "second" }, Labels(r));
            r.Close();
        });

        [Fact]
        public void An_item_can_put_the_caret_inside_what_it_inserts_and_with_no_range_it_replaces_the_selection() => Headless.Run(() =>
        {
            var service = new FakeLanguageService { Completions = (t, p, k) => new[] { new CompletionItem("()", CompletionItemKind.Snippet, "optional item", "()", null, 1) } };
            var r = Rig("ab", 1, service);
            r.Field.Press(Key.Space, KeyModifiers.Control);
            r.Field.Press(Key.Enter);
            Assert.Equal("a()b", Text(r));
            Assert.Equal(new TextPosition(0, 2), r.Field.Editor.Caret);

            r.Field.Editor.SetCaret(new TextPosition(0, 1));
            r.Field.Editor.SetCaret(new TextPosition(0, 3), true);
            Assert.True(r.Field.Editor.HasSelection);
            r.Field.Press(Key.Space, KeyModifiers.Control);
            Assert.False(r.Field.IsCompletionOpen);                  // a selection gets no popup: the typed text would be replaced
            Assert.False(r.Field.AcceptCompletion());                // nothing open: false
            Assert.Equal("a()b", Text(r));
            r.Close();
        });

        [Fact]
        public void Accepting_is_refused_and_the_popup_closes_when_the_text_changed_behind_the_field() => Headless.Run(() =>
        {
            var r = Rig("al", 2);
            r.Field.Press(Key.Space, KeyModifiers.Control);
            r.Field.Editor.Document.Paragraphs[0].Runs[0].Text = "other";
            Assert.False(r.Field.AcceptCompletion());
            Assert.False(r.Field.IsCompletionOpen);
            Assert.Equal("other", Text(r));
            r.Close();
        });

        [Fact]
        public void An_input_method_composing_gets_no_popup_and_starting_to_compose_closes_it() => Headless.Run(() =>
        {
            var r = Rig("al", 2);
            r.Field.SetPreedit("zz", null);
            r.Field.Press(Key.Space, KeyModifiers.Control);
            Assert.False(r.Field.IsCompletionOpen);
            r.Field.SetPreedit(null, null);
            r.Field.Press(Key.Space, KeyModifiers.Control);
            Assert.True(r.Field.IsCompletionOpen);
            r.Field.SetPreedit("zz", null);
            Assert.False(r.Field.IsCompletionOpen);
            r.Close();
        });

        [Fact]
        public void A_field_without_a_service_leaves_Ctrl_Space_and_the_other_keys_alone() => Headless.Run(() =>
        {
            var r = new LanguageRig("al", attach: false);
            r.Field.Focus();
            Assert.False(r.Field.HandleKey(Key.Space, KeyModifiers.Control));
            Assert.False(r.Field.HandleKey(Key.Escape, KeyModifiers.None));
            Assert.False(r.Field.IsCompletionOpen);
            r.Close();
        });

        [Fact]
        public void The_popup_is_not_open_on_screen_when_the_field_is_in_no_window_but_the_state_works() => Headless.Run(() =>
        {
            var field = Fixtures.Field(TestDocs.OneParagraph("al"));
            field.LanguageService = Service();
            field.Editor.SetCaret(new TextPosition(0, 2));
            field.Press(Key.Space, KeyModifiers.Control);
            Assert.True(field.IsCompletionOpen);
            Assert.False(field.CompletionPopupIsOpen);
            Assert.True(field.AcceptCompletion());
            Assert.Equal("alpha", field.Editor.Document.Paragraphs[0].Text);
        });

        [Fact]
        public void Changes_to_the_popup_are_announced_so_a_host_can_follow_them() => Headless.Run(() =>
        {
            var r = Rig("al", 2);
            int changes = 0;
            r.Field.CompletionChanged += (o, e) => changes++;
            r.Field.Press(Key.Space, KeyModifiers.Control);
            Assert.Equal(1, changes);
            r.Field.Press(Key.Down);
            Assert.Equal(2, changes);
            r.Field.Press(Key.Escape);
            Assert.Equal(3, changes);
            r.Close();
        });
    }
}
