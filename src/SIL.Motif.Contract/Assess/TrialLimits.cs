namespace SIL.Motif.Contract.Assess;

/// <summary>The per-word time and step limits used by an Assessment Trial.</summary>
/// <param name="PerWordLimit">The time limit for each word, or <see langword="null"/> for no time limit.</param>
/// <param name="PerWordStepLimit">The step limit for each word.</param>
public sealed record TrialLimits(TimeSpan? PerWordLimit, StepCap PerWordStepLimit);
