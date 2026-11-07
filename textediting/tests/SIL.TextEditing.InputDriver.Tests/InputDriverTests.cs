using System;
using System.Runtime.InteropServices;
using Xunit;

namespace SIL.TextEditing.InputDriver.Tests
{
    /// <summary>Decides once, before any test runs, whether this machine can give the host a foreground window and accept injected input. When it cannot,
    /// every driver test is skipped with the reason instead of failing: a runner without an interactive desktop is not an editor regression.</summary>
    static class Preflight
    {
        public static readonly string? SkipReason = Decide();

        static string? Decide()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "needs Windows input APIs";
            // A missing build is a setup fault the tests should report, not a reason to skip.
            if (!System.IO.File.Exists(PlaygroundHost.HostPath())) return null;
            try
            {
                using var host = PlaygroundHost.Launch(withChannel: false);
                var window = host.WaitForWindow(PlaygroundHost.Wait);
                if (window == IntPtr.Zero) return "the host opened no window, so there is no usable desktop";
                if (!Win32Input.BringToForeground(window, host.Pid, TimeSpan.FromSeconds(10)))
                    return "the host could not be made the foreground window (foreground belongs to process "
                           + Win32Input.ProcessOf(Win32Input.ForegroundWindow) + "); the desktop is locked or not interactive";
                return Win32Input.TypeUnicode("a") == 2 ? null : "SendInput accepted no events; the desktop is locked or not interactive";
            }
            catch (Exception e)
            {
                return "the host could not be driven: " + e.GetType().Name + ": " + e.Message;
            }
        }
    }

    public sealed class InputDriverFactAttribute : FactAttribute
    {
        public InputDriverFactAttribute() => Skip = Preflight.SkipReason;
    }

    /// <summary>A host started with its test channel, in the foreground, with the field clicked so it has focus. Fails with what it saw when any step does not hold.</summary>
    sealed class Session : IDisposable
    {
        public PlaygroundHost Host { get; }

        Session(PlaygroundHost host) => Host = host;

        public static Session Start()
        {
            var host = PlaygroundHost.Launch(withChannel: true);
            try
            {
                var window = host.WaitForWindow(PlaygroundHost.Wait);
                Assert.True(window != IntPtr.Zero, "the host opened no window");
                Assert.True(host.Connect(PlaygroundHost.Wait), "the host's test channel did not open");
                Assert.True(Win32Input.BringToForeground(window, host.Pid, TimeSpan.FromSeconds(10)),
                    "the host is not the foreground window; foreground is process " + Win32Input.ProcessOf(Win32Input.ForegroundWindow));
                var rect = host.Snapshot().FieldRect;
                Assert.Equal(2u, Win32Input.Click(rect[0] + rect[2] / 2, rect[1] + rect[3] / 2));
                return new Session(host);
            }
            catch
            {
                host.Dispose();
                throw;
            }
        }

        /// <summary>Types the units, checks Windows accepted every event, and returns the model once it holds exactly <paramref name="expected"/>.</summary>
        public Snapshot TypeAndRead(string text, string expected)
        {
            Assert.Equal((uint)(text.Length * 2), Win32Input.TypeUnicode(text));
            return ReadUntil(expected);
        }

        public Snapshot ReadUntil(string expected)
        {
            var snapshot = Host.WaitFor(s => s.AllText == expected, TimeSpan.FromSeconds(10));
            Assert.True(snapshot.AllText == expected, "expected [" + expected + "] but the model is " + snapshot + "; foreground is process "
                + Win32Input.ProcessOf(Win32Input.ForegroundWindow) + ", host is " + Host.Pid);
            return snapshot;
        }

        public void Dispose() => Host.Dispose();
    }

    public class InputDriverTests
    {
        [InputDriverFact]
        public void A_short_unicode_string_reaches_the_field_of_the_foreground_host()
        {
            using var session = Session.Start();
            var snapshot = session.TypeAndRead("abc", "abc");
            Assert.Equal("0:3", snapshot.Caret);
            Assert.Equal(snapshot.Caret, snapshot.Anchor);
            Assert.All(snapshot.Paragraphs[0], run => Assert.False(string.IsNullOrEmpty(run.Ws)));
        }

        [InputDriverFact]
        public void Arabic_typed_through_the_input_path_arrives_in_logical_order()
        {
            using var session = Session.Start();
            const string salaam = "سلام";
            var snapshot = session.TypeAndRead(salaam, salaam);
            Assert.Equal("0:4", snapshot.Caret);
        }

        [InputDriverFact]
        public void A_supplementary_plane_character_sent_as_a_surrogate_pair_arrives_as_one_character()
        {
            using var session = Session.Start();
            const string grinning = "\U0001F600";
            var snapshot = session.TypeAndRead(grinning, grinning);
            Assert.Equal("0:2", snapshot.Caret);
        }

        [InputDriverFact]
        public void Latin_Arabic_and_a_supplementary_character_typed_together_match_the_model()
        {
            using var session = Session.Start();
            const string mixed = "ab سلام \U0001F600 cd";
            session.TypeAndRead(mixed, mixed);
        }

        [InputDriverFact]
        public void A_keyman_legacy_sequence_of_backspace_then_unicode_replaces_the_typed_letter()
        {
            using var session = Session.Start();
            session.TypeAndRead("o", "o");
            // Keyman's non-TSF output for o becoming o-grave: one Backspace press, then U+00F2 as a Unicode packet.
            Assert.Equal(4u, Win32Input.KeymanLegacyOutput(1, "ò"));
            var snapshot = session.ReadUntil("ò");
            Assert.Equal("0:1", snapshot.Caret);
        }

        [InputDriverFact]
        public void A_keyman_legacy_sequence_replaces_a_longer_run_and_a_surrogate_pair()
        {
            using var session = Session.Start();
            session.TypeAndRead("ka", "ka");
            Assert.Equal(8u, Win32Input.KeymanLegacyOutput(2, "\U0001F600"));
            session.ReadUntil("\U0001F600");
        }

        [InputDriverFact]
        public void The_test_channel_stays_closed_unless_the_host_is_asked_for_it()
        {
            using var host = PlaygroundHost.Launch(withChannel: false);
            Assert.NotEqual(IntPtr.Zero, host.WaitForWindow(PlaygroundHost.Wait));
            // The driver's pipes carry a fixed prefix, so a host that opened a channel unasked would show a new pipe with it.
            Assert.DoesNotContain(System.IO.Directory.GetFiles(@"\\.\pipe\"), name => name.Contains("textediting-test-"));
            Assert.False(host.HasExited);
            Assert.Null(host.PipeName);
        }

        [Fact(Skip = "needs an installed IME: IME composition cannot be started from SendInput; the headless suite drives the field's IME client directly")]
        public void An_ime_composition_shows_preedit_text_and_commits_it_to_the_model()
        {
        }
    }
}
