namespace SIL.Motif.Host.Assess;

using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Parsimony;

/// <summary>
/// The stored material a Report is computed from — an Assessment's own kind, scope and identity, plus the
/// words and analyses already recorded for it. Read back from the store rather than re-produced, so a
/// Report never needs the Assessor that made the Assessment to still be reachable.
/// </summary>
public sealed record ReportableAssessment(
    string AssessmentId,
    string Assessor,
    string Kind,
    string ScopeJson,
    string SelectionName,
    IReadOnlyList<string> SelectionWords,
    string SelectionSha256,
    string GrammarSourceSha256,
    IReadOnlyList<Parser.AssessedWord> Words);

/// <summary>A closed input family a Report producer can evaluate.</summary>
public abstract record ReportInput
{
    private ReportInput() { }

    /// <summary>A ReportableAssessment selected as the primary input.</summary>
    public sealed record AssessmentMaterial : ReportInput
    {
        internal AssessmentMaterial(ReportableAssessment value) =>
            Value = value ?? throw new ArgumentNullException(nameof(value));

        public ReportableAssessment Value { get; }
    }

    /// <summary>A frozen evidence bundle and the Assessments it explicitly references.</summary>
    public sealed record Parsimony : ReportInput
    {
        internal Parsimony(ParsimonyReportInputs inputs, IReadOnlyList<ReportableAssessment> assessments,
            string? grammarFactsPath = null, string? evidencePath = null, string measureId = "P-adhoc-duplicate")
        {
            Inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
            Assessments = Array.AsReadOnly(assessments.ToArray());
            GrammarFactsPath = grammarFactsPath;
            EvidencePath = evidencePath;
            MeasureId = string.IsNullOrWhiteSpace(measureId)
                ? throw new ArgumentException("A measure ID is required.", nameof(measureId)) : measureId;
        }

        public ParsimonyReportInputs Inputs { get; }
        public IReadOnlyList<ReportableAssessment> Assessments { get; }

        /// <summary>The validated PanGloss facts file, when the caller is producing a new Report.</summary>
        public string? GrammarFactsPath { get; }

        /// <summary>The validated Motif evidence file, when the caller is producing a new Report.</summary>
        public string? EvidencePath { get; }

        /// <summary>The fixed measure this Report evaluates.</summary>
        public string MeasureId { get; }
    }

    /// <summary>Input material that a report kind requires but the caller did not supply.</summary>
    public sealed record Missing : ReportInput
    {
        internal Missing(string capability) => Capability = capability;

        public string Capability { get; }
    }

    /// <summary>Wraps one stored Assessment for an Assessment-based Report.</summary>
    public static ReportInput FromAssessment(ReportableAssessment assessment) =>
        new AssessmentMaterial(assessment ?? throw new ArgumentNullException(nameof(assessment)));

    /// <summary>Wraps frozen Parsimony inputs and their loaded Assessment references.</summary>
    public static ReportInput FromParsimony(
        ParsimonyReportInputs inputs, IReadOnlyList<ReportableAssessment>? assessments = null)
        => CreateParsimony(inputs, assessments, null, null);

