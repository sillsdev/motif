using System;
using System.Collections.Generic;
using SIL.TextEditing.Markdown.Notation;

namespace SIL.TextEditing.Markdown.Reader
{
    /// <summary>Constructs of Pandoc Markdown that the notation does not have and Markdig reads as plain text: math, citations, inline notes, strikeout, and every line that starts
    /// like a list, a definition, a line block, a grid table or a div fence. Pandoc reads each of them as something else, so a hand-written text that holds one would mean
    /// one thing to us and another to Pandoc (spec 3 and 7): it is a finding, and the escape the writer would have written is the fix. The writer escapes all of them, so
    /// canonical text never holds one.</summary>
    internal static class PandocConstructs
    {
        public delegate void Report(int start, int end, string message, string fix);

        /// <summary>Looks at the paragraph text in <paramref name="text"/> [<paramref name="start"/>, <paramref name="end"/>); offsets in the reports are those of <paramref name="text"/>.</summary>
        public static void Find(string text, int start, int end, bool lines, Report report, bool nearTable = false, bool fences = true)
        {
            if (lines) Lines(text, start, end, report, nearTable, fences);
            Inline(text, start, end, report);
        }

        /// <summary>The block constructs of Pandoc that Markdig reads as text, found by Pandoc's own rules (not the writer's broader escape rule). Pandoc starts a list,
        /// a line block, a grid table or a table caption only at the first line of a block; a definition only after a term line; a fence at any line start.
        /// Everything else on a later line is a continuation, which the reader takes the same way.</summary>
        static void Lines(string text, int start, int end, Report report, bool nearTable, bool fences)
        {
            int pos = start;
            bool first = true;
            while (pos < end)
            {
                int lineEnd = text.IndexOf('\n', pos);
                if (lineEnd < 0 || lineEnd > end) lineEnd = end;
                int from = pos, indent = 0;
                while (from < lineEnd && (text[from] == ' ' || text[from] == '\t')) { from++; indent++; }
                string line = text.Substring(from, lineEnd - from).TrimEnd('\r');
                int at = indent > 3 ? -1 : MarkerAt(text, from, lineEnd, line, first, nearTable, fences);
                if (at >= 0)
                    report(from, from + line.Length,
                        "Pandoc reads a line that starts like this as a list, a definition, a line block, a table or a div, which FieldWorks text has none of. Escape the first marker.",
                        line.Substring(0, at) + "\\" + line.Substring(at));
                first = false;
                pos = lineEnd + 1;
            }
        }

        /// <summary>The index in <paramref name="line"/> before which a backslash turns the construct into text, or -1 when Pandoc reads the line as text too.</summary>
        static int MarkerAt(string text, int from, int lineEnd, string line, bool first, bool nearTable, bool fences)
        {
            if (line.Length == 0) return -1;
            // A fence of three colons or more starts a div (or closes one) wherever it stands.
            if (line.StartsWith(":::", StringComparison.Ordinal)) return fences ? 0 : -1;
            char c = line[0];
            if (!first)
            {
                // After a term line: ':' or '~' and a space is a definition.
                return (c == ':' || c == '~') && line.Length > 1 && (line[1] == ' ' || line[1] == '\t') ? 0 : -1;
            }
            if (nearTable && (line.StartsWith("Table:", StringComparison.OrdinalIgnoreCase) || (c == ':' && line.Length > 1 && (line[1] == ' ' || line[1] == '\t')))) return c == ':' ? 0 : 5;
            if (c == '[') { int close = FirstUnescapedBracket(line); return close > 1 && close + 1 < line.Length && line[close + 1] == ':' ? 0 : -1; } // a reference or note definition, which Pandoc takes out of the text
            if (c == '|') return line.Length == 1 || line[1] == ' ' || line[1] == '\t' ? 0 : -1;
            if (c == '+') return line.Length == 1 || IsGridTop(text, from, lineEnd, line) ? 0 : -1;
            return ListMarkerAt(line);
        }

        static int FirstUnescapedBracket(string line)
        {
            for (int i = 1; i < line.Length; i++)
            {
                if (line[i] == '\\') { i++; continue; }
                if (line[i] == ']') return i;
                if (line[i] == '[') return -1; // a label holds no bracket of its own, so a span that starts with one is not a definition
            }
            return -1;
        }

