using SIL.Motif.Contract.Assess;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Queries;

/// <summary>The measured or fallback parser time for one analysis attempt.</summary>
public sealed record ParserStepRate(decimal MillisecondsPerStep, bool IsTypicalMachine);

/// <summary>The estimated word duration and saved time cap for one finite analysis-attempt limit.</summary>
public sealed record StepLimitEstimate(
    decimal EstimatedMilliseconds,
    long? PerWordTimeLimitMs,
    bool IsTypicalMachine);

/// <summary>Derives setup estimates from stored parser timing or a documented typical-machine rate.</summary>
public static class StepLimitEstimator
{
    /// <summary>The fallback rate estimates 250,000 analysis attempts per second.</summary>
    public const decimal TypicalMachineMillisecondsPerStep = 0.004m;

    /// <summary>The fallback rate used before an Assessment has usable parser statistics.</summary>
    public static ParserStepRate TypicalMachineRate { get; } =
        new(TypicalMachineMillisecondsPerStep, IsTypicalMachine: true);

    /// <summary>Uses a stored Assessment's per-word elapsed times and analysis-attempt counts when available.</summary>
    public static ParserStepRate FromAssessment(AssessmentRecord? assessment)
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
            ? new ParserStepRate(totalMilliseconds / totalSteps, IsTypicalMachine: false)
            : TypicalMachineRate;
    }

    /// <summary>Calculates the estimate and ten-times time cap, rounded up to a whole second.</summary>
    /// <param name="stepLimit">The finite per-word analysis-attempt cap.</param>
    /// <param name="rate">The measured parser rate or the fallback rate.</param>
    /// <returns><see langword="null"/> when the analysis-attempt limit is unbounded.</returns>
    public static StepLimitEstimate? Calculate(StepCap stepLimit, ParserStepRate rate)
    {
        ArgumentNullException.ThrowIfNull(stepLimit);
        ArgumentNullException.ThrowIfNull(rate);
        if (stepLimit.IsUnbounded) return null;
        if (rate.MillisecondsPerStep <= 0)
            throw new ArgumentOutOfRangeException(nameof(rate), "The parser attempt rate must be positive.");

        decimal estimatedMilliseconds;
        try
        {
            estimatedMilliseconds = stepLimit.Steps!.Value * rate.MillisecondsPerStep;
            var timeLimitSeconds = decimal.Ceiling(estimatedMilliseconds * 10m / 1000m);
            var timeLimitMs = timeLimitSeconds * 1000m;
            return new StepLimitEstimate(estimatedMilliseconds,
                timeLimitMs <= long.MaxValue ? (long)timeLimitMs : null, rate.IsTypicalMachine);
        }
        catch (OverflowException)
        {
            return new StepLimitEstimate(decimal.MaxValue, null, rate.IsTypicalMachine);
        }
    }
}