    /// <summary>Wraps validated artifact files for one new Parsimony Report.</summary>
    public static ReportInput FromParsimonyFiles(
        ParsimonyReportInputs inputs, string grammarFactsPath, string evidencePath,
        IReadOnlyList<ReportableAssessment>? assessments = null, string measureId = "P-adhoc-duplicate")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(grammarFactsPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidencePath);
        return CreateParsimony(inputs, assessments, grammarFactsPath, evidencePath, measureId);
    }

    private static ReportInput CreateParsimony(ParsimonyReportInputs inputs,
        IReadOnlyList<ReportableAssessment>? assessments, string? grammarFactsPath, string? evidencePath,
        string measureId = "P-adhoc-duplicate")
    {
        ArgumentNullException.ThrowIfNull(inputs);
        assessments ??= Array.Empty<ReportableAssessment>();
        if (assessments.Any(assessment => assessment is null))
            throw new ArgumentException("Loaded Assessments must not contain null entries.", nameof(assessments));
        var assessmentIds = assessments.Select(assessment => assessment.AssessmentId).ToArray();
        if (!inputs.AssessmentIds.SequenceEqual(assessmentIds, StringComparer.Ordinal))
            throw new ArgumentException(
                "Loaded Assessments must match the frozen Assessment references in order.", nameof(assessments));
        if ((grammarFactsPath is null) != (evidencePath is null))
            throw new ArgumentException("Both frozen artifact paths must be supplied together.");
        return new Parsimony(inputs, assessments, grammarFactsPath, evidencePath, measureId);
    }

    /// <summary>Creates an input whose missing capability can be named in a producer refusal.</summary>
    public static ReportInput MissingCapability(string capability) =>
        new Missing(string.IsNullOrWhiteSpace(capability)
            ? throw new ArgumentException("A capability name is required.", nameof(capability))
            : capability);

    /// <summary>Requires a primary Assessment and names the absent input family on refusal.</summary>
    public ReportableAssessment RequireAssessment(string reportKind)
    {
        ArgumentNullException.ThrowIfNull(reportKind);
        return this switch
        {
            AssessmentMaterial assessment => assessment.Value,
            Missing missing => throw new ReportRefusalException(reportKind,
                $"This report needs the '{missing.Capability}' capability, but that input was not supplied."),
            Parsimony => throw new ReportRefusalException(reportKind,
                "This report needs an Assessment input; a Parsimony evidence bundle was supplied instead."),
            _ => throw new ArgumentOutOfRangeException(nameof(reportKind)),
        };
    }
}

/// <summary>The optional narrowing a caller of <c>motif report</c> may supply; unused by every kind so far.</summary>
public sealed record ReportQuery(string? Word = null, string? Text = null);

/// <summary>One Report's rendered, storable form — what <c>Reports.RenderedText</c> keeps.</summary>
public sealed record RenderedReport(string Kind, string Text)
{
    public int? TotalSearches { get; init; }
    public int? CompletedSearches { get; init; }

    /// <summary>The typed advice rows carried by a Parsimony report.</summary>
    public IReadOnlyList<ParsimonyFinding>? ParsimonyFindings { get; init; }

    /// <summary>The complete requested measure outcomes carried by a Parsimony report.</summary>
    public IReadOnlyList<ParsimonyMeasureRun>? ParsimonyMeasureRuns { get; init; }

    /// <summary>The distinct evidence denominators carried by a Parsimony report.</summary>
    public ParsimonyJoinQuality? ParsimonyJoinQuality { get; init; }
    public ParsimonyDispositionProjection? ParsimonyDispositionProjection { get; init; }

    /// <summary>The information lines for checks that could not look, carried by a Parsimony report.</summary>
    public IReadOnlyList<ParsimonyNote>? ParsimonyNotes { get; init; }
}

/// <summary>
/// Raised when a report kind cannot be produced from the Assessment it was asked about — ADR 0042 decision
/// 4's *"this scope did not collect X"*, never a zero indistinguishable from a real one.
/// </summary>
public sealed class ReportRefusalException : Exception
{
    public ReportRefusalException(string kind, string reason) : base(reason) => Kind = kind;

    /// <summary>The report kind that was refused.</summary>
    public string Kind { get; }
}

/// <summary>
/// Produces one report kind from a closed <see cref="ReportInput"/> family. Registered into a
/// <see cref="ReportCatalog"/>, never dispatched by name in a caller's own switch — that is the whole
/// point of the registry (ADR 0042 decision 4's amendment on Reports).
/// </summary>
/// <remarks>
/// <see cref="Produce"/>'s <c>assessors</c> parameter exists so a kind that needs to ask the Assessor that
/// owns a raw format (for example reading a PanGloss stats cache) has a seam to do it through, without
/// every producer needing one. A kind that renders entirely from the Assessment's own stored rows — every
/// kind registered so far — never calls it.
/// </remarks>
public interface IReportProducer
{
    /// <summary>The registry key this kind is asked for under, and what <c>--list-kinds</c> names.</summary>
    string Kind { get; }

    /// <summary>One line describing what this kind reports, for <c>--list-kinds</c>.</summary>
    string Description { get; }

    /// <exception cref="ReportRefusalException">
    /// <paramref name="input"/> does not contain the material this kind can report from.
    /// </exception>
    RenderedReport Produce(ReportInput input, ReportQuery query, IAssessorCatalog assessors);
}
