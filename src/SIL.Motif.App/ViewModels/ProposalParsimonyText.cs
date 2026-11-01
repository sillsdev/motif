using System.Globalization;
using SIL.Motif.Contract.Parsimony;
using ContractGroup = SIL.Motif.Contract.Parsimony.ParsimonyMeasureGroup;
using SIL.Motif.Help;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The window's words for a Proposal's Parsimony status. Parsimony is advice, so a job that did not finish reads as
/// advice that did not finish and never as something that blocks the Trial, Readiness or Apply.
/// </summary>
internal static class ProposalParsimonyText
{
    // Pinned by `AFinishedTrialWithNoQueuedRunIsNotYetRunAndTheQueryQueuesNothing`.
    internal const string NotQueuedCode = "status-not-queued";

    // Only adds a sentence: without a match the timed-out cause still reads whole.
    internal const string SkippedTimeoutCode = "status-skipped-timeout";

    private static readonly Lazy<ParsimonyRecipeCatalog> Recipes = new(ParsimonyRecipeCatalog.Load);

    public static string StateLabel(ProposalParsimonyState state) => state switch
    {
        ProposalParsimonyState.NotSetUp => MotifAppStrings.ProposalParsimonyStateNotSetUp,
        ProposalParsimonyState.NotYetRun => MotifAppStrings.ProposalParsimonyStateNotYetRun,
        ProposalParsimonyState.Queued => MotifAppStrings.ProposalParsimonyStateQueued,
        ProposalParsimonyState.Running => MotifAppStrings.ProposalParsimonyStateRunning,
        ProposalParsimonyState.Ready => MotifAppStrings.ProposalParsimonyStateReady,
        ProposalParsimonyState.Stale => MotifAppStrings.ProposalParsimonyStateStale,
        ProposalParsimonyState.Failed => MotifAppStrings.ProposalParsimonyStateFailed,
        _ => MotifAppStrings.ProposalParsimonyStateUnknown,
    };

    public static string Sentence(ProposalParsimonyStatus status) => status.State switch
    {
        ProposalParsimonyState.NotSetUp => MotifAppStrings.ProposalParsimonyNotSetUp,
        ProposalParsimonyState.NotYetRun => status.StateReason?.Code == NotQueuedCode
            ? MotifAppStrings.ProposalParsimonyNotYetRunWaiting : MotifAppStrings.ProposalParsimonyNotYetRunNoTrial,
        ProposalParsimonyState.Queued => status.Job?.QueuedBehind switch
        {
            "trial" => MotifAppStrings.ProposalParsimonyQueuedTrial,
            "parse" => MotifAppStrings.ProposalParsimonyQueuedParse,
            _ => MotifAppStrings.ProposalParsimonyQueuedOther,
        },
        ProposalParsimonyState.Running => Running(status),
        ProposalParsimonyState.Ready => MotifAppStrings.ProposalParsimonyReady,
        ProposalParsimonyState.Stale => Stale(status.Stale),
        ProposalParsimonyState.Failed => Failed(status),
        _ => MotifAppStrings.ProposalParsimonyUnknown,
    };

    /// <summary>Ready says what it did not check, so a person never reads it as current on those inputs.</summary>
    public static string Note(ProposalParsimonyStatus status) =>
        status.State == ProposalParsimonyState.Ready ? MotifAppStrings.ProposalParsimonyReadyBehind : string.Empty;

    /// <summary>One group's words; a measured group of a Stale whole reads Out of Date, with its counts kept.</summary>
    public static ProposalParsimonyGroupViewModel Group(ProposalParsimonyGroup group, ProposalParsimonyState whole) => new(
        group.Group == ContractGroup.Grammar ? MotifAppStrings.ProposalParsimonyGroupGrammar
            : MotifAppStrings.ProposalParsimonyGroupText,
        whole == ProposalParsimonyState.Stale && group.State == ProposalParsimonyGroupState.Ready
            ? MotifAppStrings.ProposalParsimonyStateStale : GroupStateLabel(group.State),
        GroupNote(group), [.. MeasureLines(group)]);

    private static string GroupStateLabel(ProposalParsimonyGroupState state) => state switch
    {
        ProposalParsimonyGroupState.NotSetUp => MotifAppStrings.ProposalParsimonyStateNotSetUp,
        ProposalParsimonyGroupState.NotYetRun => MotifAppStrings.ProposalParsimonyStateNotYetRun,
        ProposalParsimonyGroupState.Ready => MotifAppStrings.ProposalParsimonyStateReady,
        ProposalParsimonyGroupState.Abstained => MotifAppStrings.ProposalParsimonyStateAbstained,
        ProposalParsimonyGroupState.Failed => MotifAppStrings.ProposalParsimonyStateFailed,
        _ => MotifAppStrings.ProposalParsimonyStateUnknown,
    };

    private static string GroupNote(ProposalParsimonyGroup group)
    {
        if (group.State == ProposalParsimonyGroupState.Abstained) return MotifAppStrings.ProposalParsimonyGroupAbstained;
        if (group.State == ProposalParsimonyGroupState.Failed) return MotifAppStrings.ProposalParsimonyGroupFailed;
        if (group.State == ProposalParsimonyGroupState.Ready) return string.Empty;
        if (group.Group == ContractGroup.Grammar)
            return group.State == ProposalParsimonyGroupState.NotYetRun
                ? MotifAppStrings.ProposalParsimonyGrammarNotYetRun : string.Empty;
        return group.State == ProposalParsimonyGroupState.NotSetUp
            ? MotifAppStrings.ProposalParsimonyTextGroupNeedsSelection : MotifAppStrings.ProposalParsimonyTextGroupManual;
    }

