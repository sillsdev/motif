using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Whether the numbers on screen describe the FieldWorks project as it was last saved.</summary>
public enum NumbersFreshness
{
    /// <summary>No Baseline has been captured, so there are no numbers to be current or not.</summary>
    NoBaseline,

    /// <summary>The numbers were measured against the latest save Motif knows of.</summary>
    Current,

    /// <summary>FieldWorks has saved the project since the save the numbers were measured against.</summary>
    SavedSince,

    /// <summary>Changes were applied to the FieldWorks project since the numbers were measured.</summary>
    AppliedSince,
}

/// <summary>
/// The evidence every page shows for the open project: the Assessment on screen, its measurements by kind, the
/// Baseline, and one answer to whether the numbers are still current. A page reads it and never computes any of
/// it for itself.
/// </summary>
/// <remarks>
/// Three things feed it through <see cref="WorkspaceContext"/>: the stored read model, which the context loads when
/// a project opens, after each Refresh and when a person comes back to the window; a completed in-session run; and
/// a matching word-context query's comparison with the live save. A stored read replaces the Assessment on screen
/// only when it holds a different one: a stored Assessment
/// whose words changed, or one recorded after the run this window shows, as when an agent ran it from the command
/// line. An identical effective evidence set preserves this window's fresh result; explicit replacements
/// change the set even when the root run is unchanged. A successful Refresh restores stored evidence only when it
/// matches the captured Baseline and Selection.
/// </remarks>
public sealed class ProjectEvidence : ObservableObject
{
    private static readonly string[] Derived =
    [
        nameof(HasAssessment), nameof(Words), nameof(StoredAssessmentId), nameof(ParseTimeAssessmentId),
        nameof(CorrectnessAssessmentId), nameof(ObjectTimingAssessmentId), nameof(TimingOverrideAssessmentIds),
        nameof(MeasuredSaveUtc), nameof(LatestSaveUtc), nameof(Freshness), nameof(IsStale),
    ];

    private WorkspaceEvidence? _assessment;
    private CurrentEvidenceSnapshot? _stored;
    private WorkspaceBaseline? _baseline;
    private bool _appliedSinceRefresh;
    private bool _wordContextIsStale;

    internal ProjectEvidence()
    {
    }

    /// <summary>The Assessment the pages show, or <see langword="null"/> before any has been published.</summary>
    public WorkspaceEvidence? Assessment
    {
        get => _assessment;
        private set => Set(ref _assessment, value);
    }

    /// <summary>The stored read model last loaded for the open project, or <see langword="null"/> before one.</summary>
    public CurrentEvidenceSnapshot? Stored
    {
        get => _stored;
        private set => Set(ref _stored, value);
    }

    /// <summary>The open project's Baseline, whose saves the freshness is measured against.</summary>
    public WorkspaceBaseline? Baseline
    {
        get => _baseline;
        internal set
        {
            var priorToken = _baseline?.Token;
            Set(ref _baseline, value);
            if (priorToken != value?.Token) WordContextIsStale = false;
        }
    }

    /// <summary>Whether a matching Try a Word query found a save newer than the current Baseline.</summary>
    internal bool WordContextIsStale
    {
        get => _wordContextIsStale;
        private set => Set(ref _wordContextIsStale, value);
    }

    /// <summary>Whether changes were applied to the FieldWorks project since the numbers were measured.</summary>
    public bool AppliedSinceRefresh
    {
        get => _appliedSinceRefresh;
        internal set => Set(ref _appliedSinceRefresh, value);
    }

    /// <summary>Whether an Assessment is on screen.</summary>
    public bool HasAssessment => Assessment is not null;

    /// <summary>The words of the Assessment on screen, or none before one.</summary>
    public IReadOnlyList<AssessmentWordResult> Words => Assessment?.Assessment.Words ?? [];

    /// <summary>The stored ParseTime Assessment that matches the current Baseline and default Selection.</summary>
    public string? StoredAssessmentId => Stored?.MatchingAssessment?.AssessmentId;

    /// <summary>The ParseTime measurement of the Assessment on screen, else of the stored one.</summary>
    public string? ParseTimeAssessmentId => Assessment is { } shown
        ? ParseTimeMeasurementOf(shown.Assessment) ?? StoredAssessmentId
        : StoredAssessmentId;

    /// <summary>The Correctness measurement of the Assessment on screen, when it collected one.</summary>
    public string? CorrectnessAssessmentId => MeasurementOf(AssessmentKinds.Correctness);

    /// <summary>The per-rule timing measurement of the Assessment on screen, when it collected one.</summary>
    public string? ObjectTimingAssessmentId => MeasurementOf(AssessmentKinds.ObjectTiming);

