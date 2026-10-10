using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.HumanJudgments;

internal sealed class ClosedJudgmentEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    private static readonly IReadOnlyDictionary<string, T> Values = Enum.GetValues<T>().ToDictionary(
        value => JsonNamingPolicy.KebabCaseLower.ConvertName(value.ToString()), StringComparer.Ordinal);

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || !Values.TryGetValue(reader.GetString()!, out var value))
            throw new JsonException("Unknown closed judgment enum value.");
        return value;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        if (!Enum.IsDefined(value)) throw new JsonException("Unknown closed judgment enum value.");
        writer.WriteStringValue(JsonNamingPolicy.KebabCaseLower.ConvertName(value.ToString()));
    }
}
