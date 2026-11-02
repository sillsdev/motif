namespace SIL.Motif.Contract.Assess;

/// <summary>The per-word time and parser work limits used by an Assessment Trial.</summary>
/// <param name="PerWordLimit">The time limit for each word, or <see langword="null"/> for no time limit.</param>
/// <param name="PerWordParserWorkLimit">The parser work limit for each word.</param>
public sealed record TrialLimits(TimeSpan? PerWordLimit, ParserWorkLimit PerWordParserWorkLimit);
