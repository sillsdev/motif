using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SIL.TextEditing.Avalonia;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.RealClipboard.Tests
{
    /// <summary>A fact that is opt-in: it runs only on Windows and only when the environment variable <see cref="Switch"/> is 1, and is skipped with that message
    /// otherwise. It uses the real clipboard of the person running it (and overwrites it), and the shared GitHub runner's non-interactive session crashed the
    /// net48 test host here, so it is not part of the default run.</summary>
    public sealed class RealClipboardFactAttribute : FactAttribute
    {
        public const string Switch = "TEXTEDITING_REAL_CLIPBOARD";

        public RealClipboardFactAttribute()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) Skip = "needs the real Windows clipboard";
            else if (Environment.GetEnvironmentVariable(Switch) != "1")
                Skip = "opt-in: set " + Switch + "=1 to run it (it overwrites your clipboard; needs an interactive Windows desktop)";
        }
    }

    public class App : Application
    {
    }

    /// <summary>One real Avalonia application (Win32 platform, no headless) on a thread of its own for the whole test assembly; test bodies run on its UI thread.</summary>
    static class Ui
    {
        static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);
        static readonly Lazy<string?> Started = new Lazy<string?>(Start);

        /// <summary>Null when the Win32 platform started, else why it could not (the tests then end without checking anything, saying so, instead of crashing the host).</summary>
        static string? Start()
        {
            try { return StartCore(); }
            catch (Exception e) { return e.GetType().Name + ": " + e.Message; }
        }

        static string? StartCore()
        {
            var ready = new ManualResetEventSlim();
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    AppBuilder.Configure<App>().UsePlatformDetect().SetupWithoutStarting();
                    ready.Set();
                    Dispatcher.UIThread.MainLoop(CancellationToken.None);
                }
                catch (Exception e)
                {
                    failure = e;
                    ready.Set();
                }
            }) { IsBackground = true, Name = "real clipboard UI" };
#pragma warning disable CA1416
            thread.SetApartmentState(ApartmentState.STA);
#pragma warning restore CA1416
            thread.Start();
            if (!ready.Wait(Limit)) return "the Avalonia application did not start within " + Limit.TotalSeconds + " s";
            return failure == null ? null : "the Avalonia application did not start: " + failure.Message;
        }

        /// <summary>Runs the body on the UI thread, in turn with the other test processes. A body or a turn that takes over 60 s fails the test. When the platform
        /// cannot start the body is not run and the reason is written out (an opt-in test on a machine without a desktop has nothing to check).</summary>
        public static void Run(Func<Task> body)
        {
            var notStarted = Started.Value;
            if (notStarted != null)
            {
                Console.WriteLine("real clipboard test not run: " + notStarted);
                return;
            }
            // The test run starts one process per target framework at the same time, and they would overwrite each other's clipboard: take turns across processes.
            using (var turn = new Mutex(false, "Global\\SIL.TextEditing.RealClipboard.Tests"))
            {
                bool owned;
                try { owned = turn.WaitOne(Limit); }
                catch (AbandonedMutexException) { owned = true; }
                if (!owned) throw new TimeoutException("another test process kept the clipboard for over " + Limit.TotalSeconds + " s");
                try
                {
                    var running = Dispatcher.UIThread.InvokeAsync(body);
                    if (!running.Wait(Limit)) throw new TimeoutException("the clipboard test took over " + Limit.TotalSeconds + " s");
                    running.GetAwaiter().GetResult();
                }
                finally { turn.ReleaseMutex(); }
            }
        }
    }

    /// <summary>Bytes and text through the default backend and the operating system's own clipboard, on whichever Avalonia line the target framework uses.</summary>
    public class RealClipboardTests
    {
        static readonly byte[] NotText = { 0xFF, 0xFE, 0xC0, 0x80, 0x00, 0x01, 0x80, 0xFF, 0x00 };
        const string Arabic = "العربية مرحبا";

        static async Task WithField(Func<RichTextField, Task> body)
        {
            var field = new RichTextField(OneParagraph("x"));
            var window = new Window { Width = 300, Height = 200, Content = field };
            window.Show();
            try { await body(field); }
            finally { window.Close(); }
        }

        /// <summary>The real clipboard can be busy for a moment, as a real user's is: Avalonia's reads report a busy clipboard as nothing, so a read that finds
        /// nothing is asked again a few times before the test believes it.</summary>
        static async Task<T> Retry<T>(Func<Task<T>> action)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    var result = await action();
                    if (result != null || attempt >= 20) return result;
                }
                catch (Exception) when (attempt < 20) { }
                await Task.Delay(100);
            }
        }

        [RealClipboardFact]
        public void Bytes_that_are_not_text_come_back_as_the_same_bytes() => Ui.Run(() => WithField(async field =>
        {
            var backend = field.ClipboardBackend;
            await Retry(() => backend.SetAsync("plain", new List<KeyValuePair<string, object>> { new KeyValuePair<string, object>("SIL.TextEditing.Test.Bytes", NotText) }));
            var back = await Retry(() => backend.GetAsync("SIL.TextEditing.Test.Bytes", ClipboardPayloadKind.Bytes));
            Assert.Equal(NotText, Assert.IsType<byte[]>(back));
        }));

        [RealClipboardFact]
        public void Arabic_text_comes_back_as_the_same_text_and_the_plain_text_too() => Ui.Run(() => WithField(async field =>
        {
            var backend = field.ClipboardBackend;
            await Retry(() => backend.SetAsync(Arabic, new List<KeyValuePair<string, object>> { new KeyValuePair<string, object>("SIL.TextEditing.Test.Text", Arabic) }));
            Assert.Equal(Arabic, await Retry(() => backend.GetAsync("SIL.TextEditing.Test.Text", ClipboardPayloadKind.Text)));
            Assert.Equal(Arabic, await Retry(() => backend.GetTextAsync()));
        }));

        [RealClipboardFact]
        public void Text_and_bytes_in_one_copy_are_each_read_back_as_what_they_are() => Ui.Run(() => WithField(async field =>
        {
            var backend = field.ClipboardBackend;
            await Retry(() => backend.SetAsync("plain", new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>("SIL.TextEditing.Test.Text", Arabic),
                new KeyValuePair<string, object>("SIL.TextEditing.Test.Bytes", NotText),
            }));
            Assert.Equal(Arabic, await Retry(() => backend.GetAsync("SIL.TextEditing.Test.Text", ClipboardPayloadKind.Text)));
            Assert.Equal(NotText, Assert.IsType<byte[]>(await Retry(() => backend.GetAsync("SIL.TextEditing.Test.Bytes", ClipboardPayloadKind.Bytes))));
        }));
    }
}
