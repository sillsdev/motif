using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Assess;

/// <summary>A finite per-word parser work limit or an explicit request for no parser work limit.</summary>
[JsonConverter(typeof(ParserWorkLimitJsonConverter))]
public sealed record ParserWorkLimit
{
    /// <summary>The default limit: 20,000,000 units of parser work for one word.</summary>
    public const long DefaultWork = 20_000_000;

    public ParserWorkLimit(long? work)
    {
        if (work is <= 0)
            throw new ArgumentOutOfRangeException(nameof(work), "A finite parser work limit must be positive.");
        Work = work;
    }

    public long? Work { get; }

    public bool IsUnbounded => Work is null;

    public static ParserWorkLimit Default { get; } = new((long?)DefaultWork);

    public static ParserWorkLimit Unbounded { get; } = new((long?)null);

    public static implicit operator ParserWorkLimit(int work) => new((long)work);

    public static implicit operator ParserWorkLimit(long work) => new(work);

    /// <summary>
    /// How many units of parser work PanGloss allows per analysis attempt, as its own default does. A limit stored
    /// before the limit counted parser work counted attempts, and reads as this many times its value.
    /// </summary>
    public const long WorkPerAttempt = 100;

    /// <summary>
    /// The limit that an older attempt count stands for, or null if the count is not positive. A count too large to
    /// hold clamps to the largest limit, as the store upgrade does.
    /// </summary>
    public static ParserWorkLimit? FromAttempts(long attempts) =>
        attempts <= 0 ? null
        : new ParserWorkLimit(attempts > long.MaxValue / WorkPerAttempt ? long.MaxValue : attempts * WorkPerAttempt);

    /// <summary>The <c>--work-cap</c> value, or <c>unbounded</c> without a limit.</summary>
    public string ToWorkCapArgument() => Work?.ToString(CultureInfo.InvariantCulture) ?? "unbounded";

    /// <summary>The limit in the form a person types and a project file keeps.</summary>
    public string ToArgument() => Work?.ToString(CultureInfo.InvariantCulture) ?? "unbounded";

    public static ParserWorkLimit Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (StringComparer.OrdinalIgnoreCase.Equals(value, "unbounded")) return Unbounded;
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var work) || work <= 0)
            throw new FormatException("A parser work limit must be a positive integer or 'unbounded'.");
        return new ParserWorkLimit(work);
    }
}

/// <summary>Reads and writes a parser work limit as a structured JSON value; reads the older attempt form too.</summary>
public sealed class ParserWorkLimitJsonConverter : JsonConverter<ParserWorkLimit>
{
    public override ParserWorkLimit Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException();
        long? value = null;
        bool? isUnbounded = null;
        var hasValue = false;
        var legacyAttempts = false;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException();
            var property = reader.GetString();
            if (!reader.Read()) throw new JsonException();
            switch (property)
            {
                // "steps" is the form written before the limit counted parser work: its number counts attempts.
                case "work" or "steps" when !hasValue:
                    legacyAttempts = property == "steps";
                    if (reader.TokenType == JsonTokenType.Null) value = null;
                    else if (reader.TokenType == JsonTokenType.Number)
                    {
                        try { value = reader.GetInt64(); }
                        catch (FormatException exception) { throw new JsonException("Parser work limit is out of range.", exception); }
                    }
                    else throw new JsonException();
                    hasValue = true;
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

        if (reader.TokenType != JsonTokenType.EndObject || !hasValue || isUnbounded is null || isUnbounded != (value is null))
            throw new JsonException();
        if (legacyAttempts && value is { } attempts)
            return ParserWorkLimit.FromAttempts(attempts) ?? throw new JsonException("Parser work limit is out of range.");
        try { return new ParserWorkLimit(value); }
        catch (ArgumentOutOfRangeException exception) { throw new JsonException("Parser work limit must be positive.", exception); }
    }

    public override void Write(Utf8JsonWriter writer, ParserWorkLimit value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        if (value.Work is { } work) writer.WriteNumber("work", work);
        else writer.WriteNull("work");
        writer.WriteBoolean("isUnbounded", value.IsUnbounded);
        writer.WriteEndObject();
    }
}
