using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Commands.Queries;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The Overview page's model: where the project stands, meaning its Baseline, its latest Assessment, its grammar
/// and its history, with a link from each summary to the page that shows it in full. It owns the history and the
/// Assessment's word counts, reading both for itself from what the context publishes.
/// </summary>
public sealed class OverviewPageModel : PageModel
{
    public OverviewPageModel(WorkspaceContext context) : base(context)
    {
        History = new ProjectHistoryViewModel(context.Commands);
        context.PropertyChanged += OnContextPropertyChanged;
        context.KnownProjects.CollectionChanged += OnKnownProjectsChanged;
    }

    /// <summary>The project's Baselines and Assessments, newest first, which the page loads for itself.</summary>
    public ProjectHistoryViewModel History { get; }

    /// <summary>The published Assessment's words, counted for the page's summary.</summary>
    public AssessWordsViewModel Words { get; } = new();

    /// <summary>When the published Assessment completed, or <see langword="null"/> when that is not known.</summary>
    public DateTimeOffset? CompletedAt => Context.Evidence?.CompletedAt;

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
        Words.Load(null);
        OnPropertyChanged(nameof(CompletedAt));
    }

    protected override Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken) =>
        History.SetProjectAsync(projectPath, cancellationToken);

    protected override Task OnBaselineCapturedAsync(CancellationToken cancellationToken) =>
        History.LoadAsync(cancellationToken);

    protected override void OnEvidencePublished(WorkspaceEvidence evidence)
    {
        Words.Load(evidence.Assessment.Words);
        OnPropertyChanged(nameof(CompletedAt));
        // The history lists this Assessment as soon as it is stored.
        _ = History.LoadAsync();
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
        }
    }

    private void OnKnownProjectsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RaiseProjectChoice();

    private void RaiseProjectChoice()
    {
        OnPropertyChanged(nameof(SelectedKnownProject));
        OnPropertyChanged(nameof(OpenUnlistedPath));
    }
}
