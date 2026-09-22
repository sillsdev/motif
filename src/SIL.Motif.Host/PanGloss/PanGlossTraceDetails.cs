namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// What <c>pangloss parse --trace-details</c> reports about one traced word besides the tree: how its search
/// ended, how long the parser itself took, and where the effort went. Every number is the parser's own.
/// </summary>
/// <param name="Capped">The search stopped at PanGloss's step cap before exploring everything.</param>
/// <param name="TimedOut">The search stopped at PanGloss's own time limit.</param>
/// <param name="InvalidShape">The word's shape could not be traced at all, so no search ran.</param>
/// <param name="Steps">The parser's own step count, the counter its step cap measures.</param>
/// <param name="ElapsedNs">How long the parser's search took, excluding process start and grammar loading.</param>
/// <param name="Guessed">Whether the parser's guesser, rather than the lexicon, supplied the result.</param>
/// <param name="Categories">The parser's effort by kind of grammar object, in the parser's own order.</param>
public sealed record PanGlossTraceDetails(
    bool Capped,
    bool TimedOut,
    bool InvalidShape,
    long Steps,
    long ElapsedNs,
    bool Guessed,
    IReadOnlyList<PanGlossTraceCategory> Categories)
{
    /// <summary>Whether the search explored everything it meant to.</summary>
    public bool ReportedCompleted { get; init; } = true;
    public bool SearchCompleted => ReportedCompleted && !Capped && !TimedOut && !InvalidShape;
}

/// <summary>
/// The parser's counters for one kind of grammar object while tracing one word, summed over every object of
/// that kind.
/// </summary>
/// <param name="Kind">The parser's own name: <c>morphRule</c>, <c>phonRule</c>, <c>lexEntry</c>,
/// <c>rootIndex</c>, <c>guesser</c> or <c>overlay</c>.</param>
/// <param name="Attempts">How many times an object of this kind was tried.</param>
/// <param name="Work">The parser's own measure of work done inside those attempts.</param>
/// <param name="Outputs">How many attempts produced something.</param>
/// <param name="NotApplied">How many attempts did not apply.</param>
/// <param name="NoRoot">How many lookups found no root.</param>
/// <param name="SurfaceMismatch">How many syntheses failed to reproduce the word's surface form.</param>
/// <param name="Uses">How many times an object of this kind was used in a result.</param>
/// <param name="SelfElapsedNs">Time spent in this kind alone, or <see langword="null"/> when the parser does not
/// time this kind. A zero is a measurement, never a stand-in for "not measured".</param>
public sealed record PanGlossTraceCategory(
    string Kind,
    long Attempts,
    long Work,
    long Outputs,
    long NotApplied,
    long NoRoot,
    long SurfaceMismatch,
    long Uses,
    long? SelfElapsedNs);
