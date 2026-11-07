using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SIL.TextEditing.Markdown.Records
{
    /// <summary>The spellings of the strict YAML profile (ADR 0032): double-quoted strings with <c>\"</c> and <c>\\</c> as the only escapes, <c>|-</c>
    /// literal blocks, and which keys are written bare.</summary>
    internal static class RecordProfile
    {
        /// <summary>True for a character no canonical record file holds raw: C0 and C1 controls (tab and line feed included, outside a block's line
        /// ends), DEL, the line and paragraph separators (one YAML reader breaks lines at them, another does not), the byte order mark, U+FFFE and U+FFFF.</summary>
        public static bool IsUnspellable(char c)
            => c < ' ' || (c >= '\x7F' && c <= '\x9F') || c == '\u2028' || c == '\u2029' || c == '\uFEFF' || c == '\uFFFE' || c == '\uFFFF';

        /// <summary>True when the text can be written as a double-quoted string with only <c>\"</c> and <c>\\</c> escaped.</summary>
        public static bool CanQuote(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (IsUnspellable(c)) return false;
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) { i++; continue; }
                if (char.IsSurrogate(c)) return false;
            }
            return true;
        }

        public static string Quote(string text)
        {
            var sb = new StringBuilder(text.Length + 2).Append('"');
            foreach (char c in text)
            {
                if (c == '"' || c == '\\') sb.Append('\\');
                sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        /// <summary>Why the text cannot be a <c>|-</c> block's content, or null when it can: one line per paragraph line, no line end at either end, no
        /// first line that starts with a space (it would change the block's indentation), and no character <see cref="IsUnspellable"/> but the line feed.</summary>
        public static string? LiteralProblem(string text)
        {
            if (text.Length == 0) return "it is empty";
            if (text[0] == '\n' || text[text.Length - 1] == '\n') return "it starts or ends with a line end";
            if (text[0] == ' ') return "its first line starts with a space";
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\n') continue;
                if (IsUnspellable(c)) return "it holds U+" + ((int)c).ToString("X4", CultureInfo.InvariantCulture);
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) { i++; continue; }
                if (char.IsSurrogate(c)) return "it holds a lone surrogate";
            }
            return null;
        }

        /// <summary>FieldWorks Markdown as a <c>|-</c> block can hold it: each character <see cref="IsUnspellable"/> but the line feed written as a character
        /// reference, which the FieldWorks Markdown reader reads back as that character in text and in a quoted attribute value alike. The canonical writer
        /// writes U+2028, U+2029 and U+FEFF raw only inside quoted attribute values; everything else it already spells so. A lone surrogate cannot be
        /// spelled and is left for the record writer to refuse.</summary>
        public static string ForBlock(string markdown)
        {
            StringBuilder? sb = null;
            for (int i = 0; i < markdown.Length; i++)
            {
                char c = markdown[i];
                if (c != '\n' && IsUnspellable(c))
                {
                    if (sb == null) sb = new StringBuilder(markdown, 0, i, markdown.Length + 16);
                    sb.Append("&#x").Append(((int)c).ToString("X", CultureInfo.InvariantCulture)).Append(';');
                }
                else sb?.Append(c);
            }
            return sb?.ToString() ?? markdown;
        }

        /// <summary>Appends the block header and the text's lines at <paramref name="indent"/> spaces (an empty line stays empty) to a line that already has
        /// its key. The header is <c>|</c> (clip) for a value of paragraphs, which Pandoc then reads as blocks, and <c>|-</c> for one paragraph or plain
        /// text (owner decision R-8); the text never ends in a line end, so both read back as it is.</summary>
        public static void AppendLiteral(StringBuilder sb, string text, int indent, bool paragraphs = false)
        {
            sb.Append(paragraphs ? "|\n" : "|-\n");
            foreach (string line in text.Split('\n'))
            {
                if (line.Length > 0) sb.Append(' ', indent).Append(line);
                sb.Append('\n');
            }
        }

        static readonly HashSet<string> YamlWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "y", "Y", "yes", "Yes", "YES", "n", "N", "no", "No", "NO", "true", "True", "TRUE", "false", "False", "FALSE", "on", "On", "ON", "off", "Off", "OFF",
            "null", "Null", "NULL",
        };

        /// <summary>A key as written: bare when it is letters, digits, <c>-</c>, <c>_</c> and <c>/</c>, starts with a letter, and is not a word YAML 1.1
        /// reads as a Boolean or null (<c>no</c>, Norwegian's id, is quoted); otherwise double-quoted.</summary>
        public static string Key(string key)
        {
            bool bare = key.Length > 0 && IsLetter(key[0]) && !YamlWords.Contains(key);
            if (bare)
            {
                foreach (char c in key)
                {
                    if (!(IsLetter(c) || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '/')) { bare = false; break; }
                }
            }
            return bare ? key : Quote(key);
        }

        static bool IsLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        /// <summary>The offsets of the starts of the lines of <paramref name="text"/> (LF, CR LF and CR end a line).</summary>
        public static List<int> LineStarts(string text)
        {
            var starts = new List<int> { 0 };
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r') { if (i + 1 < text.Length && text[i + 1] == '\n') i++; starts.Add(i + 1); }
                else if (c == '\n') starts.Add(i + 1);
            }
            return starts;
        }
    }
}
