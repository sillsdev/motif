using System.Globalization;

namespace SIL.Motif.Contract.Assess;

/// <summary>A finite per-word search bound or an explicit request for no step bound.</summary>
public sealed record StepCap
{
    public const long DefaultSteps = 50_000_000;

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
