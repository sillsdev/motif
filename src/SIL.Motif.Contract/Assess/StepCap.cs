using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Assess;

/// <summary>A finite per-word search bound or an explicit request for no step bound.</summary>
[JsonConverter(typeof(StepCapJsonConverter))]
public sealed record StepCap
{
    public const long DefaultSteps = 1_000_000;

    public StepCap(long? steps)
    {
        if (steps is <= 0)
            throw new ArgumentOutOfRangeException(nameof(steps), "A finite step cap must be positive.");
        Steps = steps;
    }

    public long? Steps { get; }

    public bool IsUnbounded => Steps is null;

    public static StepCap Default { get; } = new((long?)DefaultSteps);

    public static StepCap Unbounded { get; } = new((long?)null);

    public static implicit operator StepCap(int steps) => new((long)steps);

    public static implicit operator StepCap(long steps) => new(steps);

    public string ToArgument() => Steps?.ToString(CultureInfo.InvariantCulture) ?? "unbounded";

    public static StepCap Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (StringComparer.OrdinalIgnoreCase.Equals(value, "unbounded")) return Unbounded;
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var steps) || steps <= 0)
            throw new FormatException("A step cap must be a positive integer or 'unbounded'.");
        return new StepCap(steps);
    }
}

/// <summary>Reads and writes a step cap as a structured JSON value.</summary>
public sealed class StepCapJsonConverter : JsonConverter<StepCap>
{
    public override StepCap Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException();
        long? steps = null;
        bool? isUnbounded = null;
        var hasSteps = false;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException();
            var property = reader.GetString();
            if (!reader.Read()) throw new JsonException();
            switch (property)
            {
                case "steps" when !hasSteps:
                    if (reader.TokenType == JsonTokenType.Null) steps = null;
                    else if (reader.TokenType == JsonTokenType.Number)
                    {
                        try { steps = reader.GetInt64(); }
                        catch (FormatException exception) { throw new JsonException("Step cap is out of range.", exception); }
                    }
                    else throw new JsonException();
                    hasSteps = true;
                    break;
                case "isUnbounded" when isUnbounded is null:
                    if (reader.TokenType != JsonTokenType.True && reader.TokenType != JsonTokenType.False)
                        throw new JsonException();
                    isUnbounded = reader.GetBoolean();
                    break;
                default:
                    throw new JsonException();
            }
        }

        if (reader.TokenType != JsonTokenType.EndObject || !hasSteps || isUnbounded is null || isUnbounded != (steps is null))
            throw new JsonException();
        try { return new StepCap(steps); }
        catch (ArgumentOutOfRangeException exception) { throw new JsonException("Step cap must be positive.", exception); }
    }

    public override void Write(Utf8JsonWriter writer, StepCap value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        if (value.Steps is { } steps) writer.WriteNumber("steps", steps);
        else writer.WriteNull("steps");
        writer.WriteBoolean("isUnbounded", value.IsUnbounded);
        writer.WriteEndObject();
    }
}
