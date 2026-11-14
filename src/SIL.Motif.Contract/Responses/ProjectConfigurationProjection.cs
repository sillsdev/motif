using System.Collections.Generic;
using SIL.Motif.Contract.Assess;

namespace SIL.Motif.Contract.Responses;

/// <summary>The <c>config show</c> report: a project's fully-resolved Assessment configuration.</summary>
public sealed record ProjectConfigurationProjection(
    bool GateOnRegression,
    bool PurgeOnApply,
    IReadOnlyList<AssessmentScopeProjection> Scopes)
{
    /// <summary>Recent standalone Assessments kept per kind alongside cited evidence.</summary>
    public int StandaloneAssessmentCount { get; init; } = 20;

    /// <summary>Parsimony measure ids the project blocks on (<c>[parsimony] block-on</c>); empty means advisory.</summary>
    public IReadOnlyList<string> ParsimonyBlockOn { get; init; } = [];

    /// <summary>Orthographic fold rules for near-duplicate lexemes (<c>[parsimony] fold</c>), each <c>from&gt;to</c>.</summary>
    public IReadOnlyList<string> ParsimonyFold { get; init; } = [];
}

/// <summary>One declared Assessment scope, as reported by <c>config show</c>.</summary>
public sealed record AssessmentScopeProjection(
    string Name,
    string Query,
    string Assessor,
    IReadOnlyList<string> Collect,
    long PerWordLimitMs,
    ParserWorkLimit PerWordParserWorkLimit)
{
    /// <summary>Maximum slowest words retaining per-object timing detail in an ordinary Assessment.</summary>
    public int TimingDetailWords { get; init; } = 2000;
}