        /// <summary>A grid table's top border (<c>+---+---+</c>) with a row of the table under it.</summary>
        static bool IsGridTop(string text, int from, int lineEnd, string line)
        {
            string trimmed = line.TrimEnd();
            if (trimmed.Length < 3 || trimmed[trimmed.Length - 1] != '+') return false;
            int cells = 0;
            for (int i = 1; i < trimmed.Length; i++)
            {
                char ch = trimmed[i];
                if (ch == '+') { cells++; continue; }
                if (ch != '-' && ch != '=' && ch != ':') return false;
                if (trimmed[i - 1] == '+' && ch == '+') return false;
            }
            if (cells == 0 || lineEnd + 1 >= text.Length) return false;
            int n = lineEnd + 1;
            while (n < text.Length && (text[n] == ' ' || text[n] == '\t')) n++;
            return n < text.Length && (text[n] == '|' || text[n] == '+');
        }

        /// <summary>Pandoc's fancy list markers: <c>(a)</c>, <c>a)</c>, <c>a.</c>, <c>i.</c>, <c>(@)</c>, <c>#.</c> and the like, followed by a space or the end of the line.
        /// A single capital letter with a period (an initial, or <c>A.</c> as a heading label) needs two spaces after it. A bare number with a period or a closing
        /// parenthesis is a list in Markdig too, so it is not looked at here.</summary>
        static int ListMarkerAt(string line)
        {
            int i = 0;
            bool open = line[0] == '(';
            if (open) i = 1;
            int bodyStart = i;
            if (i < line.Length && line[i] == '#' && !open) i++;
            else if (i < line.Length && line[i] == '#') i++;
            else if (i < line.Length && line[i] == '@')
            {
                if (!open) return -1;
                i++;
                while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_' || line[i] == '-')) i++;
            }
            else
            {
                while (i < line.Length && char.IsLetterOrDigit(line[i]) && line[i] < 128) i++;
            }
            if (i == bodyStart || i >= line.Length) return -1;
            string body = line.Substring(bodyStart, i - bodyStart);
            char close = line[i];
            if (open ? close != ')' : (close != ')' && close != '.')) return -1;
            int after = i + 1;
            bool endsToken = after >= line.Length || line[after] == ' ' || line[after] == '\t';
            if (!endsToken) return -1;
            bool digits = true;
            foreach (char ch in body) if (ch < '0' || ch > '9') digits = false;
            bool marker;
            if (body == "#" || body[0] == '@') marker = true;
            else if (digits) marker = open; // 1. and 1) start a list in Markdig as well
            else if (body.Length == 1 && char.IsLetter(body[0])) marker = true;
            else marker = IsRoman(body);
            if (!marker) return -1;
            // An initial or a label: one capital letter and a period needs two spaces (or a tab) before Pandoc takes it for a list.
            if (!open && close == '.' && body.Length == 1 && char.IsUpper(body[0]))
            {
                bool two = after >= line.Length || line[after] == '\t' || (after + 1 < line.Length && line[after] == ' ' && line[after + 1] == ' ');
                if (!two) return -1;
            }
            return open ? 0 : i;
        }

