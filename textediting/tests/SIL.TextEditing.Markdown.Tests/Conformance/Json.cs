using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SIL.TextEditing.Markdown.Tests.Conformance
{
    /// <summary>A small JSON reader (RFC 8259) for the conformance fixtures and Pandoc's output, so the tests run on net48 and net10.0 with no package of their
    /// own. Objects are <see cref="Dictionary{TKey, TValue}"/> in file order lost (a fixture never depends on it), arrays are <see cref="List{T}"/>, numbers are
    /// <see cref="long"/> or <see cref="double"/>, and a string keeps every <c>\uXXXX</c> escape exactly, lone surrogates and NUL included.</summary>
    internal static class Json
    {
        public static object? Parse(string text)
        {
            int i = 0;
            SkipSpace(text, ref i);
            var value = ReadValue(text, ref i);
            SkipSpace(text, ref i);
            if (i != text.Length) throw new FormatException("Unexpected text after the JSON value at " + i);
            return value;
        }

        static void SkipSpace(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r' || s[i] == '﻿')) i++;
        }

        static object? ReadValue(string s, ref int i)
        {
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON");
            char c = s[i];
            switch (c)
            {
                case '{': return ReadObject(s, ref i);
                case '[': return ReadArray(s, ref i);
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

        static Dictionary<string, object?> ReadObject(string s, ref int i)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            i++;
            SkipSpace(s, ref i);
            if (s[i] == '}') { i++; return result; }
            while (true)
            {
                SkipSpace(s, ref i);
                string key = ReadString(s, ref i);
                SkipSpace(s, ref i);
                if (s[i] != ':') throw new FormatException("Expected : at " + i);
                i++;
                SkipSpace(s, ref i);
                result[key] = ReadValue(s, ref i);
                SkipSpace(s, ref i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return result; }
                throw new FormatException("Expected , or } at " + i);
            }
        }

        static List<object?> ReadArray(string s, ref int i)
        {
            var result = new List<object?>();
            i++;
            SkipSpace(s, ref i);
            if (s[i] == ']') { i++; return result; }
            while (true)
            {
                SkipSpace(s, ref i);
                result.Add(ReadValue(s, ref i));
                SkipSpace(s, ref i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return result; }
                throw new FormatException("Expected , or ] at " + i);
            }
        }

        static string ReadString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("Expected a string at " + i);
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new FormatException("Unterminated string");
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c < ' ') throw new FormatException("A raw control character (U+" + ((int)c).ToString("X4") + ") in a JSON string at " + (i - 1) + "; JSON writes it as an escape");
                if (c != '\\') { sb.Append(c); continue; }
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
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
            string text = s.Substring(start, i - start);
            if (text.Length == 0) throw new FormatException("Unexpected character " + s[start] + " at " + start);
            if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long l)) return l;
            return double.Parse(text, CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------- typed access

        public static Dictionary<string, object?> Obj(object? value) => (Dictionary<string, object?>)value!;
        public static List<object?> Arr(object? value) => (List<object?>)value!;
        public static string Str(object? value) => (string)value!;

        public static string? OptStr(Dictionary<string, object?> o, string key) => o.TryGetValue(key, out var v) ? (string?)v : null;
        public static bool OptBool(Dictionary<string, object?> o, string key, bool fallback) => o.TryGetValue(key, out var v) && v is bool b ? b : fallback;
        public static int OptInt(Dictionary<string, object?> o, string key, int fallback) => o.TryGetValue(key, out var v) && v is long l ? (int)l : fallback;
    }
}
