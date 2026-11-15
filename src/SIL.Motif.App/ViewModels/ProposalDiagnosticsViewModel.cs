using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Advisory evidence owned by one open Proposal document or one side of Compare.</summary>
public sealed partial class ProposalDiagnosticsViewModel : ObservableObject, IDisposable
{
    private readonly DiagnosticsPageModel _owner;
    private WorkspaceContext Context { get; }
    private CancellationTokenSource _stop = new();
    private readonly List<Task> _reads = [];
    private int _generation;
    private bool _disposed;
    private string? _proposalId;
    private string? _intentDigest;
    private TrialSummary? _trial;
    private readonly List<Task> _watches = [];

    public ProposalDiagnosticsViewModel(WorkspaceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
        _owner = context.GetOrCreatePage(static owner => new DiagnosticsPageModel(owner));
        _owner.Register(this);
        Parsimony = new ProposalParsimonyViewModel(context.Commands.ReadProposalParsimonyStatusAsync,
            readReport: context.Commands.ReadParsimonyReportAsync, readView: context.Commands.ReadParsimonyViewAsync);
        context.PropertyChanged += OnContextChanged;
        context.Evidence.PropertyChanged += OnEvidenceChanged;
    }

    /// <summary>The candidate's stored advisory status and named findings.</summary>
    public ProposalParsimonyViewModel Parsimony { get; }

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private string _availabilityText = MotifAppStrings.DiagnosticsTrialUnavailable;

    [ObservableProperty]
    private string _warningsText = string.Empty;

    [ObservableProperty]
    private string _stepsText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<string> _operations = [];

    /// <summary>The identity of the Proposal whose diagnostic row is shown.</summary>
    public string AutomationId => AutomationIds.ForProposalDiagnostics(_proposalId ?? string.Empty);

    /// <summary>The scoped fold for this Proposal's Parsimony evidence.</summary>
    public string ParsimonyFoldAutomationId => AutomationId + ":parsimony";

    /// <summary>Selects the exact content and Trial the owning Proposal document displays.</summary>
    public Task SetProposalAsync(string proposalId, string intentDigest, TrialSummary? trial,
        CancellationToken cancellationToken = default)
    {
        Clear();
        _proposalId = proposalId;
        OnPropertyChanged(nameof(AutomationId));
        OnPropertyChanged(nameof(ParsimonyFoldAutomationId));
        _intentDigest = intentDigest;
        _trial = trial;
        return RefreshAsync(cancellationToken);
    }

    private Task RefreshAsync(CancellationToken token)
    {
        if (_disposed || !Context.Diagnostics || Context.ProjectPath is not { } path || _proposalId is null || _intentDigest is null)
            return Task.CompletedTask;
        IsVisible = true;
        if (Context.Evidence.IsStale)
        {
            AvailabilityText = MotifAppStrings.DiagnosticsOutOfDate;
            return Task.CompletedTask;
        }
        var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        var read = ReadAsync(path, _proposalId, _intentDigest, _trial, _generation, linked);
        _reads.RemoveAll(task => task.IsCompletedSuccessfully);
        _reads.Add(read);
        return read;
    }

    private async Task ReadAsync(string path, string proposalId, string digest, TrialSummary? summary,
        int generation, CancellationTokenSource linked)
    {
        try
        {
            var outcome = await Context.Commands.ReadProposalReviewAsync(new ProposalReviewRequest(path, proposalId), linked.Token)
                .ConfigureAwait(true);
            if (generation != _generation || linked.IsCancellationRequested || Context.ProjectPath != path ||
                !Context.Diagnostics || Context.Evidence.IsStale) return;
            if (!outcome.Succeeded)
            {
                AvailabilityText = MotifAppStrings.DiagnosticsUnreadable;
                return;
            }
            if (outcome.Value is not { } review || review.ProposalId != proposalId || review.IntentDigest != digest)
            {
                AvailabilityText = MotifAppStrings.DiagnosticsMismatched;
                return;
            }
            Operations = review.Entries.OfType<GrammarChangeEntry>().OrderBy(entry => entry.Ordinal)
                .Select(entry => string.IsNullOrWhiteSpace(entry.Summary) ? entry.Title : entry.Summary).ToArray();
            var trial = review.Trial;
            if (trial?.Freshness is TrialFreshness.BaselineMoved or TrialFreshness.ProposalChanged)
            {
                AvailabilityText = MotifAppStrings.DiagnosticsOutOfDate;
                return;
            }
            if (trial is not { Freshness: TrialFreshness.Current, Status: Contract.Jobs.JobStatus.Completed } ||
                trial.ProposalId != proposalId || trial.IntentDigest != digest || summary is null ||
                summary.JobId != trial.JobId || summary.Freshness != TrialFreshness.Current ||
                Context.Baseline?.Token is { } baseline && baseline != trial.Baseline) return;
            AvailabilityText = string.Empty;
            var warnings = summary.WarningCount;
            WarningsText = warnings is { HasBefore: true, HasAfter: true, Before: { } before, After: { } after,
                AfterRefusal: null }
                ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.DiagnosticsWarningsFormat,
                    before, after, (after - before).ToString("+0;-0;0", CultureInfo.CurrentCulture))
                : MotifAppStrings.DiagnosticsUnavailable;
            StepsText = trial.Work is { WordCount: > 0, BaselineWork: { } oldWork, TrialParserWork: { } newWork } work
                ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.DiagnosticsStepsFormat,
                    ((decimal)oldWork / work.WordCount).ToString("N2", CultureInfo.CurrentCulture),
                    ((decimal)newWork / work.WordCount).ToString("N2", CultureInfo.CurrentCulture), work.WordCount)
                : MotifAppStrings.DiagnosticsWorkUnavailable;
            Parsimony.AutomationScope = proposalId;
            Parsimony.SetTrial(summary);
            _watches.RemoveAll(task => task.IsCompletedSuccessfully);
            _watches.Add(Parsimony.WatchPinned(path, proposalId, digest, trial.JobId));
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        finally { linked.Dispose(); }
    }

    private void OnContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WorkspaceContext.Diagnostics)) return;
        Clear();
        if (Context.Diagnostics) _ = RefreshAsync(CancellationToken.None);
    }

    private void OnEvidenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectEvidence.IsStale) && Context.Evidence.IsStale)
        {
            Clear();
            AvailabilityText = MotifAppStrings.DiagnosticsOutOfDate;
            IsVisible = Context.Diagnostics && _proposalId is not null;
        }
    }

    private void Clear()
    {
        ++_generation;
        _stop.Cancel();
        _stop.Dispose();
        _stop = new CancellationTokenSource();
        Parsimony.Stop();
        IsVisible = false;
        AvailabilityText = MotifAppStrings.DiagnosticsTrialUnavailable;
        WarningsText = StepsText = string.Empty;
        Operations = [];
    }

    internal Task RefreshEvidenceAsync(CancellationToken cancellationToken)
    {
        Clear();
        return RefreshAsync(cancellationToken);
    }

    internal void ClearProject()
    {
        Clear();
        _proposalId = _intentDigest = null;
        _trial = null;
    }

    internal async Task StopWorkAsync()
    {
        Clear();
        await Task.WhenAll(_reads.Concat(_watches)).ConfigureAwait(true);
        _reads.Clear();
        _watches.Clear();
    }

    /// <summary>Stops this document's diagnostic reads when its owner closes it.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _owner.Unregister(this);
        Clear();
        _proposalId = _intentDigest = null;
        _trial = null;
        Context.PropertyChanged -= OnContextChanged;
        Context.Evidence.PropertyChanged -= OnEvidenceChanged;
    }
}
