using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Queries;

/// <summary>Identifies whether measured evidence belongs to the project or to a Proposal's Trial.</summary>
public enum EvidenceOriginKind
{
    /// <summary>Evidence measured against the project's captured Baseline.</summary>
    Project,

    /// <summary>Evidence measured against a Proposal applied to a disposable project copy.</summary>
    Trial,
}

/// <summary>Names the project or Trial that produced one evidence set.</summary>
/// <param name="Kind">The source of the evidence.</param>
/// <param name="ProposalId">The Trial's Proposal, or <see langword="null"/> for project evidence.</param>
/// <param name="IntentDigest">The Proposal content measured by the Trial, when present.</param>
/// <param name="JobId">The job that produced the Trial, when present.</param>
public sealed record EvidenceOrigin(
    EvidenceOriginKind Kind,
    string? ProposalId,
    string? IntentDigest,
    string? JobId);

/// <summary>One named body of measurements and the Baseline and origin that give them meaning.</summary>
/// <param name="Baseline">The project Baseline measured against, including for a Trial.</param>
/// <param name="RootAssessmentId">The complete ParseTime Assessment, or <see langword="null"/> before measurement.</param>
/// <param name="ReplacementAssessmentIds">Subset runs that replace answers from the root Assessment.</param>
/// <param name="MeasurementIds">Companion Assessment ids keyed by their kind.</param>
/// <param name="GrammarCheckId">The stored grammar check identity, or <see langword="null"/> when absent.</param>
/// <param name="MeasuredWords">The words represented by the root run and its replacements.</param>
/// <param name="Origin">Whether this evidence belongs to the project or a Proposal's Trial.</param>
public sealed record EvidenceSet(
    BaselineToken Baseline,
    string? RootAssessmentId,
    IReadOnlyList<string> ReplacementAssessmentIds,
    IReadOnlyDictionary<string, string> MeasurementIds,
    string? GrammarCheckId,
    IReadOnlyList<string> MeasuredWords,
    EvidenceOrigin Origin)
{
    /// <summary>Creates a project evidence set from the exact components selected by its resolver.</summary>
    public static EvidenceSet FromProject(BaselineToken baseline, string? rootAssessmentId,
        IReadOnlyList<string> replacementAssessmentIds, IReadOnlyDictionary<string, string> measurementIds,
        string? grammarCheckId, IReadOnlyList<string> measuredWords) =>
        new(baseline, rootAssessmentId, replacementAssessmentIds, measurementIds, grammarCheckId, measuredWords,
            new EvidenceOrigin(EvidenceOriginKind.Project, null, null, null));

    /// <summary>Creates a project evidence set from an Assessment response.</summary>
    public static EvidenceSet FromProjectAssessment(AssessCommandResponse assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        var measurements = assessment.Measurements.Where(item => item.Kind != AssessmentKinds.ParseTime)
            .GroupBy(item => item.Kind, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().AssessmentId, StringComparer.Ordinal);
        var root = assessment.Measurements.LastOrDefault(item => item.Kind == AssessmentKinds.ParseTime)
            ?.AssessmentId;
        return FromProject(assessment.Baseline.Token, root, assessment.TimingOverrideAssessmentIds, measurements,
            grammarCheckId: null, measuredWords: assessment.Words.Select(word => word.Word).ToArray());
    }

    /// <summary>Creates a Trial evidence set from its durable record.</summary>
    public static EvidenceSet FromTrial(TrialRecord trial)
    {
        ArgumentNullException.ThrowIfNull(trial);
        var root = trial.AssessmentIds.GetValueOrDefault(AssessmentKinds.ParseTime);
        var measurements = trial.AssessmentIds
            .Where(pair => pair.Key != AssessmentKinds.ParseTime)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        return new EvidenceSet(trial.Baseline, root, [], measurements, trial.GrammarCheckId, trial.Words,
            new EvidenceOrigin(EvidenceOriginKind.Trial, trial.ProposalId, trial.IntentDigest, trial.JobId));
    }
}
