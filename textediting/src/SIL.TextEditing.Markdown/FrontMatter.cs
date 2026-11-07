using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SIL.TextEditing.Markdown
{
    /// <summary>The YAML front matter of a FieldWorks Markdown file (spec 2.1): the format version, the default analysis and vernacular writing
    /// systems, and the language tag, direction and font of each writing system the text uses. The exporter generates it and nobody maintains it by hand.</summary>
    public sealed class MarkdownFrontMatter
    {
        /// <summary>The format version this library reads and writes (<c>fwmd: 1</c>).</summary>
        public const int CurrentVersion = 1;

        /// <summary>The usual file name ending: <c>*.fw.md</c>. It is only a hint, so that editors can match it; the front matter decides what a file is.</summary>
        public const string FileExtension = ".fw.md";

        /// <summary>The <c>fwmd</c> key, or null when the block has none.</summary>
        public int? Version { get; set; }

        /// <summary>The writing system of unmarked text.</summary>
        public string? Analysis { get; set; }

        /// <summary>The writing system <c>.v</c> stands for.</summary>
        public string? Vernacular { get; set; }

        /// <summary>The writing systems listed, in order, with the tag, direction and font each was given. The project's own definitions win on import.</summary>
        public List<KeyValuePair<string, WritingSystemInfo>> WritingSystems { get; } = new List<KeyValuePair<string, WritingSystemInfo>>();

        /// <summary>The source range [start, end) of the whole block including its <c>---</c> lines, or null when the text has none.</summary>
        public int[]? Range { get; internal set; }

        /// <summary>The [start, end) of the <c>analysis</c> and <c>vernacular</c> lines in the block, for a finding about their writing systems.</summary>
        internal int[]? AnalysisAt, VernacularAt;
    }

    internal static class FrontMatterFormat
    {
        /// <summary>Where the front matter of <paramref name="text"/> is: the block starts at offset 0 with a <c>---</c> line and ends with a <c>---</c> or
        /// <c>...</c> line. Returns false when the text has none. <paramref name="closed"/> is false when the opening line has no closing line.</summary>
        public static bool TryFind(string text, int start, out int end, out bool closed)
        {
            end = 0;
            closed = false;
            if (!IsFenceAt(text, start, out int next)) return false;
            int pos = next;
            while (pos < text.Length)
            {
                int lineEnd = pos;
                while (lineEnd < text.Length && text[lineEnd] != '\n' && text[lineEnd] != '\r') lineEnd++;
                bool fence = IsFenceAt(text, pos, out int after, allowDots: true);
                if (fence) { end = after; closed = true; return true; }
                pos = lineEnd < text.Length ? lineEnd + (text[lineEnd] == '\r' && lineEnd + 1 < text.Length && text[lineEnd + 1] == '\n' ? 2 : 1) : lineEnd;
            }
            end = text.Length;
            return true;
        }

        static bool IsFenceAt(string text, int pos, out int next, bool allowDots = false)
        {
            next = pos;
            char c = allowDots && pos < text.Length && text[pos] == '.' ? '.' : '-';
            if (pos + 3 > text.Length || text[pos] != c || text[pos + 1] != c || text[pos + 2] != c) return false;
            int i = pos + 3;
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
            if (i < text.Length && text[i] != '\n' && text[i] != '\r') return false;
            if (i < text.Length) i += text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
            next = i;
            return true;
        }

        /// <summary>Reads the block's lines. A line the small reader does not understand is reported through <paramref name="problem"/> (offset in the block, message).</summary>
        public static MarkdownFrontMatter Parse(string block, Action<int, int, string> problem)
        {
            var result = new MarkdownFrontMatter();
            int pos = 0;
            // Skip the opening line.
            pos = SkipLine(block, pos);
            string? currentWs = null;
            var props = new Dictionary<string, string>();
            string? section = null;
            while (pos < block.Length)
            {
                int lineEnd = pos;
                while (lineEnd < block.Length && block[lineEnd] != '\n' && block[lineEnd] != '\r') lineEnd++;
                string line = block.Substring(pos, lineEnd - pos);
                int lineStart = pos;
                pos = lineEnd < block.Length ? lineEnd + (block[lineEnd] == '\r' && lineEnd + 1 < block.Length && block[lineEnd + 1] == '\n' ? 2 : 1) : lineEnd;
                if (lineStart == 0) continue;
                string trimmed = line.TrimEnd();
                if (trimmed.Length == 0 || trimmed.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;
                if (line.StartsWith("---", StringComparison.Ordinal) || line.StartsWith("...", StringComparison.Ordinal)) break;
                if (trimmed.Length > 0 && trimmed[0] == '	')
                {
                    problem(lineStart, lineEnd, "YAML is indented with spaces, not tabs: Pandoc refuses this front matter.");
                    continue;
                }
                int indent = 0;
                while (indent < trimmed.Length && trimmed[indent] == ' ') indent++;
                string body = trimmed.Substring(indent);
                if (!TrySplit(body, out string key, out string rawValue))
                {
                    problem(lineStart, lineEnd, "This front matter line is not a key and a value.");
                    continue;
                }
                string? value = rawValue.Length == 0 ? null : Scalar(rawValue);
                if (indent == 0)
                {
                    section = null;
                    currentWs = null;
                    switch (key)
                    {
                        case "fwmd":
                            if (value != null && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int version)) result.Version = version;
                            else problem(lineStart, lineEnd, "fwmd takes a whole number, the format version.");
                            break;
                        case "analysis":
                            if (IsOneId(value)) { result.Analysis = value; result.AnalysisAt = new[] { lineStart, lineEnd }; }
                            else problem(lineStart, lineEnd, "analysis takes one writing-system id.");
                            break;
                        case "vernacular":
                            if (IsOneId(value)) { result.Vernacular = value; result.VernacularAt = new[] { lineStart, lineEnd }; }
                            else problem(lineStart, lineEnd, "vernacular takes one writing-system id.");
                            break;
                        case "writingSystems": section = "writingSystems"; break;
                    }
                }
                else if (section == "writingSystems")
                {
                    if (value == null) { FlushWs(result, currentWs, props); currentWs = key; props.Clear(); }
                    else if (currentWs != null) props[key] = value;
                }
            }
            FlushWs(result, currentWs, props);
            return result;
        }

        /// <summary>A writing-system id is one scalar: not empty, not a list or a map, no control character.</summary>
        static bool IsOneId(string? value)
        {
            if (string.IsNullOrEmpty(value) || value![0] == '[' || value[0] == '{' || value[0] == '-' || value[0] == '|' || value[0] == '>') return false;
            foreach (char c in value) if (c < ' ' || c == (char)0x7F) return false;
            return true;
        }

        static void FlushWs(MarkdownFrontMatter result, string? id, Dictionary<string, string> props)
        {
            if (id == null) return;
            var fallback = WritingSystemInfo.Default(id);
            props.TryGetValue("lang", out var lang);
            props.TryGetValue("dir", out var dir);
            props.TryGetValue("font", out var font);
            result.WritingSystems.Add(new KeyValuePair<string, WritingSystemInfo>(id, new WritingSystemInfo(lang ?? fallback.Language, dir ?? fallback.Direction, font ?? fallback.Font)));
        }

        static int SkipLine(string s, int pos)
        {
            while (pos < s.Length && s[pos] != '\n' && s[pos] != '\r') pos++;
            if (pos < s.Length) pos += s[pos] == '\r' && pos + 1 < s.Length && s[pos + 1] == '\n' ? 2 : 1;
            return pos;
        }

        static bool TrySplit(string body, out string key, out string value)
        {
            key = value = string.Empty;
            int i = 0;
            if (body.Length > 0 && (body[0] == '"' || body[0] == '\''))
            {
                string? k = ReadQuoted(body, ref i);
                if (k == null) return false;
                key = k;
            }
            else
            {
                while (i < body.Length && body[i] != ':') i++;
                if (i >= body.Length) return false;
                key = body.Substring(0, i).TrimEnd();
            }
            if (i >= body.Length || body[i] != ':') return false;
            i++;
            if (i < body.Length && body[i] != ' ') return false;
            value = body.Substring(i).Trim();
            return key.Length > 0;
        }

        static string Scalar(string raw)
        {
            if (raw.Length > 0 && (raw[0] == '"' || raw[0] == '\''))
            {
                int i = 0;
                return ReadQuoted(raw, ref i) ?? raw;
            }
            int hash = raw.IndexOf(" #", StringComparison.Ordinal);
            return (hash >= 0 ? raw.Substring(0, hash) : raw).Trim();
        }

        static string? ReadQuoted(string s, ref int i)
        {
            char quote = s[i++];
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i];
                if (c == quote)
                {
                    if (quote == '\'' && i + 1 < s.Length && s[i + 1] == '\'') { sb.Append('\''); i += 2; continue; }
                    i++;
                    return sb.ToString();
                }
                if (c == '\\' && quote == '"' && i + 1 < s.Length)
                {
                    char n = s[i + 1];
                    i += 2;
                    switch (n)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u' when i + 4 <= s.Length && int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int cu): sb.Append((char)cu); i += 4; break;
                        default: sb.Append(n); break;
                    }
                    continue;
                }
                sb.Append(c);
                i++;
            }
            return null;
        }

        // ---------------------------------------------------------------- writing

        public static string Write(string analysis, string vernacular, IEnumerable<string> usedIds, Func<string, WritingSystemInfo> describe)
        {
            var ids = new List<string>();
            void Add(string id) { if (!ids.Contains(id)) ids.Add(id); }
            Add(analysis);
            Add(vernacular);
            foreach (string id in usedIds) Add(id);
            var sb = new StringBuilder("---\n");
            sb.Append("fwmd: ").Append(MarkdownFrontMatter.CurrentVersion.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("analysis: ").Append(Quote(analysis)).Append('\n');
            sb.Append("vernacular: ").Append(Quote(vernacular)).Append('\n');
            sb.Append("writingSystems:\n");
            foreach (string id in ids)
            {
                var info = describe(id);
                sb.Append("  ").Append(IsPlainKey(id) ? id : Quote(id)).Append(":\n");
                sb.Append("    lang: ").Append(Quote(info.Language)).Append('\n');
                sb.Append("    dir: ").Append(Quote(info.Direction)).Append('\n');
                sb.Append("    font: ").Append(Quote(info.Font)).Append('\n');
            }
            return sb.Append("---\n").ToString();
        }

        static bool IsPlainKey(string id)
        {
            if (id.Length == 0) return false;
            foreach (char c in id) if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_')) return false;
            return true;
        }

        static string Quote(string value)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in value)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c < ' ' || c == '\x7F') sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
