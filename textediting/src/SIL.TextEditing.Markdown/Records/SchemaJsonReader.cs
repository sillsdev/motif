using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SIL.TextEditing.Markdown.Records
{
    internal static class SchemaJsonReader
    {
        public static object? Parse(string text)
        {
            int i = 0;
            SkipSpace(text, ref i);
            var value = ReadValue(text, ref i, 0);
            SkipSpace(text, ref i);
            if (i != text.Length) throw new FormatException("Unexpected text after the JSON value at " + i);
            return value;
        }

        static void SkipSpace(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }

        static object? ReadValue(string s, ref int i, int depth)
        {
            if (depth > 64) throw new FormatException("JSON nesting exceeds 64.");
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON");
            char c = s[i];
            switch (c)
            {
                case '{': return ReadObject(s, ref i, depth + 1);
                case '[': return ReadArray(s, ref i, depth + 1);
                case '"': return ReadString(s, ref i);
                case 't': Expect(s, ref i, "true"); return true;
                case 'f': Expect(s, ref i, "false"); return false;
                case 'n': Expect(s, ref i, "null"); return null;
                default: return ReadNumber(s, ref i);
            }
        }

        static void Expect(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw new FormatException("Expected " + word + " at " + i);
            i += word.Length;
        }

        static Dictionary<string, object?> ReadObject(string s, ref int i, int depth)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            i++;
            SkipSpace(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return result; }
            while (true)
            {
                SkipSpace(s, ref i);
                string key = ReadString(s, ref i);
                SkipSpace(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("Expected : at " + i);
                i++;
                SkipSpace(s, ref i);
                if (result.ContainsKey(key)) throw new FormatException("Duplicate JSON key: " + key);
                result.Add(key, ReadValue(s, ref i, depth));
                SkipSpace(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; return result; }
                throw new FormatException("Expected , or } at " + i);
            }
        }

        static List<object?> ReadArray(string s, ref int i, int depth)
        {
            var result = new List<object?>();
            i++;
            SkipSpace(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return result; }
            while (true)
            {
                SkipSpace(s, ref i);
                result.Add(ReadValue(s, ref i, depth));
                SkipSpace(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; return result; }
                throw new FormatException("Expected , or ] at " + i);
            }
        }

        static string ReadString(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"') throw new FormatException("Expected a string at " + i);
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new FormatException("Unterminated string");
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c < ' ') throw new FormatException("A raw control character (U+" + ((int)c).ToString("X4") + ") in a JSON string at " + (i - 1) + "; JSON writes it as an escape");
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) throw new FormatException("Unterminated escape.");
                char n = s[i++];
                switch (n)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("Incomplete Unicode escape.");
                        for (int j = i; j < i + 4; j++)
                            if (!((s[j] >= '0' && s[j] <= '9') || (s[j] >= 'a' && s[j] <= 'f') ||
                                (s[j] >= 'A' && s[j] <= 'F'))) throw new FormatException("Invalid Unicode escape.");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw new FormatException("Bad escape \\" + n);
                }
            }
        }

        static object ReadNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && s[i] == '-') i++;
            if (i >= s.Length || s[i] < '0' || s[i] > '9') throw new FormatException("Expected an integer.");
            if (s[i] == '0') i++;
            else while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
            if (!long.TryParse(s.Substring(start, i - start), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out long value)) throw new FormatException("Integer is out of range.");
            return value;
        }

        public static Dictionary<string, object?> Obj(object? value)
            => value as Dictionary<string, object?> ?? throw new FormatException("Expected a JSON object.");
        public static List<object?> Arr(object? value)
            => value as List<object?> ?? throw new FormatException("Expected a JSON array.");
        public static string Str(object? value)
            => value as string ?? throw new FormatException("Expected a JSON string.");
        public static string? OptStr(Dictionary<string, object?> obj, string key)
            => obj.TryGetValue(key, out var value) && value != null ? Str(value) : null;
        public static bool OptBool(Dictionary<string, object?> obj, string key, bool fallback)
            => !obj.TryGetValue(key, out var value) ? fallback : value is bool b ? b
                : throw new FormatException("Expected a Boolean for " + key);
        public static int OptInt(Dictionary<string, object?> obj, string key, int fallback)
            => !obj.TryGetValue(key, out var value) ? fallback : value is long n ? checked((int)n)
                : throw new FormatException("Expected an integer for " + key);
    }
}
