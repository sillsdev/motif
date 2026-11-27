using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SIL.Motif.RecordStore.Lcm;

public static class CanonicalJson
{
    public static string Serialize(object? value) => Normalize(JsonSerializer.SerializeToElement(value));
    public static string Normalize(JsonElement value)
    {
        var builder = new StringBuilder();
        Write(value, builder);
        return builder.ToString();
    }

    private static void Write(JsonElement value, StringBuilder builder)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                builder.Append('{');
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (!seen.Add(property.Name)) throw new InvalidDataException("Duplicate JSON key.");
                    if (seen.Count > 1) builder.Append(',');
                    Quote(property.Name, builder);
                    builder.Append(':');
                    Write(property.Value, builder);
                }
                builder.Append('}');
                break;
            case JsonValueKind.Array:
                builder.Append('[');
                var first = true;
                foreach (var item in value.EnumerateArray())
                {
                    if (!first) builder.Append(',');
                    first = false;
                    Write(item, builder);
                }
                builder.Append(']');
                break;
            case JsonValueKind.String: Quote(value.GetString()!, builder); break;
            case JsonValueKind.Number:
                if (!value.TryGetInt64(out var number) || number is < -9007199254740991L or > 9007199254740991L)
                    throw new InvalidDataException("Non-interoperable integers require typed decimal strings.");
                builder.Append(number.ToString(CultureInfo.InvariantCulture));
                break;
            case JsonValueKind.True: builder.Append("true"); break;
            case JsonValueKind.False: builder.Append("false"); break;
            case JsonValueKind.Null: builder.Append("null"); break;
            default: throw new InvalidDataException("Unsupported JSON value.");
        }
    }

    private static void Quote(string value, StringBuilder builder)
    {
        builder.Append('"');
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsHighSurrogate(character))
            {
                if (++index >= value.Length || !char.IsLowSurrogate(value[index]))
                    throw new InvalidDataException("Unpaired UTF-16 surrogate.");
                builder.Append(character).Append(value[index]);
                continue;
            }
            if (char.IsLowSurrogate(character)) throw new InvalidDataException("Unpaired UTF-16 surrogate.");
            builder.Append(character switch
            {
                '"' => "\\\"", '\\' => "\\\\", '\b' => "\\b", '\f' => "\\f", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t",
                < ' ' => "\\u" + ((int)character).ToString("x4", CultureInfo.InvariantCulture), _ => character.ToString()
            });
        }
        builder.Append('"');
    }
}
