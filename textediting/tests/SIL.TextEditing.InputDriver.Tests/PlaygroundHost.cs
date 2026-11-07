using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;

namespace SIL.TextEditing.InputDriver.Tests
{
    /// <summary>What the host's test pipe returned: the field's paragraphs as (writing system, text) runs, the caret and the anchor.</summary>
    sealed class Snapshot
    {
        public string Caret = "", Anchor = "";
        public int[] FieldRect = new int[4];
        public string Focus = "", Popup = "", AvaloniaFocus = "";
        public readonly Dictionary<string, string> Boxes = new Dictionary<string, string>();
        public readonly Dictionary<string, int[]> Rects = new Dictionary<string, int[]>();
        public readonly List<List<(string Ws, string Text)>> Paragraphs = new List<List<(string Ws, string Text)>>();

        public string Text(int paragraph) => string.Concat(Paragraphs[paragraph].Select(r => r.Text));
        public string AllText => string.Join("\n", Paragraphs.Select(p => string.Concat(p.Select(r => r.Text))));

        public static Snapshot Parse(IEnumerable<string> lines)
        {
            var s = new Snapshot();
            foreach (var line in lines)
            {
                if (line.StartsWith("caret ")) s.Caret = line.Substring(6);
                else if (line.StartsWith("anchor ")) s.Anchor = line.Substring(7);
                else if (line.StartsWith("field ")) s.FieldRect = line.Substring(6).Split(' ').Select(int.Parse).ToArray();
                else if (line.StartsWith("popup ")) s.Popup = line.Substring(6);
                else if (line.StartsWith("focus ")) s.Focus = line.Substring(6);
                else if (line.StartsWith("avalonia ")) s.AvaloniaFocus = line.Substring(9);
                else if (line.StartsWith("box "))
                {
                    var parts = line.Split(new[] { ' ' }, 3);
                    s.Boxes[parts[1]] = Unescape(parts.Length > 2 ? parts[2] : "");
                }
                else if (line.StartsWith("rect "))
                {
                    var parts = line.Split(' ');
                    s.Rects[parts[1]] = parts.Skip(2).Select(int.Parse).ToArray();
                }
                else if (line == "p" || line.StartsWith("p\t"))
                {
                    var parts = line.Split('\t');
                    var runs = new List<(string Ws, string Text)>();
                    for (int i = 1; i + 1 < parts.Length; i += 2) runs.Add((parts[i], Unescape(parts[i + 1])));
                    s.Paragraphs.Add(runs);
                }
            }
            return s;
        }

        static string Unescape(string text)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '\\' || i + 1 == text.Length) { sb.Append(text[i]); continue; }
                char c = text[++i];
                sb.Append(c == 't' ? '\t' : c == 'n' ? '\n' : c == 'r' ? '\r' : c);
            }
            return sb.ToString();
        }

        public override string ToString() => "caret " + Caret + ", anchor " + Anchor + ", focus " + Focus + ", avalonia focus " + AvaloniaFocus + ", popup " + Popup + ", text [" + AllText.Replace("\n", "|") + "]";
    }

    /// <summary>The three hosts the driver runs: the plain Avalonia desktop host on each Avalonia line, and FieldWorks' WinForms shape (net48).</summary>
    public enum HostKind { Desktop10, Desktop48, WinForms48 }

    /// <summary>The playground's plain desktop host running as a child process, with its test pipe when one was asked for.</summary>
    sealed class PlaygroundHost : IDisposable
    {
        public static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);
        readonly Process _process;
        NamedPipeClientStream? _pipe;
        StreamReader? _reader;
        StreamWriter? _writer;

        public string? PipeName { get; }
        public int Pid => _process.Id;
        public bool HasExited => _process.HasExited;

        PlaygroundHost(Process process, string? pipeName)
        {
            _process = process;
            PipeName = pipeName;
        }

        /// <summary>The built host of that kind, found beside this test project's output (same configuration).</summary>
        public static string HostPath(HostKind kind = HostKind.Desktop10)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            string configuration = dir.Parent!.Name;
            string bin = dir.Parent.Parent!.Parent!.FullName;
            switch (kind)
            {
                case HostKind.Desktop48: return Path.Combine(bin, "Playground.Desktop", configuration, "net48", "Playground.Desktop.exe");
                case HostKind.WinForms48: return Path.Combine(bin, "Host48", configuration, "net48", "Host48.exe");
                default: return Path.Combine(bin, "Playground.Desktop", configuration, "net10.0", "Playground.Desktop.exe");
            }
        }

        public static PlaygroundHost Launch(bool withChannel, string scenario = "Empty", HostKind kind = HostKind.Desktop10)
        {
            string? pipe = withChannel ? "textediting-test-" + Guid.NewGuid().ToString("N") : null;
            var info = new ProcessStartInfo(HostPath(kind)) { UseShellExecute = false };
            info.ArgumentList.Add("--scenario");
            info.ArgumentList.Add(scenario);
            if (pipe != null)
            {
                info.ArgumentList.Add("--test-channel");
                info.ArgumentList.Add(pipe);
            }
            return new PlaygroundHost(Process.Start(info)!, pipe);
        }

        /// <summary>The host's top-level window, once it has one; zero if the process ends first or <paramref name="limit"/> passes.</summary>
        public IntPtr WaitForWindow(TimeSpan limit)
        {
            var end = DateTime.UtcNow + limit;
            while (DateTime.UtcNow < end && !_process.HasExited)
            {
                _process.Refresh();
                if (_process.MainWindowHandle != IntPtr.Zero) return _process.MainWindowHandle;
                Thread.Sleep(100);
            }
            return IntPtr.Zero;
        }

        /// <summary>Connects to the test pipe; false when nothing listens before <paramref name="limit"/>.</summary>
        public bool Connect(TimeSpan limit)
        {
            if (PipeName == null) throw new InvalidOperationException("the host was started without a test channel");
            var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
            try { pipe.Connect((int)limit.TotalMilliseconds); }
            catch (TimeoutException) { pipe.Dispose(); return false; }
            _pipe = pipe;
            var utf8 = new UTF8Encoding(false);
            _reader = new StreamReader(pipe, utf8);
            _writer = new StreamWriter(pipe, utf8) { AutoFlush = true, NewLine = "\n" };
            return true;
        }

        public Snapshot Snapshot()
        {
            _writer!.Write("snapshot\n");
            var lines = new List<string>();
            string? line;
            while ((line = _reader!.ReadLine()) != null && line != "end") lines.Add(line);
            return Tests.Snapshot.Parse(lines);
        }

        /// <summary>Polls snapshots until <paramref name="done"/> holds or <paramref name="limit"/> passes; returns the last one either way.</summary>
        public Snapshot WaitFor(Func<Snapshot, bool> done, TimeSpan limit)
        {
            var end = DateTime.UtcNow + limit;
            var snapshot = Snapshot();
            while (!done(snapshot) && DateTime.UtcNow < end)
            {
                Thread.Sleep(50);
                snapshot = Snapshot();
            }
            return snapshot;
        }

        public void Dispose()
        {
            try { _pipe?.Dispose(); } catch (IOException) { }
            try { if (!_process.HasExited) _process.Kill(true); } catch (InvalidOperationException) { }
            _process.Dispose();
        }
    }
}