        static readonly System.Text.RegularExpressions.Regex UpperRoman = new System.Text.RegularExpressions.Regex("^M*(CM|CD|D?C{0,3})(XC|XL|L?X{0,3})(IX|IV|V?I{0,3})$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        static readonly System.Text.RegularExpressions.Regex LowerRoman = new System.Text.RegularExpressions.Regex("^m*(cm|cd|d?c{0,3})(xc|xl|l?x{0,3})(ix|iv|v?i{0,3})$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        static bool IsRoman(string s) => s.Length > 0 && (UpperRoman.IsMatch(s) || LowerRoman.IsMatch(s));

        static void Inline(string text, int start, int end, Report report)
        {
            var masked = Mask(text, start, end);
            int mathFailedFrom = int.MaxValue; // once no closing dollar sign follows one opener, none follows a later one, which keeps the scan linear
            for (int i = start; i < end; i++)
            {
                char c = text[i];
                if (c == '\\') { i++; continue; }
                if (masked[i - start]) continue;
                if (c == '$' && i + 1 < end && !IsSpace(text[i + 1]) && i < mathFailedFrom)
                {
                    int close = FindMathClose(text, i + 1, end);
                    if (close < 0) mathFailedFrom = i;
                    if (close > 0)
                    {
                        report(i, close + 1, "Pandoc reads this as math. Escape the dollar signs.", Escape(text, i, close + 1, '$'));
                        i = close;
                    }
                }
                else if (c == '@' && i + 1 < end && (char.IsLetterOrDigit(text[i + 1]) || text[i + 1] == '_' || text[i + 1] == '{') && (i == start || !char.IsLetterOrDigit(text[i - 1])))
                    report(i, i + 1, "Pandoc reads this as a citation. Escape the at sign.", "\\@");
                else if (c == '^' && i + 1 < end && text[i + 1] == '[')
                    report(i, i + 2, "Pandoc reads this as an inline note. Escape the caret.", "\\^[");
                else if (c == '~' && i + 1 < end && text[i + 1] == '~')
                {
                    int close = i + 2 < end && !IsSpace(text[i + 2]) ? text.IndexOf("~~", i + 2, end - i - 2, StringComparison.Ordinal) : -1;
                    if (close > 0 && !IsSpace(text[close - 1]))
                    {
                        report(i, close + 2, "Pandoc reads this as strikeout. Escape the tildes.", Escape(text, i, close + 2, '~'));
                        i = close + 1;
                    }
                    else
                    {
                        // Two tildes that close nothing are an empty subscript, which Pandoc drops.
                        report(i, i + 2, "Pandoc reads two tildes as an empty subscript and drops them. Escape the tildes.", "\\~\\~");
                        i++;
                    }
                }
            }
        }

        static bool IsSpace(char c) => c == ' ' || c == '\t' || c == '\n' || c == '\r';

        /// <summary>The offset of the <c>$</c> that closes math opened before <paramref name="from"/>: after a character that is not a space, and not followed by a digit.</summary>
        static int FindMathClose(string text, int from, int end)
        {
            for (int j = from; j < end; j++)
            {
                if (text[j] == '\\') { j++; continue; }
                if (text[j] == '$' && !IsSpace(text[j - 1]) && !(j + 1 < end && char.IsDigit(text[j + 1]))) return j;
            }
            return -1;
        }

        static string Escape(string text, int from, int to, char mark)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = from; i < to; i++)
            {
                if (text[i] == '\\' && i + 1 < to) { sb.Append(text[i]).Append(text[i + 1]); i++; continue; }
                if (text[i] == mark) sb.Append('\\');
                sb.Append(text[i]);
            }
            return sb.ToString();
        }

        /// <summary>The attribute blocks and destinations of the notation: Pandoc does not read a citation or a note inside them, and neither do we.</summary>
        static bool[] Mask(string text, int start, int end)
        {
            var mask = new bool[end - start];
            for (int i = start + 1; i < end; i++)
            {
                if (text[i] == '{' && (text[i - 1] == ']' || text[i - 1] == ')'))
                {
                    if (AttributeScanner.TryScan(text, i, end, out var block, out _, out _) == ScanStatus.Ok)
                    {
                        for (int k = i; k < block!.End && k < end; k++) mask[k - start] = true;
                        i = block.End - 1;
                    }
                }
                else if (text[i] == '(' && text[i - 1] == ']')
                {
                    int depth = 0, k = i;
                    for (; k < end; k++)
                    {
                        if (text[k] == '\\') { k++; continue; }
                        if (text[k] == '(') depth++;
                        else if (text[k] == ')' && --depth == 0) break;
                    }
                    for (int m = i; m <= k && m < end; m++) mask[m - start] = true;
                    i = k;
                }
            }
            return mask;
        }

        /// <summary>Pandoc's heading attributes: <c>## Title {#id .class key=value}</c>. Returns the offset of the <c>{</c> when the line ends in such a group.</summary>
        public static int HeadingAttributes(string line)
        {
            string trimmed = line.TrimEnd();
            if (trimmed.Length == 0 || trimmed[trimmed.Length - 1] != '}') return -1;
            int open = trimmed.LastIndexOf('{');
            if (open < 0) return -1;
            int slashes = 0;
            for (int b = open - 1; b >= 0 && trimmed[b] == '\\'; b--) slashes++;
            if (slashes % 2 == 1) return -1;
            if (open > 0 && (trimmed[open - 1] == ']' || trimmed[open - 1] == ')')) return -1;
            string inner = trimmed.Substring(open + 1, trimmed.Length - open - 2).Trim();
            if (inner.Length == 0 || inner.IndexOfAny(new[] { '{', '}', '"', '\'' }) >= 0) return -1;
            if (open == 0 || (trimmed[open - 1] != ' ' && trimmed[open - 1] != '\t')) return -1;
            foreach (string token in inner.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                bool idOrClass = (token[0] == '#' || token[0] == '.') && token.Length > 1;
                bool pair = token.IndexOf('=') > 0;
                if (!idOrClass && !pair) return -1;
            }
            return open;
        }
    }
}
