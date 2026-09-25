using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The Overview page's model: where the project stands, meaning its Baseline, its latest Assessment, its grammar
/// and its history, with a link from each summary to the page that shows it in full. It owns the history and the
/// Assessment's word counts, reading both for itself from what the context publishes.
/// </summary>
public sealed partial class OverviewPageModel : PageModel
{
    private int _readGeneration;

    public OverviewPageModel(WorkspaceContext context) : base(context)
    {
        History = new ProjectHistoryViewModel(context.Commands);
        context.PropertyChanged += OnContextPropertyChanged;
        context.KnownProjects.CollectionChanged += OnKnownProjectsChanged;
    }

    /// <summary>The project's Baselines and Assessments, newest first, which the page loads for itself.</summary>
    public ProjectHistoryViewModel History { get; }

    /// <summary>The stored Overview read for the open project.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverview))]
    [NotifyPropertyChangedFor(nameof(OverviewSummary))]
    [NotifyPropertyChangedFor(nameof(OverviewIsStale))]
    [NotifyPropertyChangedFor(nameof(CompletedAt))]
    [NotifyPropertyChangedFor(nameof(HasAssessment))]
    [NotifyPropertyChangedFor(nameof(ShowNoAssessment))]
    private OverviewResponse? _overview;

    /// <summary>Why the stored Overview query was refused.</summary>
    [ObservableProperty]
    private string? _overviewRefusalMessage;

    /// <summary>Whether the project returned a stored Overview.</summary>
    public bool HasOverview => Overview?.AssessmentId is not null;

    /// <summary>Whether this project has a stored Assessment or a run completed in this window.</summary>
    public bool HasAssessment => Context.HasEvidence || HasOverview;

    /// <summary>Whether the Overview should explain that this project has no Assessment.</summary>
    public bool ShowNoAssessment => !HasAssessment;

    /// <summary>Whether the live FieldWorks file is newer than the Baseline behind this Overview.</summary>
    public bool OverviewIsStale => Overview?.IsStale == true;

    /// <summary>The Overview's stored counts in one line.</summary>
    public string OverviewSummary => Overview is not { } overview ? string.Empty :
        $"{overview.TextCoverage.ParsedWords:N0} of {overview.SelectionWordCount:N0} Selection words parsed · " +
        $"{overview.Accuracy.ApprovedWordsKept:N0} of {overview.Accuracy.ApprovedWordCount:N0} approved kept · " +
        (overview.Timing.MedianMs is { } median ? $"median {median:N1} ms" : "no timing recorded");

    /// <summary>The published Assessment's words, counted for the page's summary.</summary>
    public AssessWordsViewModel Words { get; } = new();

    /// <summary>When the published Assessment completed, or <see langword="null"/> when that is not known.</summary>
    public DateTimeOffset? CompletedAt => Context.Evidence?.CompletedAt ?? Overview?.AssessedUtc;

    /// <summary>The open project's Baseline, as the shell last published it.</summary>
    public WorkspaceBaseline? Baseline => Context.Baseline;

    /// <summary>The grammar check in one line, as the Warnings page last published it.</summary>
    public GrammarSummary? Grammar => Context.GrammarSummary;

    /// <summary>The machine's Known projects, for the project picker.</summary>
    public ObservableCollection<KnownProjectSummary> KnownProjects => Context.KnownProjects;

    /// <summary>The listed project that is open; choosing another opens it.</summary>
    public KnownProjectSummary? SelectedKnownProject
    {
        get => KnownProjects.FirstOrDefault(known => IsOpen(known.FullFwDataPath));
        set
        {
            if (value is not null && !IsOpen(value.FullFwDataPath))
                Context.OpenProjectCommand?.Execute(value.FullFwDataPath);
        }
    }

    /// <summary>The open project's path when the Known projects do not list it, so the picker can still name it.</summary>
    public string? OpenUnlistedPath =>
        Context.ProjectPath is { } path && SelectedKnownProject is null ? path : null;

    /// <summary>Browses for a <c>.fwdata</c> file and opens it.</summary>
    public IAsyncRelayCommand? BrowseCommand => Context.BrowseForProjectCommand;

    /// <summary>Captures a new Baseline from FieldWorks' last save.</summary>
    public IAsyncRelayCommand? RefreshBaselineCommand => Context.RefreshBaselineCommand;

    protected override void OnProjectCleared()
    {
        _readGeneration++;
        Overview = null;
        OverviewRefusalMessage = null;
        Words.Load(null);
        OnPropertyChanged(nameof(CompletedAt));
    }

    protected override async Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken)
    {
        await History.SetProjectAsync(projectPath, cancellationToken).ConfigureAwait(true);
        await RefreshReadModelAsync(projectPath, cancellationToken).ConfigureAwait(true);
    }

    protected override Task OnBaselineCapturedAsync(CancellationToken cancellationToken) =>
        History.LoadAsync(cancellationToken);

    protected override void OnEvidencePublished(WorkspaceEvidence evidence)
    {
        Words.Load(evidence.Assessment.Words);
        OnPropertyChanged(nameof(CompletedAt));
        // The history lists this Assessment as soon as it is stored.
        _ = History.LoadAsync();
        if (Context.ProjectPath is { } path) _ = RefreshReadModelAsync(path, CancellationToken.None);
    }

    private async Task RefreshReadModelAsync(string projectPath, CancellationToken cancellationToken)
    {
        var generation = ++_readGeneration;
        var overviewTask = Context.Commands.OverviewAsync(new OverviewRequest(projectPath), cancellationToken);
        var evidenceTask = Context.Commands.ReadCurrentEvidenceAsync(projectPath, cancellationToken);
        await Task.WhenAll(overviewTask, evidenceTask).ConfigureAwait(true);
        if (generation != _readGeneration || !string.Equals(projectPath, Context.ProjectPath, StringComparison.Ordinal))
            return;

        var overview = await overviewTask.ConfigureAwait(true);
        Overview = overview.Succeeded ? overview.Value : null;
        OverviewRefusalMessage = overview.Succeeded ? null : overview.Refusal?.Message;

        var evidence = await evidenceTask.ConfigureAwait(true);
        if (evidence.Succeeded)
            await Context.PublishCurrentEvidenceAsync(evidence.Value!, cancellationToken).ConfigureAwait(true);
    }

    private bool IsOpen(string path) => string.Equals(path, Context.ProjectPath, StringComparison.OrdinalIgnoreCase);

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(WorkspaceContext.Baseline):
                OnPropertyChanged(nameof(Baseline));
                break;
            case nameof(WorkspaceContext.GrammarSummary):
                OnPropertyChanged(nameof(Grammar));
                break;
            case nameof(WorkspaceContext.ProjectPath):
                RaiseProjectChoice();
                break;
            case nameof(WorkspaceContext.Evidence):
                OnPropertyChanged(nameof(CompletedAt));
                OnPropertyChanged(nameof(HasAssessment));
                OnPropertyChanged(nameof(ShowNoAssessment));
                break;
        }
    }

    private void OnKnownProjectsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RaiseProjectChoice();

    private void RaiseProjectChoice()
    {
        OnPropertyChanged(nameof(SelectedKnownProject));
        OnPropertyChanged(nameof(OpenUnlistedPath));
    }
}