    // One line per measure, in the order the group ran them; counts are never summed across measures.
    private static IEnumerable<string> MeasureLines(ProposalParsimonyGroup group)
    {
        var counted = group.Changes.ToDictionary(change => change.MeasureId, StringComparer.Ordinal);
        foreach (var run in group.MeasureRuns)
        {
            var name = MeasureName(run.MeasureId);
            if (counted.Remove(run.MeasureId, out var change)) yield return Counts(name, change);
            else yield return Format(StatusLine(run.Status), name);
        }
        foreach (var change in counted.Values) yield return Counts(MeasureName(change.MeasureId), change);
    }

    private static string Counts(string name, ProposalParsimonyMeasureChange change) => Format(
        MotifAppStrings.ProposalParsimonyMeasureCountsFormat, name, change.NewFindings, change.ResolvedFindings,
        change.UnchangedFindings, change.EvidenceChangedFindings);

    private static string StatusLine(ParsimonyMeasureStatus status) => status switch
    {
        ParsimonyMeasureStatus.NotRun => MotifAppStrings.ProposalParsimonyMeasureNotRun,
        ParsimonyMeasureStatus.NotAvailable => MotifAppStrings.ProposalParsimonyMeasureNotAvailable,
        ParsimonyMeasureStatus.Inconclusive => MotifAppStrings.ProposalParsimonyMeasureInconclusive,
        ParsimonyMeasureStatus.Failed => MotifAppStrings.ProposalParsimonyMeasureFailed,
        _ => MotifAppStrings.ProposalParsimonyMeasureNoCounts,
    };

    private static string MeasureName(string measureId) => Recipes.Value.Find(measureId)?.Title ?? measureId;

    private static string Running(ProposalParsimonyStatus status)
    {
        if (status.Job?.Stage is not { } stage) return MotifAppStrings.ProposalParsimonyRunning;
        return StageName(stage.Stage) is { } name
            ? Format(MotifAppStrings.ProposalParsimonyRunningStageFormat, name, stage.Step, stage.Steps)
            : Format(MotifAppStrings.ProposalParsimonyRunningStepFormat, stage.Step, stage.Steps);
    }

    private static string? StageName(string stage) => stage switch
    {
        "facts" => MotifAppStrings.ProposalParsimonyStageFacts,
        "validate" => MotifAppStrings.ProposalParsimonyStageValidate,
        "measure" => MotifAppStrings.ProposalParsimonyStageMeasure,
        "render" => MotifAppStrings.ProposalParsimonyStageRender,
        "waiting-for-parser-slot" => MotifAppStrings.ProposalParsimonyStageWaitingForParser,
        _ => null,
    };

    private static string Stale(ProposalParsimonyStale? stale)
    {
        if (stale is null || stale.Reasons.Count == 0) return MotifAppStrings.ProposalParsimonyStaleUnknown;
        return Format(MotifAppStrings.ProposalParsimonyStaleFormat, string.Join(", ", stale.Reasons.Select(Changed)));
    }

    private static string Changed(ProposalParsimonyStaleReason reason) => reason switch
    {
        ProposalParsimonyStaleReason.BaselineChanged => MotifAppStrings.ProposalParsimonyChangedBaseline,
        ProposalParsimonyStaleReason.ProposalChanged => MotifAppStrings.ProposalParsimonyChangedProposal,
        ProposalParsimonyStaleReason.PrerequisiteChanged => MotifAppStrings.ProposalParsimonyChangedPrerequisite,
        ProposalParsimonyStaleReason.ParserChanged => MotifAppStrings.ProposalParsimonyChangedParser,
        ProposalParsimonyStaleReason.SchemaChanged => MotifAppStrings.ProposalParsimonyChangedSchema,
        ProposalParsimonyStaleReason.MeasuresChanged => MotifAppStrings.ProposalParsimonyChangedMeasures,
        ProposalParsimonyStaleReason.SelectionChanged => MotifAppStrings.ProposalParsimonyChangedSelection,
        ProposalParsimonyStaleReason.JudgmentsChanged => MotifAppStrings.ProposalParsimonyChangedJudgments,
        _ => MotifAppStrings.ProposalParsimonyChangedUnknown,
    };

    private static string Failed(ProposalParsimonyStatus status)
    {
        var category = status.Failure?.Category ?? ProposalParsimonyFailureCategory.Internal;
        var cause = category switch
        {
            ProposalParsimonyFailureCategory.TimedOut => MotifAppStrings.ProposalParsimonyFailedTimedOut,
            ProposalParsimonyFailureCategory.ParserUnavailable => MotifAppStrings.ProposalParsimonyFailedParserUnavailable,
            ProposalParsimonyFailureCategory.FactsFailed => MotifAppStrings.ProposalParsimonyFailedFactsFailed,
            ProposalParsimonyFailureCategory.EvidenceFailed => MotifAppStrings.ProposalParsimonyFailedEvidenceFailed,
            ProposalParsimonyFailureCategory.Cancelled => MotifAppStrings.ProposalParsimonyFailedCancelled,
            _ => MotifAppStrings.ProposalParsimonyFailedInternal,
        };
        if (category == ProposalParsimonyFailureCategory.TimedOut && status.StateReason?.Code == SkippedTimeoutCode)
            cause += " " + MotifAppStrings.ProposalParsimonyFailedNotAgain;
        return Format(MotifAppStrings.ProposalParsimonyFailedFormat, cause);
    }

    private static string Format(string format, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, format, arguments);
}
