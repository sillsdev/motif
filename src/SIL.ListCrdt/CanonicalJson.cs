using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SIL.ListCrdt;

/// <summary>Text that is already canonical JSON (for example a serialised operation) to embed unchanged.</summary>
public readonly record struct RawJson(string Text);

/// <summary>
/// Minimal RFC 8785 (JCS) writer for the value domain the list operations use: null, booleans, strings,
/// integers within +/-(2^53-1), arrays and objects. Floating point numbers are deliberately unsupported;
/// counters and Lamport times are written as decimal strings. Object members sort by UTF-16 code units
/// (ordinal), no insignificant whitespace, minimal string escapes, non-ASCII written literally.
/// </summary>
public static class CanonicalJson
{
    const long MaxSafeInteger = 9007199254740991;

    public static string Write(object? value)
    {
        var sb = new StringBuilder();
        Append(sb, value);
        return sb.ToString();
    }

    public static byte[] WriteUtf8(object? value) => new UTF8Encoding(false).GetBytes(Write(value));

    static void Append(StringBuilder sb, object? value)
    {
        switch (value)
        {
            case null: sb.Append("null"); break;
            case bool b: sb.Append(b ? "true" : "false"); break;
            case RawJson raw: sb.Append(raw.Text); break;
            case string s: AppendString(sb, s); break;
            case int i: AppendInteger(sb, i); break;
            case long l: AppendInteger(sb, l); break;
            case IReadOnlyDictionary<string, object?> map:
                sb.Append('{');
                bool first = true;
                foreach (var key in map.Keys.Order(StringComparer.Ordinal))
                {
                    if (!first) sb.Append(',');
                    first = false;
                    AppendString(sb, key);
                    sb.Append(':');
                    Append(sb, map[key]);
                }
                sb.Append('}');
                break;
            case IEnumerable<object?> array:
                sb.Append('[');
                bool firstItem = true;
                foreach (var item in array)
                {
                    if (!firstItem) sb.Append(',');
                    firstItem = false;
                    Append(sb, item);
                }
                sb.Append(']');
                break;
            default: throw new ArgumentException("Unsupported canonical JSON value: " + value.GetType());
        }
    }

    static void AppendInteger(StringBuilder sb, long value)
    {
        if (value is > MaxSafeInteger or < -MaxSafeInteger) throw new ArgumentOutOfRangeException(nameof(value), "Integer outside the IEEE 754 safe range; write it as a string");
        sb.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    static void AppendString(StringBuilder sb, string s)
    {
        sb.Append('"');
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1])) throw new ArgumentException("Lone surrogate cannot be canonicalised");
                sb.Append(c).Append(s[++i]);
                continue;
            }
            if (char.IsLowSurrogate(c)) throw new ArgumentException("Lone surrogate cannot be canonicalised");
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\t': sb.Append("\\t"); break;
                case '\n': sb.Append("\\n"); break;
                case '\f': sb.Append("\\f"); break;
                case '\r': sb.Append("\\r"); break;
                case < ' ': sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); break;
                default: sb.Append(c); break;
            }
        }
        sb.Append('"');
    }

    /// <summary>Parses JSON text into a <see cref="JsonDocument"/>.</summary>
    public static JsonDocument Parse(string json) =>
        JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
}
