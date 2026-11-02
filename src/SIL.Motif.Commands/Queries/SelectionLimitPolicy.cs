using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Requests;

namespace SIL.Motif.Commands.Queries;

/// <summary>The limits stored on a Selection and the resolved time cap for a future run.</summary>
public sealed record ResolvedSelectionLimits(
    ParserWorkLimit PerWordParserWorkLimit,
    int? PerWordTimeLimitMs,
    decimal? EstimatedMilliseconds,
    bool IsTypicalMachine);

/// <summary>Validates and resolves a Selection's saved parsing policy for editors and Assessments.</summary>
public static class SelectionLimitPolicy
{
    /// <summary>Resolves an estimate from the current parser rate without changing the saved policy.</summary>
    public static ResolvedSelectionLimits Resolve(SelectionParsingLimits limits, ParserAttemptRate rate)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(rate);
        if (limits.ValidationError() is { } error) throw new ArgumentException(error, nameof(limits));

        var estimate = ParserWorkLimitEstimator.Calculate(limits.PerWordParserWorkLimit, rate);
        var timeLimit = limits.TimeMode == SelectionTimeLimitMode.Explicit
            ? limits.ExplicitPerWordLimitMs
            : estimate?.PerWordTimeLimitMs is { } milliseconds && milliseconds <= int.MaxValue
                ? (int)milliseconds
                : null;
        return new ResolvedSelectionLimits(limits.PerWordParserWorkLimit, timeLimit,
            estimate?.EstimatedMilliseconds, rate.IsTypicalMachine);
    }

    /// <summary>Returns estimated policy, clearing any explicit cap for an unbounded Selection.</summary>
    public static SelectionParsingLimits Estimated(ParserWorkLimit parserWorkLimit) =>
        new(parserWorkLimit, SelectionTimeLimitMode.Estimated, null);

    /// <summary>Returns an explicit policy with a validated positive millisecond cap.</summary>
    public static SelectionParsingLimits Explicit(ParserWorkLimit parserWorkLimit, int milliseconds)
    {
        var limits = new SelectionParsingLimits(parserWorkLimit, SelectionTimeLimitMode.Explicit, milliseconds);
        if (limits.ValidationError() is { } error) throw new ArgumentOutOfRangeException(nameof(milliseconds), error);
        return limits;
    }
}
