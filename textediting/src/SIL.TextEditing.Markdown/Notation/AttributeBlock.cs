using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SIL.TextEditing.Markdown.Notation
{
    /// <summary>One class (<c>.v</c>), <c>key=value</c> pair or stray bare word of an attribute block (spec 3.3), with its place in the source.</summary>
    internal sealed class AttrItem
    {
        public AttrItem(string key, string? value, bool quoted, int start, int end, bool isClass = false)
        {
            Key = key;
            Value = value;
            Quoted = quoted;
            Start = start;
            End = end;
            IsClass = isClass;
        }

        /// <summary>The key, or the class name without its dot.</summary>
        public string Key { get; }
        /// <summary>The unescaped value; null for a class or a bare word.</summary>
        public string? Value { get; }
        public bool Quoted { get; }
        /// <summary>True for a class: <c>.v</c>.</summary>
        public bool IsClass { get; }
        /// <summary>True for a bare word that is neither a class nor a pair (<c>{v}</c>): nothing in the grammar spells that.</summary>
        public bool IsFlag => Value == null && !IsClass;
        /// <summary>Offsets in the scanned string, [Start, End).</summary>
        public int Start { get; }
        public int End { get; }

        /// <summary>The item as the writer would spell it, for messages.</summary>
        public string Spelling => IsClass ? "." + Key : Key;
    }

    /// <summary>A scanned <c>{...}</c> block, [Start, End) in the scanned string.</summary>
    internal sealed class AttrBlock
    {
        public AttrBlock(int start, int end, List<AttrItem> items)
        {
            Start = start;
            End = end;
            Items = items;
        }

        public int Start { get; }
        public int End { get; }
        public List<AttrItem> Items { get; }

        /// <summary>The same block with every offset moved by <paramref name="delta"/>: Markdig numbers a table cell's text from the start of the
        /// table, so the parsers shift what they scan to offsets in the source.</summary>
        public AttrBlock Shifted(int delta)
        {
            if (delta == 0) return this;
            var items = new List<AttrItem>(Items.Count);
            foreach (var item in Items) items.Add(new AttrItem(item.Key, item.Value, item.Quoted, item.Start + delta, item.End + delta, item.IsClass));
            return new AttrBlock(Start + delta, End + delta, items);
        }
    }

    internal enum ScanStatus { Ok, Unclosed, Malformed }

    /// <summary>The scanner for the attribute grammar of the format (spec 3.3). It reads the whole block in one step, so a <c>}</c>, a space,
    /// a <c>|</c> or an <c>=</c> inside a quoted value never ends it.
    /// <code>
    /// block  = "{" item { " " item } "}"      ; "{}" is an error
    /// item   = class | pair
    /// class  = "." ALPHA { ALPHA | DIGIT | "-" | "_" }
    /// pair   = key "=" value
    /// key    = ALPHA { ALPHA | DIGIT | "-" }
    /// value  = bare | quoted
    /// bare   = 1*( ALPHA | DIGIT | "-" | "_" | "." | ":" | "#" | "/" )
    /// quoted = DQUOTE { char | "\" DQUOTE | "\\" } DQUOTE
    /// </code>
    /// The reader is lenient the way Pandoc is: it also takes single quotes, several spaces, and numeric and named character references in
    /// a quoted value (Pandoc decodes them). The writer never writes any of these.</summary>
    internal static class AttributeScanner
    {
        /// <param name="unescapePipe">Inside a pipe-table cell, <c>\|</c> is a <c>|</c> (Pandoc takes it before the cell is read).</param>
        public static ScanStatus TryScan(string text, int start, int limit, out AttrBlock? block, out int errorPos, out string error, bool unescapePipe = true)
        {
            string s = text;
            block = null;
            errorPos = start;
            error = string.Empty;
            if (start >= limit || s[start] != '{') { error = "expected {"; return ScanStatus.Malformed; }
            var items = new List<AttrItem>();
            int i = start + 1;
            if (i < limit && s[i] == '}') { errorPos = i; error = "{} is empty"; return ScanStatus.Malformed; }
            while (true)
            {
                // A soft line break between attributes is a space, as in Pandoc.
                while (i < limit && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
                if (i >= limit)
                {
                    // The report covers the block as far as its first line goes, where the writer of the text most likely forgot the brace.
                    int newline = s.IndexOfAny(new[] { '\n', '\r' }, start, limit - start);
                    errorPos = start + 1;
                    foreach (var item in items) if (newline < 0 || item.End <= newline) errorPos = item.End;
                    error = "no closing }";
                    return ScanStatus.Unclosed;
                }
                if (s[i] == '}')
                {
                    if (items.Count == 0) { errorPos = i; error = "{} is empty"; return ScanStatus.Malformed; }
                    block = new AttrBlock(start, i + 1, items);
                    return ScanStatus.Ok;
                }
                int itemStart = i;
                if (s[i] == '.')
                {
                    i++;
                    int nameStart = i;
                    if (i >= limit || !IsAlpha(s[i])) { errorPos = i; error = "a class starts with a letter"; return ScanStatus.Malformed; }
                    while (i < limit && (IsAlpha(s[i]) || IsDigit(s[i]) || s[i] == '-' || s[i] == '_')) i++;
                    items.Add(new AttrItem(s.Substring(nameStart, i - nameStart), null, false, itemStart, i, isClass: true));
                }
                else
                {
                    if (!IsAlpha(s[i])) { errorPos = i; error = s[i] == '#' ? "an identifier (#name) is not part of the notation" : "a key starts with a letter, a class with a dot"; return ScanStatus.Malformed; }
                    while (i < limit && (IsAlpha(s[i]) || IsDigit(s[i]) || s[i] == '-')) i++;
                    string key = s.Substring(itemStart, i - itemStart);
                    if (i < limit && s[i] == '=')
                    {
                        i++;
                        if (i >= limit || s[i] == '\n' || s[i] == '\r') { errorPos = i; error = "no closing }"; return ScanStatus.Unclosed; }
                        if (s[i] == '"' || s[i] == '\'')
                        {
                            var st = ReadQuoted(s, limit, ref i, unescapePipe, out var value, out errorPos, out error);
                            if (st != ScanStatus.Ok) return st;
                            items.Add(new AttrItem(key, value, true, itemStart, i));
                        }
                        else
                        {
                            int vs = i;
                            while (i < limit && IsBare(s[i])) i++;
                            if (i == vs) { errorPos = i; error = "a value is bare letters, digits and - _ . : # / or quoted"; return ScanStatus.Malformed; }
                            items.Add(new AttrItem(key, s.Substring(vs, i - vs), false, itemStart, i));
                        }
                    }
                    else items.Add(new AttrItem(key, null, false, itemStart, i));
                }
                if (i < limit && s[i] != ' ' && s[i] != '\t' && s[i] != '\n' && s[i] != '\r' && s[i] != '}')
                {
                    errorPos = i;
                    error = "unexpected '" + s[i] + "'";
                    return ScanStatus.Malformed;
                }
            }
        }

        static ScanStatus ReadQuoted(string s, int limit, ref int i, bool unescapePipe, out string value, out int errorPos, out string error)
        {
            value = string.Empty;
            errorPos = i;
            error = string.Empty;
            char quote = s[i];
            var sb = new StringBuilder();
            i++; // opening quote
            while (true)
            {
                if (i >= limit || s[i] == '\n' || s[i] == '\r') { errorPos = i; error = "no closing quote"; return ScanStatus.Unclosed; }
                char c = s[i];
                if (c == quote) { i++; value = DecodeReferences(sb.ToString()); return ScanStatus.Ok; }
                if (c == '\\' && i + 1 < limit)
                {
                    char n = s[i + 1];
                    if (n == '\\' || n == quote) { sb.Append(n); i += 2; continue; }
                    if (n == '|' && unescapePipe) { sb.Append('|'); i += 2; continue; }
                }
                sb.Append(c);
                i++;
            }
        }

        /// <summary>Pandoc decodes numeric and named character references in a quoted value. Only references that name a character are
        /// decoded, so <c>a &amp; b</c> stays as it is; NUL and a lone surrogate become U+FFFD, as in text.</summary>
        internal static string DecodeReferences(string value)
        {
            if (value.IndexOf('&') < 0) return value;
            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '&')
                {
                    int semi = value.IndexOf(';', i + 1);
                    if (semi > i + 1 && semi - i <= 33)
                    {
                        string? decoded = DecodeOne(value.Substring(i + 1, semi - i - 1));
                        if (decoded != null) { sb.Append(decoded); i = semi; continue; }
                    }
                }
                sb.Append(value[i]);
            }
            return sb.ToString();
        }

        static string? DecodeOne(string name)
        {
            if (name.Length > 1 && name[0] == '#')
            {
                bool hex = name[1] == 'x' || name[1] == 'X';
                string digits = name.Substring(hex ? 2 : 1);
                if (digits.Length == 0) return null;
                long cp = 0;
                foreach (char d in digits)
                {
                    int v = hex ? HexValue(d) : (d >= '0' && d <= '9' ? d - '0' : -1);
                    if (v < 0) return null;
                    cp = cp * (hex ? 16 : 10) + v;
                    if (cp > 0x10FFFF) return null;
                }
                if (cp == 0 || (cp >= 0xD800 && cp <= 0xDFFF)) return ((char)0xFFFD).ToString();
                return char.ConvertFromUtf32((int)cp);
            }
            string decoded = System.Net.WebUtility.HtmlDecode("&" + name + ";");
            return decoded.Length > 0 && decoded != "&" + name + ";" ? decoded : null;
        }

        internal static int HexValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        static bool IsAlpha(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        static bool IsDigit(char c) => c >= '0' && c <= '9';
        static bool IsBare(char c) => IsAlpha(c) || IsDigit(c) || c == '-' || c == '_' || c == '.' || c == ':' || c == '#' || c == '/';

        /// <summary>True when the writer may write the value without quotes: letters, digits and <c>- _ . : # /</c>.</summary>
        public static bool IsBareValue(string value)
        {
            if (value.Length == 0) return false;
            foreach (char c in value) if (!IsBare(c)) return false;
            return true;
        }
    }

    /// <summary>Writes attribute values: bare when the grammar allows it, otherwise quoted with <c>\"</c> and <c>\\</c> as the only escapes.</summary>
    internal static class AttributeText
    {
        /// <param name="escapePipe">In a table row every <c>|</c> that is not a cell separator is written <c>\|</c>.</param>
        public static string Value(string value, bool escapePipe)
        {
            if (AttributeScanner.IsBareValue(value)) return value;
            return Quote(value, escapePipe);
        }

        public static string Quote(string value, bool escapePipe)
        {
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (escapePipe && c == '|') sb.Append("\\|");
                else if (c == '&' && ReadsAsReference(value, i)) sb.Append("&amp;");
                // Pandoc does not read a quoted value that starts with white space as quoted (it takes the quote as part of a bare value), so a leading space is
                // written as a character reference, which it decodes; so is any control character, which a value cannot hold as it is.
                else if ((i == 0 && char.IsWhiteSpace(c)) || c < ' ' || (c >= '\x7F' && c <= '\x9F')) sb.Append("&#x").Append(((int)c).ToString("X", CultureInfo.InvariantCulture)).Append(';');
                else sb.Append(c);
            }
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>True when the <c>&amp;</c> at <paramref name="at"/> would be read as a character reference: <c>&amp;#</c>, or letters and digits then <c>;</c>.</summary>
        public static bool ReadsAsReference(string text, int at)
        {
            int j = at + 1;
            if (j < text.Length && text[j] == '#') return true;
            while (j < text.Length && ((text[j] >= 'a' && text[j] <= 'z') || (text[j] >= 'A' && text[j] <= 'Z') || (text[j] >= '0' && text[j] <= '9'))) j++;
            return j > at + 1 && j < text.Length && text[j] == ';';
        }

        public static bool HasLoneSurrogate(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])) { i++; continue; }
                if (char.IsSurrogate(value[i])) return true;
            }
            return false;
        }

        /// <summary>True when the value cannot be carried in a quoted attribute value: it holds a control character or an unpaired surrogate.</summary>
        public static bool NeedsHex(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c < ' ' || (c >= '\x7F' && c <= '\x9F')) return true;
                if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])) { i++; continue; }
                if (char.IsSurrogate(c)) return true;
            }
            return false;
        }

        /// <summary>The value as hex: four uppercase digits for every UTF-16 code unit.</summary>
        public static string Hex(string value)
        {
            var sb = new StringBuilder(value.Length * 4);
            foreach (char c in value) sb.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        public static bool TryUnhex(string hex, out string value)
        {
            value = string.Empty;
            if (hex.Length % 4 != 0) return false;
            var sb = new StringBuilder(hex.Length / 4);
            for (int i = 0; i < hex.Length; i += 4)
            {
                int cu = 0;
                for (int k = 0; k < 4; k++)
                {
                    int d = AttributeScanner.HexValue(hex[i + k]);
                    if (d < 0) return false;
                    cu = cu * 16 + d;
                }
                sb.Append((char)cu);
            }
            value = sb.ToString();
            return true;
        }
    }
}
