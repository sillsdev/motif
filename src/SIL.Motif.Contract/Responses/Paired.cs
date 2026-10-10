using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Commands;

namespace SIL.Motif.Contract.Responses;

/// <summary>A value measured before and, when available, after a Proposal Trial.</summary>
/// <typeparam name="T">The value represented by the pair.</typeparam>
/// <param name="Before">The value from today's project evidence.</param>
/// <param name="After">The value from the Proposal Trial, when one exists.</param>
/// <param name="HasBefore">Whether the today's value was read successfully.</param>
/// <param name="HasAfter">Whether the Proposal has a Trial value to show.</param>
/// <param name="Moved">Whether the domain comparer considers the values meaningfully different.</param>
/// <param name="AfterRefusal">Why the Trial value could not be read, when a read was refused.</param>
[JsonConverter(typeof(PairedJsonConverterFactory))]
public readonly record struct Paired<T>(
    [property: JsonPropertyName("before")] T? Before,
    [property: JsonPropertyName("after")] T? After,
    [property: JsonPropertyName("hasBefore")] bool HasBefore,
    [property: JsonPropertyName("hasAfter")] bool HasAfter,
    [property: JsonPropertyName("moved")] bool Moved,
    [property: JsonPropertyName("afterRefusal")] Refusal? AfterRefusal);

/// <summary>Writes a missing after-value as JSON null for both reference and value types.</summary>
public sealed class PairedJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Paired<>);

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[0];
        return (JsonConverter)Activator.CreateInstance(typeof(PairedJsonConverter<>).MakeGenericType(valueType))!;
    }

    private sealed class PairedJsonConverter<T> : JsonConverter<Paired<T>>
    {
        public override Paired<T> Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            var beforeElement = root.GetProperty("before");
            var before = beforeElement.ValueKind == JsonValueKind.Null
                ? default
                : beforeElement.Deserialize<T>(options);
            var afterElement = root.GetProperty("after");
            var after = afterElement.ValueKind == JsonValueKind.Null
                ? default
                : afterElement.Deserialize<T>(options);
            return new Paired<T>(before, after, root.GetProperty("hasBefore").GetBoolean(),
                root.GetProperty("hasAfter").GetBoolean(), root.GetProperty("moved").GetBoolean(),
                root.GetProperty("afterRefusal").Deserialize<Refusal>(options));
        }

        public override void Write(Utf8JsonWriter writer, Paired<T> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("before");
            if (value.HasBefore) JsonSerializer.Serialize(writer, value.Before, options);
            else writer.WriteNullValue();
            writer.WritePropertyName("after");
            if (value.HasAfter) JsonSerializer.Serialize(writer, value.After, options);
            else writer.WriteNullValue();
            writer.WriteBoolean("hasBefore", value.HasBefore);
            writer.WriteBoolean("hasAfter", value.HasAfter);
            writer.WriteBoolean("moved", value.Moved);
            writer.WritePropertyName("afterRefusal");
            JsonSerializer.Serialize(writer, value.AfterRefusal, options);
            writer.WriteEndObject();
        }
    }
}
