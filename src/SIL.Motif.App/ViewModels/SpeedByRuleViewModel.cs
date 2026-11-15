using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Reads stored timing rows grouped by rule for the current Assessment.</summary>
public sealed partial class SpeedByRuleViewModel : ObservableObject
{
    private readonly WorkspaceContext _context;
    private int _generation;

    public SpeedByRuleViewModel(WorkspaceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        context.Evidence.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ProjectEvidence.ParseTimeAssessmentId) or
                nameof(ProjectEvidence.TimingOverrideAssessmentIds) ||
                e.PropertyName == nameof(ProjectEvidence.IsStale) && context.Evidence.IsStale) Clear();
        };
    }

    /// <summary>Timing rows from the recorded Assessment.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRows))]
    private IReadOnlyList<TimingWorkAggregate> _rows = [];

    /// <summary>Whether any recorded rule rows are available.</summary>
    public bool HasRows => Rows.Count > 0;

    internal void Clear()
    {
        _generation++;
        Rows = [];
    }

    internal async Task ReadAsync(string projectPath, CancellationToken cancellationToken)
    {
        var generation = ++_generation;
        var assessmentId = _context.Evidence.ParseTimeAssessmentId;
        if (assessmentId is null || _context.Evidence.IsStale)
        {
            Rows = [];
            return;
        }
        var overrides = _context.Evidence.TimingOverrideAssessmentIds.ToArray();
        var result = await _context.Commands.TimingAsync(new TimingRequest(projectPath, assessmentId, By: "rule",
            OverrideAssessmentIds: overrides),
            cancellationToken).ConfigureAwait(true);
        if (generation != _generation || cancellationToken.IsCancellationRequested || _context.ProjectPath != projectPath ||
            assessmentId != _context.Evidence.ParseTimeAssessmentId ||
            !overrides.SequenceEqual(_context.Evidence.TimingOverrideAssessmentIds)) return;
        Rows = result.Succeeded && result.Value is { IsStale: false, CurrentProjectIsStale: false,
            ObjectTimingAggregatesAvailable: true } timing && timing.AssessmentId == assessmentId && timing.By == "rule"
            ? timing.ParserWorkAggregates : [];
    }
}
