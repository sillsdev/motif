using System.Globalization;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
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

    /// <summary>
    /// The short read: how many measures found new or changed Findings, never a count of Findings or a score. It is
    /// empty unless the whole is Ready, because an Out of Date run does not say whether to go ahead. It says nothing
    /// is new only when every Grammar measure computed and every new Finding was checked against decisions, and it always says
    /// what did not give a result.
    /// </summary>
    /// <param name="status">The Proposal's Parsimony status.</param>
    /// <param name="readings">What each measure's new Findings came to once checked against decisions, or null to take the Report's counts as they stand.</param>
    public static string Read(ProposalParsimonyStatus status, IReadOnlyList<ProposalParsimonyMeasureReading>? readings = null)
    {
        if (status.State != ProposalParsimonyState.Ready) return string.Empty;
        readings ??= CountedReadings(status);
        var ready = status.Groups.Where(group => group.State == ProposalParsimonyGroupState.Ready).ToArray();
        var runs = ready.SelectMany(group => group.MeasureRuns).ToArray();
        var computed = runs.Count(run => run.Status == ParsimonyMeasureStatus.Computed);
        var found = readings.Count(reading => reading.Found);
        var unsettled = readings.Count(reading => reading.Unsettled);
        var lines = new List<string>();
        if (found > 0) lines.Add(PluralText.Format(MotifAppStrings.ProposalParsimonyReadNewPattern, found));
        else if (unsettled == 0 && readings.Any(reading => reading.SetAside))
            lines.Add(MotifAppStrings.ProposalParsimonyFindingsAllDecided);
        else if (unsettled == 0 && computed > 0)
            lines.Add(computed == runs.Length && IsReady(status, ContractGroup.Grammar)
                ? MotifAppStrings.ProposalParsimonyReadNothingNew : MotifAppStrings.ProposalParsimonyReadNothingNewComputed);
        var resolved = ready.SelectMany(group => group.Changes).Count(change => change.ResolvedFindings > 0);
        if (resolved > 0) lines.Add(PluralText.Format(MotifAppStrings.ProposalParsimonyReadResolvedPattern, resolved));
        if (unsettled > 0) lines.Add(PluralText.Format(MotifAppStrings.ProposalParsimonyReadUnknownPattern, unsettled));
        lines.AddRange(Gaps(status));
        return string.Join("\n", lines);
    }

    /// <summary>
    /// What a Ready whole did not measure or compute, which holds whether or not the Findings are read yet; empty
    /// unless the whole is Ready.
    /// </summary>
    /// <param name="status">The Proposal's Parsimony status.</param>
    public static string Caveats(ProposalParsimonyStatus status) => string.Join("\n", Gaps(status));

    private static IEnumerable<string> Gaps(ProposalParsimonyStatus status)
    {
        if (status.State != ProposalParsimonyState.Ready) yield break;
        var notComputed = status.Groups.Where(group => group.State == ProposalParsimonyGroupState.Ready)
            .SelectMany(group => group.MeasureRuns).Count(run => run.Status != ParsimonyMeasureStatus.Computed);
        if (notComputed > 0) yield return PluralText.Format(MotifAppStrings.ProposalParsimonyReadNotComputedPattern, notComputed);
        if (!IsReady(status, ContractGroup.Grammar)) yield return MotifAppStrings.ProposalParsimonyReadGrammarNoResult;
        if (!IsReady(status, ContractGroup.Text)) yield return MotifAppStrings.ProposalParsimonyReadTextNotMeasured;
    }

    private static bool IsReady(ProposalParsimonyStatus status, ContractGroup kind) =>
        status.Groups.Any(group => group.Group == kind && group.State == ProposalParsimonyGroupState.Ready);

    // Without a reader of the stored Reports, the Report's own counts are the whole answer.
    private static ProposalParsimonyMeasureReading[] CountedReadings(ProposalParsimonyStatus status) =>
        [.. status.Groups.Where(group => group.State == ProposalParsimonyGroupState.Ready).SelectMany(group => group.Changes)
            .Select(change => new ProposalParsimonyMeasureReading(change.MeasureId,
                change.NewFindings + change.EvidenceChangedFindings, change.NewFindings + change.EvidenceChangedFindings, true))];

    /// <summary>How long the finished job took as wall time in seconds or minutes, or empty when it recorded none.</summary>
    public static string WallTime(ProposalParsimonyStatus status)
    {
        if (status.State is not (ProposalParsimonyState.Ready or ProposalParsimonyState.Stale)) return string.Empty;
        var total = status.Job?.StageTimings?.FirstOrDefault(timing => timing.Stage == "total" && timing.Side is null);
        if (total is null) return string.Empty;
        var seconds = Math.Max(1, (long)Math.Round(total.Milliseconds / 1000.0, MidpointRounding.AwayFromZero));
        return seconds < 120
            ? PluralText.Format(MotifAppStrings.ProposalParsimonyWallTimeSecondsPattern, seconds)
            : PluralText.Format(MotifAppStrings.ProposalParsimonyWallTimeMinutesPattern, (seconds + 30) / 60);
    }

    /// <summary>
    /// The Trial's parser work as a percent change against the Baseline and never as a level, or empty when the
    /// Proposal has no Trial.
    /// </summary>
    public static string ParserWork(TrialWork? work)
    {
        if (work is null) return string.Empty;
        if (work is not { BaselineWork: { } baseline, TrialParserWork: { } trial })
            return MotifAppStrings.ProposalWorkNotComparable;
        var percent = work.PercentChange ?? (baseline > 0 ? (trial - baseline) * 100.0 / baseline : (double?)null);
        if (percent is not { } change) return MotifAppStrings.ProposalWorkNotComparable;
        var whole = TrialWorkNotice.WholePercent(change);
        if (whole == 0) return MotifAppStrings.ProposalParsimonyParserWorkSame;
        return Format(change > 0 ? MotifAppStrings.ProposalParsimonyParserWorkMoreFormat
            : MotifAppStrings.ProposalParsimonyParserWorkLessFormat, whole);
    }

    /// <summary>One group's words; a measured group of a Stale whole reads Out of Date, with its counts kept.</summary>
    public static ProposalParsimonyGroupViewModel Group(ProposalParsimonyGroup group, ProposalParsimonyState whole) => new(
        group.Group == ContractGroup.Grammar ? MotifAppStrings.ProposalParsimonyGroupGrammar
            : MotifAppStrings.ProposalParsimonyGroupText,
        whole == ProposalParsimonyState.Stale && group.State == ProposalParsimonyGroupState.Ready
            ? MotifAppStrings.ProposalParsimonyStateStale : GroupStateLabel(group.State),
        GroupNote(group), [.. MeasureLines(group)])
    {
        Key = group.Group == ContractGroup.Grammar ? "grammar" : "text",
    };

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

    internal static string MeasureName(string measureId) => Recipes.Value.Find(measureId)?.Title ?? measureId;

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
