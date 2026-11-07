using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Threading;
using SIL.TextEditing.Playground;

namespace SIL.TextEditing.Samples
{
    /// <summary>An opt-in named pipe a test driver reads the playground's field through. It exists only when the host is started with
    /// <c>--test-channel &lt;pipe name&gt;</c>; without that argument nothing listens. A request is the line <c>snapshot</c> and the reply is the
    /// lines <see cref="Format"/> writes, ending with <c>end</c>.</summary>
    public static class TestChannel
    {
        public const string Argument = "--test-channel";

        /// <summary>The pipe name after <see cref="Argument"/>, or null when the host was not asked to open the channel.</summary>
        public static string? NameFrom(string[] args)
        {
            int i = Array.IndexOf(args, Argument);
            return i >= 0 && i + 1 < args.Length && args[i + 1].Length > 0 ? args[i + 1] : null;
        }

        /// <summary>Serves snapshots of the view's active field on a background thread until the process ends.</summary>
        public static void Start(string pipeName, PlaygroundView view, Func<IEnumerable<string>>? hostLines = null)
        {
            var thread = new Thread(() => Serve(pipeName, view, hostLines)) { IsBackground = true, Name = "test channel" };
            thread.Start();
        }

        static void Serve(string pipeName, PlaygroundView view, Func<IEnumerable<string>>? hostLines)
        {
            while (true)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte);
                    pipe.WaitForConnection();
                    var utf8 = new UTF8Encoding(false);
                    using var reader = new StreamReader(pipe, utf8, false, 1024, true);
                    using var writer = new StreamWriter(pipe, utf8, 1024, true) { AutoFlush = true, NewLine = "\n" };
                    string? request;
                    while ((request = reader.ReadLine()) != null)
                    {
                        // The model is read on the UI thread so a snapshot never races an edit.
                        string reply = request == "snapshot" ? Dispatcher.UIThread.Invoke(() => Format(view, hostLines)) : "error unknown request\nend\n";
                        writer.Write(reply);
                    }
                }
                catch (IOException)
                {
                    // A driver that disconnects abruptly just ends that connection; the next one is served.
                }
            }
        }

        /// <summary>Lines: <c>caret P:O</c>, <c>anchor P:O</c>, <c>field X Y W H</c> (screen pixels, or none), one <c>p</c> line per paragraph with
        /// tab-separated run ws and text pairs (text escaped by <see cref="Escape"/>), <c>popup open|closed</c> (the completion popup), the host's own lines (<c>focus</c>, <c>box</c>, <c>rect</c>), then <c>end</c>.</summary>
        public static string Format(PlaygroundView view, Func<IEnumerable<string>>? hostLines = null)
        {
            var field = view.Field;
            var editor = field.Editor;
            var sb = new StringBuilder();
            sb.Append("caret ").Append(editor.Caret).Append('\n');
            sb.Append("anchor ").Append(editor.Anchor).Append('\n');
            var size = field.Bounds.Size;
            double scale = global::Avalonia.Controls.TopLevel.GetTopLevel(field)?.RenderScaling ?? 1;
            var origin = field.PointToScreen(new Point(0, 0));
            sb.Append("field ").Append(origin.X).Append(' ').Append(origin.Y).Append(' ')
              .Append((int)(size.Width * scale)).Append(' ')
              .Append((int)(size.Height * scale)).Append('\n');
            foreach (var paragraph in editor.Document.Paragraphs)
            {
                sb.Append('p');
                foreach (var run in paragraph.Runs) sb.Append('\t').Append(run.Ws).Append('\t').Append(Escape(run.Text));
                sb.Append('\n');
            }
            sb.Append("popup ").Append(field.IsCompletionOpen ? "open" : "closed").Append('\n');
            if (hostLines != null) foreach (var line in hostLines()) sb.Append(line).Append('\n');
            return sb.Append("end\n").ToString();
        }

        public static string Escape(string text) =>
            text.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r");

        public static string Unescape(string text)
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
    }
}
