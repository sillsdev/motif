using System;
using System.Text;

namespace SIL.TextEditing.Markdown.Notation
{
    /// <summary>The characters at the start of a paragraph line that Markdown or Pandoc would read as block markup (spec 3.4): a heading, a list, a quote, a
    /// definition, a div fence, a table caption, a link reference definition. The writer escapes them, and the reader's fix for an unescaped one is the same.</summary>
    internal static class LineStart
    {
        /// <summary>The index in <paramref name="line"/> before which a backslash makes the line read as text, or -1 when it needs none.</summary>
        public static int EscapeAt(string line)
        {
            if (line.Length == 0) return -1;
            char c = line[0];
            char next = line.Length > 1 ? line[1] : '\0';
            switch (c)
            {
                case '#': case '>': case '+': case '|':
                    return 0;
                case '-':
                    return line.Length == 1 || next == ' ' || next == '\t' || next == '-' ? 0 : -1;
                case '=':
                    foreach (char e in line) if (e != '=' && e != ' ' && e != '\t') return -1;
                    return 0;
                case ':': case '~':
                    return next == ' ' || next == '\t' || next == ':' || (c == '~' && next == '~') ? 0 : -1;
                case '[':
                {
                    int close = line.IndexOf(']');
                    return close > 0 && close + 1 < line.Length && line[close + 1] == ':' ? 0 : -1;
                }
                case '(':
                {
                    int close = line.IndexOf(')');
                    if (close < 2) return -1;
                    string inner = line.Substring(1, close - 1);
                    bool marker = inner == "#" || inner[0] == '@' || IsDigits(inner) || IsRomanOrLetter(inner);
                    return marker && EndsToken(line, close + 1) ? 0 : -1;
                }
            }
            if ((c == 'T' || c == 't') && line.Length >= 6 && string.Compare(line, 0, "table:", 0, 6, StringComparison.OrdinalIgnoreCase) == 0) return 5;
            int i = 0;
            if (c >= '0' && c <= '9') { while (i < line.Length && line[i] >= '0' && line[i] <= '9') i++; }
            else if (IsRomanChar(c) || IsLetter(c)) { while (i < line.Length && IsRomanChar(line[i])) i++; if (i == 0) i = 1; }
            else return -1;
            if (i < line.Length && (line[i] == '.' || line[i] == ')') && EndsToken(line, i + 1)) return i;
            return -1;
        }

        static bool EndsToken(string line, int at) => at >= line.Length || line[at] == ' ' || line[at] == '\t';
        static bool IsDigits(string s) { foreach (char c in s) if (c < '0' || c > '9') return false; return s.Length > 0; }
        static bool IsLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        static bool IsRomanChar(char c) => "ivxlcdmIVXLCDM".IndexOf(c) >= 0;
        static bool IsRomanOrLetter(string s)
        {
            if (s.Length == 1 && IsLetter(s[0])) return true;
            foreach (char c in s) if (!IsRomanChar(c)) return false;
            return s.Length > 0;
        }

        /// <summary>The text with a backslash before the block marker at the start of each of its lines, or null when no line needs one.</summary>
        public static string? Escaped(string text)
        {
            var sb = new StringBuilder();
            bool any = false;
            int pos = 0;
            while (pos <= text.Length)
            {
                int lineEnd = text.IndexOf('\n', pos);
                int stop = lineEnd < 0 ? text.Length : lineEnd;
                string line = text.Substring(pos, stop - pos);
                int at = EscapeAt(line);
                if (at >= 0) { any = true; line = line.Substring(0, at) + "\\" + line.Substring(at); }
                sb.Append(line);
                if (lineEnd < 0) break;
                sb.Append('\n');
                pos = lineEnd + 1;
            }
            return any ? sb.ToString() : null;
        }
    }
}
