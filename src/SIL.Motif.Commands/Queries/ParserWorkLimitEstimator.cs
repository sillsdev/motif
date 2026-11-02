using SIL.Motif.Contract.Assess;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Queries;

/// <summary>The measured or fallback parser time for one analysis attempt.</summary>
public sealed record ParserAttemptRate(decimal MillisecondsPerAttempt, bool IsTypicalMachine);

/// <summary>The estimated word duration and saved time cap for one finite analysis-parser work limit.</summary>
public sealed record ParserWorkLimitEstimate(
    decimal EstimatedMilliseconds,
    long? PerWordTimeLimitMs,
    bool IsTypicalMachine);

/// <summary>Derives setup estimates from stored parser timing or a documented typical-machine rate.</summary>
public static class ParserWorkLimitEstimator
{
    /// <summary>The fallback rate estimates 250,000 analysis attempts per second.</summary>
    public const decimal TypicalMachineMillisecondsPerAttempt = 0.004m;

    /// <summary>The fallback rate used before an Assessment has usable parser statistics.</summary>
    public static ParserAttemptRate TypicalMachineRate { get; } =
        new(TypicalMachineMillisecondsPerAttempt, IsTypicalMachine: true);

    /// <summary>Uses a stored Assessment's per-word elapsed times and analysis-attempt counts when available.</summary>
    public static ParserAttemptRate FromAssessment(AssessmentRecord? assessment)
    {
        if (assessment?.Words is null)
            return TypicalMachineRate;

        decimal totalSteps = 0;
        decimal totalMilliseconds = 0;
        foreach (var word in assessment.Words)
        {
            if (word.ElapsedMs is not > 0 || word.Morphology?.Attempts is not { } attempts || attempts <= 0)
                continue;
            totalSteps += attempts;
            totalMilliseconds += word.ElapsedMs.Value;
        }

        return totalSteps > 0 && totalMilliseconds > 0
            ? new ParserAttemptRate(totalMilliseconds / totalSteps, IsTypicalMachine: false)
            : TypicalMachineRate;
    }

    /// <summary>Calculates the estimate and ten-times time cap, rounded up to a whole second.</summary>
    /// <param name="parserWorkLimit">The finite per-word parser work limit.</param>
    /// <param name="rate">The measured parser rate or the fallback rate.</param>
    /// <returns><see langword="null"/> when the parser work limit is unbounded.</returns>
    public static ParserWorkLimitEstimate? Calculate(ParserWorkLimit parserWorkLimit, ParserAttemptRate rate)
    {
        ArgumentNullException.ThrowIfNull(parserWorkLimit);
        ArgumentNullException.ThrowIfNull(rate);
        if (parserWorkLimit.IsUnbounded) return null;
        if (rate.MillisecondsPerAttempt <= 0)
            throw new ArgumentOutOfRangeException(nameof(rate), "The parser attempt rate must be positive.");

        decimal estimatedMilliseconds;
        try
        {
            // The rate is measured per analysis attempt, and a limit of 100 units of parser work allows one attempt.
            estimatedMilliseconds = parserWorkLimit.Work!.Value * rate.MillisecondsPerAttempt / ParserWorkLimit.WorkPerAttempt;
            var timeLimitSeconds = decimal.Ceiling(estimatedMilliseconds * 10m / 1000m);
            var timeLimitMs = timeLimitSeconds * 1000m;
            return new ParserWorkLimitEstimate(estimatedMilliseconds,
                timeLimitMs <= long.MaxValue ? (long)timeLimitMs : null, rate.IsTypicalMachine);
        }
        catch (OverflowException)
        {
            return new ParserWorkLimitEstimate(decimal.MaxValue, null, rate.IsTypicalMachine);
        }
    }
}