    /// <summary>Later ParseTime re-runs whose word timings replace the Assessment's own, in order.</summary>
    public IReadOnlyList<string> TimingOverrideAssessmentIds => Assessment?.Assessment.TimingOverrideAssessmentIds ?? [];

    /// <summary>The FieldWorks save the numbers were measured against: the Assessment's, else the Baseline's.</summary>
    public DateTimeOffset? MeasuredSaveUtc => Assessment?.MeasuredSaveUtc ?? Baseline?.SourceLastWriteUtc;

    /// <summary>The latest FieldWorks save known: the project file as last read, or a later Baseline's save.</summary>
    public DateTimeOffset? LatestSaveUtc =>
        EvidenceFreshnessRule.LatestSave(Baseline?.ProjectLastWriteUtc, Baseline?.SourceLastWriteUtc);

    /// <summary>
    /// Whether the numbers on screen describe the FieldWorks project as it was last saved, by
    /// <see cref="EvidenceFreshnessRule"/> applied to the Assessment on screen, plus what only the window knows:
    /// that changes were applied since.
    /// </summary>
    public NumbersFreshness Freshness =>
        AppliedSinceRefresh ? NumbersFreshness.AppliedSince
        : WordContextIsStale ? NumbersFreshness.SavedSince
        : EvidenceFreshnessRule.Of(Baseline?.HasBaseline == true ? Baseline.SourceLastWriteUtc : null,
                MeasuredSaveUtc, LatestSaveUtc) switch
            {
                EvidenceFreshness.Stale => NumbersFreshness.SavedSince,
                EvidenceFreshness.NoBaseline => NumbersFreshness.NoBaseline,
                _ => NumbersFreshness.Current,
            };

    /// <summary>Whether the numbers on screen describe an older state of the project than the current one.</summary>
    public bool IsStale => Freshness is NumbersFreshness.SavedSince or NumbersFreshness.AppliedSince;

    internal void ShowStored(CurrentEvidenceSnapshot stored)
    {
        Stored = stored;
        if (stored.Assessment is not { } assessment)
        {
            if (Assessment?.IsStored == true) Assessment = null;
            return;
        }
        if (Assessment is { } shown && !IsNewer(shown, assessment, stored.AssessedUtc)) return;
        Assessment = new WorkspaceEvidence(assessment, stored.AssessedUtc, WasRerun: false) { IsStored = true };
    }

    internal void ClearForNewBaseline()
    {
        Assessment = null;
        Stored = null;
        AppliedSinceRefresh = false;
        WordContextIsStale = false;
    }

    /// <summary>Uses a word query's live comparison when it names the current Baseline.</summary>
    internal void ObserveWordContext(WordContextResponse context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.HasBaseline && context.Baseline is { } token && token == Baseline?.Token)
            WordContextIsStale = context.IsStale;
    }

    private static bool IsNewer(WorkspaceEvidence shown, AssessCommandResponse stored, DateTimeOffset? storedAt)
    {
        var sameRun = MeasurementOf(shown.Assessment, AssessmentKinds.ParseTime) ==
            MeasurementOf(stored, AssessmentKinds.ParseTime);
        return shown.IsStored
            ? !sameRun || !SameEffectiveEvidence(shown.Assessment, stored)
            : sameRun ? !SameEffectiveEvidence(shown.Assessment, stored) : storedAt > shown.CompletedAt;
    }

    private static bool SameEffectiveEvidence(AssessCommandResponse left, AssessCommandResponse right) =>
        left.TimingOverrideAssessmentIds.SequenceEqual(right.TimingOverrideAssessmentIds) &&
        left.Words.Select(word => (word.Word, word.Origin)).SequenceEqual(
            right.Words.Select(word => (word.Word, word.Origin)));

    internal void ShowRun(WorkspaceEvidence run)
    {
        Assessment = run;
        AppliedSinceRefresh = false;
    }

    internal void Clear()
    {
        Assessment = null;
        Stored = null;
        Baseline = null;
        AppliedSinceRefresh = false;
        WordContextIsStale = false;
    }

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (!SetProperty(ref field, value, name)) return;
        foreach (var derived in Derived) OnPropertyChanged(derived);
    }

    private string? MeasurementOf(string kind) => Assessment is { } shown ? MeasurementOf(shown.Assessment, kind) : null;

    /// <summary>Selects an Assessment response's typed ParseTime measurement.</summary>
    internal static string? ParseTimeMeasurementOf(AssessCommandResponse assessment) =>
        MeasurementOf(assessment, AssessmentKinds.ParseTime);

    private static string? MeasurementOf(AssessCommandResponse assessment, string kind) => assessment.Measurements
        .LastOrDefault(measurement => measurement.Kind == kind)?.AssessmentId;
}
