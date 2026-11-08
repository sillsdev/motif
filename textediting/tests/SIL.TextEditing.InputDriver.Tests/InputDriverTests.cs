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

    public sealed class InputDriverTheoryAttribute : TheoryAttribute
    {
        public InputDriverTheoryAttribute() => Skip = Preflight.SkipReason;
    }

    /// <summary>A host started with its test channel, in the foreground, with the field clicked so it has focus. Fails with what it saw when any step does not hold.</summary>
    sealed class Session : IDisposable
    {
        public PlaygroundHost Host { get; }
        readonly IntPtr _window;

        Session(PlaygroundHost host, IntPtr window)
        {
            Host = host;
            _window = window;
        }

        /// <summary>Another program can take the foreground between steps on a shared desktop; input sent then would reach it, so each step checks first.</summary>
        void EnsureForeground() =>
            Assert.True(Win32Input.ProcessOf(Win32Input.ForegroundWindow) == Host.Pid || Win32Input.BringToForeground(_window, Host.Pid, TimeSpan.FromSeconds(10)),
                "the host lost the foreground; foreground is process " + Win32Input.ProcessOf(Win32Input.ForegroundWindow));

        public void Click(string control)
        {
            EnsureForeground();
            var r = Host.Snapshot().Rects[control];
            Assert.Equal(2u, Win32Input.Click(r[0] + r[2] / 2, r[1] + r[3] / 2));
        }

        /// <summary>With <paramref name="clickField"/> false the focus is left where the host put it, so a test can watch it move.</summary>
        public static Session Start(HostKind kind = HostKind.Desktop10, string scenario = "Empty", bool clickField = true)
        {
            var host = PlaygroundHost.Launch(withChannel: true, scenario, kind);
            try
            {
                var window = host.WaitForWindow(PlaygroundHost.Wait);
                Assert.True(window != IntPtr.Zero, "the host opened no window");
                Assert.True(host.Connect(PlaygroundHost.Wait), "the host's test channel did not open");
                Assert.True(Win32Input.BringToForeground(window, host.Pid, TimeSpan.FromSeconds(10)),
                    "the host is not the foreground window; foreground is process " + Win32Input.ProcessOf(Win32Input.ForegroundWindow));
                var session = new Session(host, window);
                if (clickField)
                {
                    var rect = host.Snapshot().FieldRect;
                    Assert.Equal(2u, Win32Input.Click(rect[0] + rect[2] / 2, rect[1] + rect[3] / 2));
                    session.WaitForFocus("field");
                }
                return session;
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
            Type(text);
            return ReadUntil(expected);
        }

        public Snapshot ReadUntil(string expected)
        {
            var snapshot = Host.WaitFor(s => s.AllText == expected, TimeSpan.FromSeconds(10));
            Assert.True(snapshot.AllText == expected, "expected [" + expected + "] but the model is " + snapshot + "; foreground is process "
                + Win32Input.ProcessOf(Win32Input.ForegroundWindow) + ", host is " + Host.Pid);
            return snapshot;
        }

        /// <summary>Waits until the host reports focus in <paramref name="control"/> (<c>field</c>, <c>before</c> or <c>after</c>) and returns that snapshot.</summary>
        public Snapshot WaitForFocus(string control)
        {
            var snapshot = Host.WaitFor(s => s.Focus == control, TimeSpan.FromSeconds(10));
            Assert.True(snapshot.Focus == control, "expected focus in " + control + " but the host says " + snapshot);
            return snapshot;
        }

        public void Press(ushort vk, bool shift = false, bool control = false)
        {
            EnsureForeground();
            Assert.Equal(Win32Input.PressedEvents(shift, control), Win32Input.Press(vk, shift, control));
        }

        /// <summary>Types units into whatever has focus and checks Windows took every event.</summary>
        public void Type(string text)
        {
            EnsureForeground();
            Assert.Equal((uint)(text.Length * 2), Win32Input.TypeUnicode(text));
        }

        public Snapshot ReadWhen(Func<Snapshot, bool> done, string what)
        {
            var snapshot = Host.WaitFor(done, TimeSpan.FromSeconds(10));
            Assert.True(done(snapshot), "expected " + what + " but the host says " + snapshot);
            return snapshot;
        }

        public void Dispose() => Host.Dispose();
    }

    public class InputDriverTests
    {
        [InputDriverTheory]
        [InlineData(HostKind.Desktop10)]
        [InlineData(HostKind.Desktop48)]
        [InlineData(HostKind.WinForms48)]
        public void A_short_unicode_string_reaches_the_field_of_the_foreground_host(HostKind kind)
        {
            using var session = Session.Start(kind);
            var snapshot = session.TypeAndRead("abc", "abc");
            Assert.Equal("0:3", snapshot.Caret);
            Assert.Equal(snapshot.Caret, snapshot.Anchor);
            Assert.All(snapshot.Paragraphs[0], run => Assert.False(string.IsNullOrEmpty(run.Ws)));
        }

        [InputDriverTheory]
        [InlineData(HostKind.Desktop10)]
        [InlineData(HostKind.Desktop48)]
        [InlineData(HostKind.WinForms48)]
        public void Arabic_typed_through_the_input_path_arrives_in_logical_order(HostKind kind)
        {
            using var session = Session.Start(kind);
            const string salaam = "سلام";
            var snapshot = session.TypeAndRead(salaam, salaam);
            Assert.Equal("0:4", snapshot.Caret);
        }

        [InputDriverTheory]
        [InlineData(HostKind.Desktop10)]
        [InlineData(HostKind.Desktop48)]
        [InlineData(HostKind.WinForms48)]
        public void A_supplementary_plane_character_sent_as_a_surrogate_pair_arrives_as_one_character(HostKind kind)
        {
            using var session = Session.Start(kind);
            const string grinning = "\U0001F600";
            var snapshot = session.TypeAndRead(grinning, grinning);
            Assert.Equal("0:2", snapshot.Caret);
        }

        [InputDriverTheory]
        [InlineData(HostKind.Desktop10)]
        [InlineData(HostKind.Desktop48)]
        [InlineData(HostKind.WinForms48)]
        public void Latin_Arabic_and_a_supplementary_character_typed_together_match_the_model(HostKind kind)
        {
            using var session = Session.Start(kind);
            const string mixed = "ab سلام \U0001F600 cd";
            session.TypeAndRead(mixed, mixed);
        }

        [InputDriverTheory]
        [InlineData(HostKind.Desktop10)]
        [InlineData(HostKind.Desktop48)]
        [InlineData(HostKind.WinForms48)]
        public void A_keyman_legacy_sequence_of_backspace_then_unicode_replaces_the_typed_letter(HostKind kind)
        {
            using var session = Session.Start(kind);
            session.TypeAndRead("o", "o");
            // Keyman's non-TSF output for o becoming o-grave: one Backspace press, then U+00F2 as a Unicode packet.
            Assert.Equal(4u, Win32Input.KeymanLegacyOutput(1, "ò"));
            var snapshot = session.ReadUntil("ò");
            Assert.Equal("0:1", snapshot.Caret);
        }

        [InputDriverTheory]
        [InlineData(HostKind.Desktop10)]
        [InlineData(HostKind.Desktop48)]
        [InlineData(HostKind.WinForms48)]
        public void A_keyman_legacy_sequence_replaces_a_longer_run_and_a_surrogate_pair(HostKind kind)
        {
            using var session = Session.Start(kind);
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

        [InputDriverFact]
        public void Tab_and_shift_tab_move_focus_between_the_winforms_text_boxes_and_the_field_and_typing_follows()
        {
            using var session = Session.Start(HostKind.WinForms48, clickField: false);
            // The field takes focus when the host opens, so, as the manual test does, click the first text box to start there.
            session.Click("before");
            session.WaitForFocus("before");
            session.Type("Q");
            session.ReadWhen(s => s.Boxes["before"].Contains("Q"), "Q in the first text box");

            session.Press(Win32Input.Tab);
            session.ReadWhen(x => x.Focus == "field" || x.Focus == "host", "focus in the field");
            session.Type("ab");
            var snapshot = session.ReadUntil("ab");
            Assert.Contains("Q", snapshot.Boxes["before"]);

            session.Press(Win32Input.Tab);
            session.WaitForFocus("after");
            session.Type("Z");
            snapshot = session.ReadWhen(s => s.Boxes["after"].Contains("Z"), "Z in the second text box");
            Assert.Equal("ab", snapshot.AllText);
            Assert.DoesNotContain("Z", snapshot.Boxes["before"]);

            session.Press(Win32Input.Tab, shift: true);
            session.ReadWhen(x => x.Focus == "field" || x.Focus == "host", "focus back in the field");
            session.Type("c");
            session.ReadUntil("abc");

            session.Press(Win32Input.Tab, shift: true);
            session.WaitForFocus("before");
            session.Type("R");
            snapshot = session.ReadWhen(s => s.Boxes["before"].Contains("R"), "R in the first text box");
            Assert.Equal("abc", snapshot.AllText);
        }

        [InputDriverFact]
        public void Editing_keys_in_the_field_of_the_winforms_host_stay_in_the_field()
        {
            using var session = Session.Start(HostKind.WinForms48);
            session.TypeAndRead("abc", "abc");
            session.Press(Win32Input.Back);
            session.ReadUntil("ab");
            session.Press(Win32Input.A, control: true);
            session.Press(Win32Input.Back);
            var snapshot = session.ReadUntil("");
            Assert.Equal("field", snapshot.Focus);
        }

        [InputDriverFact]
        public void The_completion_popup_in_the_winforms_host_takes_tab_and_escape_and_returns_focus_to_the_field()
        {
            using var session = Session.Start(HostKind.WinForms48, "Environment (language service)");
            // The scenario starts with a whole environment in it; clear it so the popup offers the slash.
            session.Press(Win32Input.A, control: true);
            session.Press(Win32Input.Back);
            session.ReadUntil("");

            session.Press(Win32Input.Space, control: true);
            session.ReadWhen(s => s.Popup == "open", "the popup open");
            session.Press(Win32Input.Escape);
            var snapshot = session.ReadWhen(s => s.Popup == "closed", "the popup closed by Esc");
            Assert.Equal("field", snapshot.Focus);
            Assert.Equal("", snapshot.AllText);
            Assert.False(session.Host.HasExited);

            session.Press(Win32Input.Space, control: true);
            session.ReadWhen(s => s.Popup == "open", "the popup open again");
            session.Press(Win32Input.Tab);
            snapshot = session.ReadWhen(s => s.Popup == "closed" && s.AllText == "/ ", "Tab accepting the slash");
            Assert.Equal("field", snapshot.Focus);

            session.Type("x");
            session.ReadUntil("/ x");
        }

        [InputDriverFact]
        public void Clicking_a_winforms_text_box_closes_the_completion_popup_and_typing_lands_there()
        {
            using var session = Session.Start(HostKind.WinForms48, "Environment (language service)");
            session.Press(Win32Input.A, control: true);
            session.Press(Win32Input.Back);
            session.ReadUntil("");
            session.Press(Win32Input.Space, control: true);
            var snapshot = session.ReadWhen(s => s.Popup == "open", "the popup open");

            session.Click("before");
            session.WaitForFocus("before");
            session.Type("Y");
            snapshot = session.ReadWhen(s => s.Popup == "closed" && s.Boxes["before"].Contains("Y"), "the popup closed and Y in the first text box");
            Assert.Equal("", snapshot.AllText);
        }

        [Fact(Skip = "needs an installed IME: IME composition cannot be started from SendInput; the headless suite drives the field's IME client directly")]
        public void An_ime_composition_shows_preedit_text_and_commits_it_to_the_model()
        {
        }
    }
}
