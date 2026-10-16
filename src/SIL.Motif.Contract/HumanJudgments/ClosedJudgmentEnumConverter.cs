using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.HumanJudgments;

/// <summary>Reads and writes a closed judgment enum as its kebab-case name; numbers and unknown names are refused.</summary>
public sealed class ClosedJudgmentEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    private static readonly IReadOnlyDictionary<string, T> Values = Enum.GetValues<T>().ToDictionary(
        value => JsonNamingPolicy.KebabCaseLower.ConvertName(value.ToString()), StringComparer.Ordinal);

    /// <summary>The accepted wire names, in declaration order.</summary>
    public static IReadOnlyList<string> Names { get; } =
        Enum.GetValues<T>().Select(value => JsonNamingPolicy.KebabCaseLower.ConvertName(value.ToString())).ToArray();

    /// <inheritdoc />
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || !Values.TryGetValue(reader.GetString()!, out var value))
            throw new JsonException("Unknown closed judgment enum value.");
        return value;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        if (!Enum.IsDefined(value)) throw new JsonException("Unknown closed judgment enum value.");
        writer.WriteStringValue(JsonNamingPolicy.KebabCaseLower.ConvertName(value.ToString()));
    }
}
