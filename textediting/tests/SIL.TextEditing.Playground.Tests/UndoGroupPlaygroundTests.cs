using System;
using System.Threading.Tasks;
using Avalonia.Input;
using Xunit;

namespace SIL.TextEditing.Playground.Tests
{
    /// <summary>ADR 0023 through the playground: typing is one undo step, and the inspector and the log show it.</summary>
    public class UndoGroupPlaygroundTests
    {
        [Fact]
        public void Typing_a_word_is_one_undo_step_that_the_Undo_button_takes_back_whole() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                s.Caret(0, 0);
                string before = s.ParagraphText(0);
                foreach (var c in "word") s.Type(c.ToString());
                Assert.Contains("undo 1 done, 0 undone (steps; 4 edits done)", s.Inspector);
                s.Click(PlaygroundView.Names.Undo);
                Assert.Equal(before, s.ParagraphText(0));
                Assert.Contains("undo 0 done, 1 undone", s.Inspector);
            }
        });

        [Fact]
        public void A_caret_key_ends_the_step_and_the_log_says_the_host_task_closed() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                s.Caret(0, 0);
                s.Type("a");
                s.Type("b");
                Assert.DoesNotContain("ended", s.Log);
                s.Caret(0, 0);
                Assert.Contains("ended", s.Log);
                s.Type("c");
                Assert.Contains("undo 2 done", s.Inspector);
                s.Press(Key.Z, KeyModifiers.Control);
                Assert.StartsWith("ab", s.ParagraphText(0));
            }
        });

        sealed class FakePause : IDisposable
        {
            public TimeSpan After;
            public Action Run = () => { };
            public bool Cancelled;
            public void Dispose() => Cancelled = true;
        }

        [Fact]
        public void A_pause_in_typing_closes_the_step_by_itself() => Headless.Run(() =>
        {
            // The pause is driven by a scheduler the test controls, not by wall time: the field asks for a timer of the editor's
            // pause, typing again cancels it and asks for a new one, and when it fires the step closes.
            using (var s = new Session(Scenarios.MixedBidi))
            {
                var timers = new System.Collections.Generic.List<FakePause>();
                s.Field.PauseScheduler = (after, run) =>
                {
                    var t = new FakePause { After = after, Run = run };
                    timers.Add(t);
                    return t;
                };
                // An hour, not milliseconds: the editor joins typing by wall-clock distance, so a slow machine must not be able to separate the two keystrokes.
                var pause = TimeSpan.FromHours(1);
                s.Editor.TypingPause = pause;
                s.Caret(0, 0);
                s.Type("a");
                Assert.True(s.Editor.HasOpenUndoGroup);
                var first = Assert.Single(timers.FindAll(t => !t.Cancelled));
                Assert.Equal(pause, first.After);
                s.Type("b");   // more typing before the pause passes: the first timer is cancelled and a new one starts
                Assert.True(first.Cancelled);
                var second = Assert.Single(timers.FindAll(t => !t.Cancelled));
                Assert.True(s.Editor.HasOpenUndoGroup);
                Assert.DoesNotContain("ended", s.Log);

                second.Run();   // the pause passes
                Assert.False(s.Editor.HasOpenUndoGroup);
                Assert.Contains("ended", s.Log);
            }
        });

        [Fact]
        public void The_default_pause_timer_closes_the_step_when_the_dispatcher_runs_it() => Headless.Run(async () =>
        {
            // The real DispatcherTimer path: no scheduler is set. The pause is one tick, so this waits for the dispatcher to run
            // the timer, not for a stretch of wall time, and only checks the step closed.
            using (var s = new Session(Scenarios.MixedBidi))
            {
                s.Editor.TypingPause = TimeSpan.FromTicks(1);
                s.Caret(0, 0);
                s.Type("a");
                for (int i = 0; i < 1000 && s.Editor.HasOpenUndoGroup; i++)
                {
                    await Task.Delay(10);
                    Headless.Pump();
                }
                Assert.False(s.Editor.HasOpenUndoGroup);
            }
        });
    }
}
