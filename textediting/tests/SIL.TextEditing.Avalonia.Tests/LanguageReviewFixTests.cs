using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Headless;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using SIL.TextEditing.Tests;
using Xunit;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>The review of PR 127 (ADR 0030): the popup lives as long as its context, the host can ask which keys the field wants, the popup and the field are visible to
    /// UI Automation, cancellation, back-off, debounce, a background service, high contrast and the mouse in the popup.</summary>
    public class LanguageReviewFixTests
    {
        static readonly string[] Words = { "alpha", "alps", "alto", "beta", "gamma" };

        static FakeLanguageService Service() => new FakeLanguageService
        {
            Triggers = new[] { '[' },
            Completions = (text, position, kind) =>
            {
                int caret = position.Character;
                int start = caret;
                while (start > 0 && text[start - 1] != ' ') start--;
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

        // ------------------------------------------------ the popup lives as long as its context (P2-1)

        [Fact]
        public void Typing_a_space_leaves_the_word_being_completed_and_the_popup_closes() => Headless.Run(() =>
        {
            var r = Rig("al", 2);
            r.Field.Press(Key.Space, KeyModifiers.Control);
            Assert.True(r.Field.IsCompletionOpen);
            r.Field.Editor.TypeText("p");
            r.Idle();
            Assert.True(r.Field.IsCompletionOpen);              // still the same word
            r.Field.Editor.TypeText(" ");
            r.Idle();
            Assert.False(r.Field.IsCompletionOpen);              // the next word is another context
            r.Close();
        });

        [Fact]
        public void With_the_popup_gone_Enter_reaches_the_host_and_Tab_leaves_the_field() => Headless.Run(() =>
        {
            var r = Rig("al", 2);
            int enters = 0, leaves = 0;
            r.Field.EnterRequested += m => enters++;
            r.Field.LeaveRequested += (o, e) => leaves++;
            r.Field.Press(Key.Space, KeyModifiers.Control);
            r.Field.Editor.TypeText(" ");
            r.Idle();
            r.Field.Editor.TypeText("zz");
            r.Idle();
            r.Field.Press(Key.Enter);
            r.Field.Press(Key.Tab);
            Assert.Equal(1, enters);
            Assert.Equal(1, leaves);
            r.Close();
        });

        [Fact]
        public void Deleting_back_before_where_the_context_began_closes_the_popup() => Headless.Run(() =>
        {
            var r = Rig("x al", 4);
            r.Field.Press(Key.Space, KeyModifiers.Control);
            r.Field.Press(Key.Back);
            r.Field.Press(Key.Back);
            r.Idle();
            r.Field.Press(Key.Back);
            r.Idle();
            Assert.False(r.Field.IsCompletionOpen);
            r.Close();
        });

        // ------------------------------------------------ the host's side of the keys (P2-2)

        [Fact]
        public void The_field_says_which_keys_it_wants_while_the_popup_is_open() => Headless.Run(() =>
        {
            var r = Rig("al", 2);
            Assert.True(r.Field.WantsKey(Key.Space, KeyModifiers.Control));
            Assert.False(r.Field.WantsKey(Key.Enter, KeyModifiers.None));       // closed: the host's
            Assert.False(r.Field.WantsKey(Key.Escape, KeyModifiers.None));
            r.Field.Press(Key.Space, KeyModifiers.Control);
            foreach (var key in new[] { Key.Enter, Key.Tab, Key.Escape, Key.Up, Key.Down, Key.PageUp, Key.PageDown })
                Assert.True(r.Field.WantsKey(key, KeyModifiers.None), key.ToString());
            Assert.False(r.Field.WantsKey(Key.Tab, KeyModifiers.Shift));
            Assert.False(r.Field.WantsKey(Key.Enter, KeyModifiers.Control));
            Assert.False(r.Field.WantsKey(Key.A, KeyModifiers.None));
            r.Field.Press(Key.Escape);
            Assert.False(r.Field.WantsKey(Key.Enter, KeyModifiers.None));
            r.Field.LanguageService = null;
            Assert.False(r.Field.WantsKey(Key.Space, KeyModifiers.Control));
            r.Close();
        });

        // ------------------------------------------------ UI Automation (P2-3, P3-14)

        [Fact]
        public void The_popup_is_a_list_of_list_items_in_the_content_view_with_their_names() => Headless.Run(() =>
        {
            var r = Rig("al", 2);
            r.Field.Press(Key.Space, KeyModifiers.Control);
            var list = r.Field.CompletionList!;
            var listPeer = ControlAutomationPeer.CreatePeerForElement(list);
            Assert.Equal(AutomationControlType.List, listPeer.GetAutomationControlType());
            Assert.True(listPeer.IsControlElement());
            Assert.Equal("Completions", listPeer.GetName());
            for (int i = 0; i < 3; i++)
            {
                var peer = ControlAutomationPeer.CreatePeerForElement(r.Field.CompletionRow(i)!);
                Assert.Equal(AutomationControlType.ListItem, peer.GetAutomationControlType());
                Assert.True(peer.IsControlElement());
                Assert.True(peer.IsContentElement());
                Assert.Equal(Words[i] + ", word", peer.GetName());
            }
            r.Close();
        });

        [Fact]
        public void The_field_peer_says_whether_the_popup_is_open_and_reports_the_worst_diagnostic_and_the_selection() => Headless.Run(() =>
        {
            var service = Service();
            service.Analysis = t => new LanguageAnalysis(new LanguageToken[0], new[]
            {
                new LanguageDiagnostic(new LanguageRange(0, 1), DiagnosticSeverity.Warning, "mild"),
                new LanguageDiagnostic(new LanguageRange(1, 2), DiagnosticSeverity.Error, "bad one"),
            });
            var r = Rig("al", 2, service);
            var peer = ControlAutomationPeer.CreatePeerForElement(r.Field);
            var pattern = Assert.IsAssignableFrom<IExpandCollapseProvider>(peer);
            Assert.Equal(ExpandCollapseState.Collapsed, pattern.ExpandCollapseState);
            Assert.Equal("Error: bad one", peer.GetItemStatus());
            pattern.Expand();
            Assert.True(r.Field.IsCompletionOpen);
            Assert.Equal(ExpandCollapseState.Expanded, pattern.ExpandCollapseState);
            Assert.Equal("Error: bad one; completion list open, 3 items, alpha selected", peer.GetItemStatus());
            r.Field.Press(Key.Down);
            Assert.Contains("alps selected", peer.GetItemStatus());
            pattern.Collapse();
            Assert.False(r.Field.IsCompletionOpen);
            Assert.Equal(ExpandCollapseState.Collapsed, pattern.ExpandCollapseState);
            r.Close();
        });

        [Fact]
        public void With_nothing_wrong_and_no_popup_the_field_has_no_status() => Headless.Run(() =>
        {
            var r = Rig("al", 2);
            Assert.Null(ControlAutomationPeer.CreatePeerForElement(r.Field).GetItemStatus() is { Length: > 0 } s ? s : null);
            r.Close();
        });

        // ------------------------------------------------ cancellation (P3-3), back-off (P3-5), trigger (P3-13)

        [Fact]
        public void A_completion_that_is_superseded_or_closed_is_cancelled_in_the_service() => Headless.Run(() =>
        {
            var r = Rig("al", 2, new FakeLanguageService { Manual = true });
            r.Field.Press(Key.Space, KeyModifiers.Control);
            r.Field.Press(Key.Space, KeyModifiers.Control);
            Assert.True(r.Service.CompletionTokens[0].IsCancellationRequested);
            Assert.False(r.Service.CompletionTokens[1].IsCancellationRequested);
            r.Field.CloseCompletion();
            Assert.True(r.Service.CompletionTokens[1].IsCancellationRequested);
            r.Close();
        });

        [Fact]
        public void A_failed_hover_does_not_switch_analysis_off() => Headless.Run(() =>
        {
            var r = new LanguageRig("one bad two", new FakeLanguageService { Analysis = FakeLanguageService.Words("word"), HoverThrows = new InvalidOperationException("hover") });
            var failures = new List<Exception>();
            r.Field.LanguageServiceFailed += failures.Add;
            r.Idle();
            _ = r.Field.GetLanguageHoverAsync(new TextPosition(0, 1));
            Assert.Equal("hover", Assert.Single(failures).Message);
            r.Field.Editor.SetCaret(new TextPosition(0, 11));
            r.Field.Editor.TypeText("x");
            r.Idle();
            Assert.Equal(2, r.Service.AnalyzedTexts.Count);        // still asking: the hover failure cost that answer only
            r.Close();
        });

        [Fact]
        public void A_hover_is_cancelled_when_the_pointer_moves_to_another_place() => Headless.Run(() =>
        {
            var r = new LanguageRig("one bad two", new FakeLanguageService { Analysis = FakeLanguageService.Words("word"), HoverOf = (t, p) => new LanguageHover("h") });
            r.Idle();
            Point Over(int a, int b)
            {
                var rect = r.Field.ParagraphLayoutFor(0).SelectionRects(a, b)[0];
                return r.Field.TranslatePoint(new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2), r.Window)!.Value;
            }
            r.Window.MouseMove(Over(0, 3));
            Headless.Pump();
            r.Window.MouseMove(Over(8, 11));
            Headless.Pump();
            Assert.True(r.Service.HoverTokens[0].IsCancellationRequested);
            Assert.False(r.Service.HoverTokens[r.Service.HoverTokens.Count - 1].IsCancellationRequested);
            r.Close();
        });

        [Fact]
        public void A_failed_analysis_holds_off_that_text_only_and_the_next_edit_asks_again() => Headless.Run(() =>
        {
            var r = new LanguageRig("a bad", new FakeLanguageService { Throws = new InvalidOperationException("boom") });
            var failures = new List<Exception>();
            r.Field.LanguageServiceFailed += failures.Add;
            r.Idle();
            r.Idle();
            Assert.Single(r.Service.AnalyzedTexts);              // the same text is not asked again
            r.Service.Throws = null;
            r.Service.Analysis = FakeLanguageService.Words("word");
            r.Field.Editor.SetCaret(new TextPosition(0, 5));
            r.Field.Editor.TypeText("x");
            r.Idle();
            Assert.Equal(2, r.Service.AnalyzedTexts.Count);
            Assert.Equal(2, r.Field.LanguageTokens.Count);
            r.Close();
        });

        [Fact]
        public void A_service_that_keeps_cancelling_what_nobody_cancelled_is_asked_a_few_times_not_for_ever() => Headless.Run(() =>
        {
            var r = new LanguageRig("a b", new FakeLanguageService { CancelsItself = true, Analysis = FakeLanguageService.Words("word") });
            r.Idle();
            Assert.Equal(4, r.Service.AnalyzedTexts.Count);      // once, and three more
            r.Idle();
            Assert.Equal(4, r.Service.AnalyzedTexts.Count);
            // A repaint is not a reason to ask again. The test paints on purpose: when the headless renderer paints by itself is a matter of timing (issue 145).
            for (int i = 0; i < 3; i++)
            {
                Fixtures.Render(r.Field);
                r.Idle();
            }
            Assert.Equal(4, r.Service.AnalyzedTexts.Count);
            r.Service.CancelsItself = false;
            r.Field.Editor.SetCaret(new TextPosition(0, 3));
            r.Field.Editor.TypeText("c");
            r.Idle();
            Assert.Equal(5, r.Service.AnalyzedTexts.Count);
            Assert.Equal(2, r.Field.LanguageTokens.Count);
            r.Close();
        });

        [Fact]
        public void A_service_that_gave_up_is_asked_again_when_it_says_it_changed_and_when_the_field_is_restarted() => Headless.Run(() =>
        {
            var r = new LanguageRig("a b", new FakeLanguageService { CancelsItself = true, Analysis = FakeLanguageService.Words("word") });
            r.Idle();
            Assert.Equal(4, r.Service.AnalyzedTexts.Count);
            r.Service.CancelsItself = false;
            r.Service.RaiseChanged();
            r.Idle();
            Assert.Equal(5, r.Service.AnalyzedTexts.Count);
            Assert.Equal(2, r.Field.LanguageTokens.Count);
            r.Close();
        });

        [Fact]
        public void A_trigger_character_typed_with_the_popup_open_is_asked_as_the_trigger_and_not_as_a_refresh() => Headless.Run(() =>
        {
            var r = Rig("al", 2);
            r.Field.Press(Key.Space, KeyModifiers.Control);
            r.Service.CompletionKinds.Clear();
            r.Field.Type("[");
            r.Idle();
            Assert.Equal(new[] { CompletionTriggerKind.TriggerCharacter }, r.Service.CompletionKinds.ToArray());
            r.Close();
        });

        // ------------------------------------------------ debounce and background (P3-4)

        [Fact]
        public void With_a_debounce_the_service_is_asked_when_typing_has_paused_and_every_edit_starts_the_wait_again() => Headless.Run(() =>
        {
            var r = new LanguageRig("one", attach: false);
            var waits = new List<(TimeSpan Interval, Action Run, bool[] Cancelled)>();
            r.Field.LanguageScheduler = (interval, run) =>
            {
                var cancelled = new bool[1];
                waits.Add((interval, run, cancelled));
                return new Cancel(() => cancelled[0] = true);
            };
            r.Field.LanguageDebounce = TimeSpan.FromMilliseconds(200);
            r.Field.LanguageService = r.Service;
            r.Idle();
            Assert.Single(r.Service.AnalyzedTexts);                // the first answer for a new service is not held back
            r.Field.Editor.SetCaret(new TextPosition(0, 3));
            r.Field.Editor.TypeText("a");
            r.Field.Editor.TypeText("b");
            r.Idle();
            Assert.Single(r.Service.AnalyzedTexts);                // waiting
            Assert.True(waits.Count >= 2);
            for (int i = 0; i < waits.Count - 1; i++) Assert.True(waits[i].Cancelled[0]);   // every edit restarted the wait
            Assert.False(waits[waits.Count - 1].Cancelled[0]);
            Assert.Equal(TimeSpan.FromMilliseconds(200), waits[waits.Count - 1].Interval);
            waits[waits.Count - 1].Run();
            r.Idle();
            Assert.Equal(new[] { "one", "oneab" }, r.Service.AnalyzedTexts.ToArray());
            r.Close();
        });

        sealed class Cancel : IDisposable
        {
            readonly Action _action;
            public Cancel(Action action) { _action = action; }
            public void Dispose() => _action();
        }

        sealed class OffThread : BackgroundTextLanguageService
        {
            public int Thread;
            public readonly ManualResetEventSlim Ran = new ManualResetEventSlim();
            protected override LanguageAnalysis Analyze(string text)
            {
                Thread = Environment.CurrentManagedThreadId;
                Ran.Set();
                return LanguageAnalysis.Empty;
            }
        }

        [Fact]
        public void The_background_service_runs_Analyze_off_the_callers_thread() => Headless.Run(() =>
        {
            var service = new OffThread();
            var task = service.AnalyzeAsync("x", CancellationToken.None);
            Assert.True(service.Ran.Wait(60000));            // waited for by event: waiting on the task itself could run it on this thread
            var analysis = Wait(task);
            Assert.Same(LanguageAnalysis.Empty, analysis);
            Assert.NotEqual(Environment.CurrentManagedThreadId, service.Thread);
        });

        static T Wait<T>(Task<T> task) => task.GetAwaiter().GetResult();

        // ------------------------------------------------ the popup's look and mouse (P3-1, 8, 9)

        [Fact]
        public void The_popup_is_aligned_to_the_left_of_the_caret_not_centred_on_it() => Headless.Run(() =>
        {
            var r = Rig("al", 2);
            r.Field.Press(Key.Space, KeyModifiers.Control);
            Assert.Equal(PlacementMode.BottomEdgeAlignedLeft, r.Field.CompletionPopup!.Placement);
            r.Close();
        });

        [Fact]
        public void In_a_high_contrast_theme_the_selected_row_is_in_the_systems_highlight_text_colour() => Headless.Run(() =>
        {
            var probe = SystemColours.Probe;
            var get = SystemColours.Get;
            SystemColours.Probe = () => true;
            SystemColours.Get = kind => kind == SystemColour.HighlightText ? Color.FromRgb(1, 2, 3) : kind == SystemColour.Highlight ? Color.FromRgb(200, 200, 0) : Color.FromRgb(250, 250, 250);
            try
            {
                var r = Rig("al", 2);
                r.Field.Press(Key.Space, KeyModifiers.Control);
                Color Colour(int row) => ((ISolidColorBrush)((TextBlock)((StackPanel)((Border)r.Field.CompletionRow(row)!).Child!).Children[0]).Foreground!).Color;
                Assert.Equal(Color.FromRgb(1, 2, 3), Colour(0));
                Assert.NotEqual(Color.FromRgb(1, 2, 3), Colour(1));
                r.Close();
            }
            finally { SystemColours.Probe = probe; SystemColours.Get = get; }
        });

        [Fact]
        public void The_wheel_scrolls_the_window_of_rows_without_moving_the_highlight_and_the_pointer_tints_a_row() => Headless.Run(() =>
        {
            var many = Enumerable.Range(0, 20).Select(i => new CompletionItem("item" + i.ToString("00"))).ToList();
            var r = Rig("", 0, new FakeLanguageService { Completions = (t, p, k) => many });
            r.Field.Press(Key.Space, KeyModifiers.Control);
            r.Field.ScrollCompletion(3);
            Assert.Equal("item03", r.Field.CompletionRowNames()[0]);
            Assert.Equal(0, r.Field.CompletionIndex);
            r.Field.ScrollCompletion(100);
            Assert.Equal("item12", r.Field.CompletionRowNames()[0]);       // stops at the last full window
            r.Field.ScrollCompletion(-100);
            Assert.Equal("item00", r.Field.CompletionRowNames()[0]);
            var plain = ((Border)r.Field.CompletionRow(2)!).Background;
            r.Field.HoverCompletion(2);
            Assert.NotEqual(Brushes.Transparent, ((Border)r.Field.CompletionRow(2)!).Background);
            Assert.NotSame(plain, ((Border)r.Field.CompletionRow(2)!).Background);
            r.Close();
        });
    }
}
