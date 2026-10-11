using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host.Parsimony;

/// <summary>
/// Decides whether a Parsimony request is admissible and names every way it is not. The enqueue commands call it
/// before a job exists and the worker calls it again when it claims one, so a request refused at the door and a
/// job refused at the claim carry the same code and the same words.
/// </summary>
public static class ParsimonyAdmission
{
    /// <summary>The sentence for a candidate request whose Dry Run is absent, unfinished or unpublished.</summary>
    public const string DryRunRequiredMessage =
        "Candidate evidence requires a completed Dry Run job with published effects.";

    /// <summary>The measure value that asks for every measure this build supports, in one job.</summary>
    public const string AllMeasures = "all";

    /// <summary>The measure ids this build runs, in catalog order; what <see cref="AllMeasures"/> expands to.</summary>
    public static IReadOnlyList<string> SupportedMeasureIds() =>
        [.. MeasureCatalog.All.Where(measure => MeasureRunner.Supports(measure.Id)).Select(measure => measure.Id)];

    /// <summary>Checks the request fields common to the report and candidate measures; null means admissible.</summary>
    /// <param name="measureId">The requested measure id.</param>
    /// <param name="evidenceScope">The requested evidence scope, or null when the request names none.</param>
    /// <param name="assessmentIds">The parser Assessment references, or null for none.</param>
    public static Refusal? Check(string? measureId, ParsimonyEvidenceScopeKind? evidenceScope,
        IReadOnlyList<string>? assessmentIds) =>
        Check(measureId, evidenceScope, assessmentIds, MeasureRunner.Supports);

    /// <summary>As <see cref="Check(string?, ParsimonyEvidenceScopeKind?, IReadOnlyList{string}?)"/> with the
    /// build's measure support supplied, so a test can name a catalogued measure the build lacks.</summary>
    public static Refusal? Check(string? measureId, ParsimonyEvidenceScopeKind? evidenceScope,
        IReadOnlyList<string>? assessmentIds, Func<string, bool> supportsMeasure)
    {
        ArgumentNullException.ThrowIfNull(supportsMeasure);
        if (string.IsNullOrWhiteSpace(measureId) || MeasureCatalog.Find(measureId) is null)
            return new Refusal(ParsimonyRefusalCodes.InvalidMeasure, FailureReason.InvalidArgument,
                $"Unknown Parsimony measure '{measureId}'.");
        if (!supportsMeasure(measureId))
            return new Refusal(ParsimonyRefusalCodes.MeasureUnavailable, FailureReason.Refused,
                $"Parsimony measure '{measureId}' is catalogued but not supported by this build.");
        if (evidenceScope is not { } scope || !Enum.IsDefined(scope))
            return new Refusal(ParsimonyRefusalCodes.InvalidScope, FailureReason.InvalidArgument,
                "Evidence scope must be 'default-selection' or 'project-approved'.");
        return CheckAssessmentIds(assessmentIds);
    }

    /// <summary>
    /// As the four-argument check for a Baseline report request, which may also name <see cref="AllMeasures"/>.
    /// A candidate request never does: its comparison is built for one named measure.
    /// </summary>
    public static Refusal? CheckReport(string? measureId, ParsimonyEvidenceScopeKind? evidenceScope,
        IReadOnlyList<string>? assessmentIds, Func<string, bool> supportsMeasure)
    {
        ArgumentNullException.ThrowIfNull(supportsMeasure);
        if (measureId != AllMeasures) return Check(measureId, evidenceScope, assessmentIds, supportsMeasure);
        if (!MeasureCatalog.All.Any(measure => supportsMeasure(measure.Id)))
            return new Refusal(ParsimonyRefusalCodes.MeasureUnavailable, FailureReason.Refused,
                "This build supports no Parsimony measure.");
        if (evidenceScope is not { } scope || !Enum.IsDefined(scope))
            return new Refusal(ParsimonyRefusalCodes.InvalidScope, FailureReason.InvalidArgument,
                "Evidence scope must be 'default-selection' or 'project-approved'.");
        return CheckAssessmentIds(assessmentIds);
    }

    /// <summary>Checks that parser Assessment references are nonblank and unique; null means admissible.</summary>
    public static Refusal? CheckAssessmentIds(IReadOnlyList<string>? assessmentIds)
    {
        var ids = assessmentIds ?? [];
        return ids.Any(string.IsNullOrWhiteSpace) ||
               ids.Distinct(StringComparer.Ordinal).Count() != ids.Count
            ? new Refusal(ParsimonyRefusalCodes.InvalidAssessment, FailureReason.InvalidArgument,
                "Assessment references must be nonblank and unique.")
            : null;
    }

    /// <summary>Checks that a candidate request names a Dry Run job at all; null means admissible.</summary>
    public static Refusal? CheckDryRunJobId(string? dryRunJobId) =>
        string.IsNullOrWhiteSpace(dryRunJobId)
            ? new Refusal(ParsimonyRefusalCodes.DryRunRequired, FailureReason.Refused, DryRunRequiredMessage)
            : null;
}
