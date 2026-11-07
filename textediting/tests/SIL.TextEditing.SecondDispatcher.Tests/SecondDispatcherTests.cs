using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using SIL.TextEditing.Avalonia;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.SecondDispatcher.Tests
{
    /// <summary>A field on a dispatcher other than the main UI thread's (Avalonia 12; issue #68). These tests make a dispatcher on a
    /// second thread, which can change what <c>Dispatcher.UIThread</c> means for the rest of the process, so they have a test
    /// project and test host of their own (issue #72).</summary>
    public class SecondDispatcherTests
    {        /// <summary>Runs <paramref name="body"/> on a new thread that owns an Avalonia dispatcher of its own (Avalonia 12 lets a
        /// control belong to a dispatcher other than the main UI thread's), and returns once it has finished. The thread has none
        /// of the platform services the headless session gives the main UI thread, so a field there cannot be laid out.</summary>
        static void OnSecondDispatcher(Action<Dispatcher> body)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try { body(Dispatcher.CurrentDispatcher); }
                catch (Exception e) { failure = e; }
            });
            thread.Start();
            thread.Join();
            if (failure != null) throw new Exception("on the second dispatcher: " + failure.Message, failure);
        }

        static Dispatcher MainDispatcher()
        {
            Dispatcher? main = null;
            Headless.Run(() => main = Dispatcher.UIThread);
            return main!;
        }

        [Fact]
        public void A_field_on_a_second_dispatcher_takes_edits_from_its_own_thread_and_refuses_the_main_UI_thread()
        {
            var main = MainDispatcher();
            RichTextField? field = null;
            OnSecondDispatcher(d =>
            {
                field = new RichTextField(OneParagraph("abcd"));
                Assert.Same(d, field.Dispatcher);
                Assert.NotSame(main, field.Dispatcher);
                field.Editor.AccessCheck!();                      // its own thread: allowed
            });
            Exception? fromMain = null;
            Headless.Run(() => fromMain = Record.Exception(() => field!.Editor.TypeText("y")));
            Assert.IsType<InvalidOperationException>(fromMain);
            Assert.Contains("dispatcher the field belongs to", fromMain!.Message);
            Assert.Equal("abcd", field!.Editor.Document.Paragraphs[0].Text);   // refused before it touched the document
        }

        [Fact]
        public void The_pause_timer_fallback_for_a_field_on_a_second_dispatcher_is_handed_to_that_dispatcher_and_settles()
        {
            var main = MainDispatcher();
            var restart = typeof(RichTextField).GetMethod("RestartPauseTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var onScreen = typeof(RichTextField).GetField("_onScreen", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var scheduledOn = new List<int>();
            RichTextField? field = null;
            using var ready = new ManualResetEventSlim();
            using var asked = new ManualResetEventSlim();
            int secondThread = 0;
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var d = Dispatcher.CurrentDispatcher;
                    secondThread = Environment.CurrentManagedThreadId;
                    field = new RichTextField(OneParagraph("ab"));
                    field.PauseScheduler = (after, run) => { scheduledOn.Add(Environment.CurrentManagedThreadId); return new NoPause(); };
                    onScreen.SetValue(field, true);
                    // The commit starts the pause; the layout that follows it needs services this thread lacks, so it throws after.
                    Record.Exception(() => field.Editor.TypeText("x"));
                    Assert.True(field.Editor.HasOpenUndoGroup);
                    Assert.Equal(new[] { secondThread }, scheduledOn);
                    scheduledOn.Clear();
                    ready.Set();
                    if (!asked.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("the main thread never asked for the restart");
                    d.RunJobs();                    // the hand-off, if it was made to this dispatcher, runs here
                    d.RunJobs();                    // and is not posted again
                }
                catch (Exception e) { failure = e; ready.Set(); }
            });
            thread.IsBackground = true;   // a test host must still exit if this test fails before releasing the thread
            thread.Start();
            try
            {
                Assert.True(ready.Wait(TimeSpan.FromSeconds(30)), "the second dispatcher's thread did not get ready");
                if (failure == null)
                    Headless.Run(() => restart.Invoke(field, null));   // the main UI thread asks for the restart; the field is not its own
            }
            finally
            {
                asked.Set();
                Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "the second dispatcher's thread did not finish");
            }
            if (failure != null) throw new Exception("on the second dispatcher: " + failure.Message, failure);
            Assert.Equal(new[] { secondThread }, scheduledOn);     // scheduled once, on the field's thread
        }

        [Fact]
        public void A_change_of_the_system_colours_is_handled_on_the_dispatcher_of_the_field_not_the_main_one()
        {
            // The platform raises ColorValuesChanged on any thread; the field's colours may only be touched on its own.
            var ranOn = new List<int>();
            var isOn = SystemColours.Probe;
            SystemColours.Probe = () => { lock (ranOn) ranOn.Add(Environment.CurrentManagedThreadId); return false; };
            RichTextField? field = null;
            using var ready = new ManualResetEventSlim();
            using var raised = new ManualResetEventSlim();
            int secondThread = 0;
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var d = Dispatcher.CurrentDispatcher;
                    secondThread = Environment.CurrentManagedThreadId;
                    field = new RichTextField(OneParagraph("ab"));
                    ready.Set();
                    if (!raised.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("the main thread never raised the change");
                    d.RunJobs();   // the hand-off, if it was made to this dispatcher, runs here
                }
                catch (Exception e) { failure = e; ready.Set(); }
            });
            thread.IsBackground = true;
            thread.Start();
            try
            {
                Assert.True(ready.Wait(TimeSpan.FromSeconds(30)));
                if (failure == null)
                {
                    Headless.Run(() => field!.OnPlatformColoursChanged(new object(), null!));
                    Headless.Run(() => global::Avalonia.Threading.Dispatcher.UIThread.RunJobs());
                }
            }
            finally
            {
                raised.Set();
                Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
                SystemColours.Probe = isOn;
            }
            if (failure != null) throw new Exception("on the second dispatcher: " + failure.Message, failure);
            lock (ranOn) Assert.Equal(new[] { secondThread }, ranOn.Distinct().ToArray());
        }

        sealed class NoPause : IDisposable { public void Dispose() { } }
    }
}
