using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SIL.TextEditing;

namespace SIL.TextEditing.Playground
{
    /// <summary>Renders what the model holds as text, so a tester sees exactly the paragraph ids, runs, properties and
    /// selection. Plain text on purpose: a tester can select it and paste it into a bug report.</summary>
    public static class Inspector
    {
        /// <summary>Documents up to this many paragraphs are shown whole; longer ones show a window around the caret.</summary>
        public const int WholeDocumentLimit = 30;
        const int Window = 4;

        public static string Describe(DocumentEditor editor, SimpleUndoStack undo)
        {
            var sb = new StringBuilder();
            var doc = editor.Document;
            var caret = editor.Caret;
            var anchor = editor.Anchor;
            sb.Append("caret ").Append(caret).Append("   anchor ").Append(anchor).Append("   selection ");
            if (editor.HasSelection)
            {
                editor.Ordered(out var a, out var b);
                sb.Append(a).Append(" -> ").Append(b);
            }
            else sb.Append("none");
            sb.AppendLine();
            sb.Append("ws at caret ").Append(editor.WsAtCaret()).Append("   active ws ").Append(editor.ActiveWs).AppendLine();
            sb.Append("undo ").Append(undo.DoneSteps).Append(" done, ").Append(undo.UndoneSteps).Append(" undone (steps; ")
                .Append(undo.Done.Count).Append(" edits done)").AppendLine();
            sb.Append("paragraphs ").Append(doc.Paragraphs.Count).AppendLine();

            int first = 0, last = doc.Paragraphs.Count - 1;
            if (doc.Paragraphs.Count > WholeDocumentLimit)
            {
                first = System.Math.Max(0, caret.Paragraph - Window);
                last = System.Math.Min(doc.Paragraphs.Count - 1, caret.Paragraph + Window);
                sb.Append("(showing paragraphs ").Append(first).Append(" to ").Append(last).Append(" around the caret)").AppendLine();
            }
            for (int i = first; i <= last; i++) AppendParagraph(sb, i, doc.Paragraphs[i]);
            return sb.ToString();
        }

        static void AppendParagraph(StringBuilder sb, int index, Paragraph p)
        {
            sb.Append('[').Append(index).Append("] key=").Append(p.Key == null ? "(none)" : System.Convert.ToString(p.Key, CultureInfo.InvariantCulture))
              .Append(" rtl=").Append(p.Rtl ? "yes" : "no")
              .Append(" style=").Append(Quote(p.Style))
              .Append(" defaultWs=").Append(p.DefaultWs)
              .Append(" length=").Append(p.Length).AppendLine();
            int pos = 0;
            for (int k = 0; k < p.Runs.Count; k++)
            {
                var r = p.Runs[k];
                sb.Append("    r").Append(k).Append(" [").Append(pos).Append(',').Append(pos + r.Text.Length).Append(") ws=").Append(r.Ws)
                  .Append(' ').Append(Quote(r.Text));
                if (r.ObjData != null) sb.Append(" obj=").Append(Quote(r.ObjData));
                sb.Append(" props{").Append(Props(r.Props)).Append('}').AppendLine();
                pos += r.Text.Length;
            }
        }

        /// <summary>Int properties as number=value (and @variation when it is not 0), string properties as number="value".</summary>
        public static string Props(PropertyBag bag)
        {
            var parts = new List<string>();
            foreach (var kv in bag.IntProps)
                parts.Add(kv.Key.ToString(CultureInfo.InvariantCulture) + "=" + kv.Value.Value.ToString(CultureInfo.InvariantCulture) + (kv.Value.Variation != 0 ? "@" + kv.Value.Variation.ToString(CultureInfo.InvariantCulture) : string.Empty));
            foreach (var kv in bag.StrProps)
                parts.Add(kv.Key.ToString(CultureInfo.InvariantCulture) + "=" + Quote(kv.Value));
            return string.Join(", ", parts);
        }

        /// <summary>Text in quotes with everything that is invisible or ambiguous written as an escape: controls,
        /// combining marks, format characters (ZWJ, bidi marks), private use, the object replacement character, and
        /// every surrogate pair (as \U and the code point).</summary>
        public static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\n') sb.Append("\\n");
                else if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    sb.Append("\\U").Append(char.ConvertToUtf32(c, s[i + 1]).ToString("X8", CultureInfo.InvariantCulture));
                    i++;
                }
                else if (NeedsEscape(c)) sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        static bool NeedsEscape(char c)
        {
            if (c < 0x20 || c == Run.ObjectReplacement) return true;
            switch (CharUnicodeInfo.GetUnicodeCategory(c))
            {
                case UnicodeCategory.Control:
                case UnicodeCategory.Format:
                case UnicodeCategory.NonSpacingMark:
                case UnicodeCategory.EnclosingMark:
                case UnicodeCategory.PrivateUse:
                case UnicodeCategory.Surrogate:
                case UnicodeCategory.OtherNotAssigned:
                case UnicodeCategory.LineSeparator:
                case UnicodeCategory.ParagraphSeparator:
                    return true;
                default:
                    return false;
            }
        }
    }
}
