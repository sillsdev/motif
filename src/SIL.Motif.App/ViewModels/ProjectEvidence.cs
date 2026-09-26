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
/// Two things feed it, both through <see cref="WorkspaceContext"/>: the stored read model, which the context loads
/// when a project opens and after each Refresh, and a completed in-session run. A stored read replaces only an
/// Assessment that also came from the store, so a run's richer rows stay on screen until the next run or project.
/// A stored read with no matching Assessment, as just after a Refresh, leaves the older numbers on screen, and
/// <see cref="Freshness"/> says they describe an older save.
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
        internal set => Set(ref _baseline, value);
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
    public string? ParseTimeAssessmentId => MeasurementOf(AssessmentKinds.ParseTime) ?? StoredAssessmentId;

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
        Baseline?.ProjectLastWriteUtc is { } written && (Baseline.SourceLastWriteUtc is not { } source || written > source)
            ? written
            : Baseline?.SourceLastWriteUtc;

    /// <summary>Whether the numbers on screen describe the FieldWorks project as it was last saved.</summary>
    public NumbersFreshness Freshness =>
        AppliedSinceRefresh ? NumbersFreshness.AppliedSince
        : MeasuredSaveUtc is { } measured && LatestSaveUtc is { } latest && latest > measured
            ? NumbersFreshness.SavedSince
        : Baseline?.HasBaseline != true ? NumbersFreshness.NoBaseline
        : NumbersFreshness.Current;

    /// <summary>Whether the numbers on screen describe an older state of the project than the current one.</summary>
    public bool IsStale => Freshness is NumbersFreshness.SavedSince or NumbersFreshness.AppliedSince;

    // A run's rows name readings the store cannot, so a stored read replaces only a stored Assessment.
    internal void ShowStored(CurrentEvidenceSnapshot stored)
    {
        Stored = stored;
        if (Assessment is { IsStored: false } || stored.Assessment is not { } assessment) return;
        Assessment = new WorkspaceEvidence(assessment, stored.AssessedUtc, WasRerun: false) { IsStored = true };
    }

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
    }

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (!SetProperty(ref field, value, name)) return;
        foreach (var derived in Derived) OnPropertyChanged(derived);
    }

    private string? MeasurementOf(string kind) => Assessment?.Assessment.Measurements
        .LastOrDefault(measurement => measurement.Kind == kind)?.AssessmentId;
}
