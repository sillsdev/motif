using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Host.Assess;

namespace SIL.Motif.Host.Parsimony;

/// <summary>The parser limits and executable identity actually used for one side of verification.</summary>
public sealed record RecipeVerificationOptions(
    string ParserSha256,
    int? PerWordTimeoutMs,
    StepCap PerWordStepLimit,
    int Threads,
    bool CollectStatistics);

/// <summary>One parser Assessment and its retained source, selection, and invocation evidence.</summary>
public sealed record RecipeVerificationRun(
    ComparableAssessment Assessment,
    BaselineToken Baseline,
    RecipeVerificationOptions? Options,
    IReadOnlyList<string> Unavailable,
    IReadOnlyList<string> SelectionWords);
