namespace SIL.Motif.Contract.Responses;

/// <summary>
/// The one set of rules that decide when a value in a <see cref="Paired{T}"/> has moved enough to highlight. The
/// command that scores Proposals and the window's pages both pass these, so the same pair is highlighted the same
/// way wherever it appears, and changing a threshold is one edit.
/// </summary>
/// <remarks>
/// A value present on one side only counts as moved; two missing values do not.
/// </remarks>
public static class PairedMoves
{
    /// <summary>The smallest change in a percentage, in percentage points, that counts as a move.</summary>
    public const double PercentPoints = 0.05;

    /// <summary>The share of the larger time that a change in time must exceed to count as a move.</summary>
    public const double TimeShare = 0.05;

    /// <summary>A count moves when it differs at all.</summary>
    public static bool Count(int before, int after) => before != after;

    /// <summary>A count moves when it differs at all, or is present on one side only.</summary>
    public static bool Count(int? before, int? after) => before != after;

    /// <summary>A percentage moves by at least <see cref="PercentPoints"/> percentage points.</summary>
    public static bool Percent(double? before, double? after) => (before, after) switch
    {
        ({ } b, { } a) => Math.Round(Math.Abs(b - a), 9) >= PercentPoints,
        (null, null) => false,
        _ => true,
    };

    /// <summary>A time moves by more than <see cref="TimeShare"/> of the larger of the two times.</summary>
    public static bool Time(double? before, double? after) => (before, after) switch
    {
        ({ } b, { } a) => Math.Abs(b - a) > TimeShare * Math.Max(Math.Abs(b), Math.Abs(a)),
        (null, null) => false,
        _ => true,
    };

    /// <summary>Text, such as an already formatted value, moves when it reads differently.</summary>
    public static bool Text(string? before, string? after) => !string.Equals(before, after, StringComparison.Ordinal);
}
