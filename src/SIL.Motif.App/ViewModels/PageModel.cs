using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// A page's own view model, built from a <see cref="WorkspaceContext"/> alone. It reacts to the project and
/// evidence the context publishes and to the <see cref="PageRequest"/>s addressed to it, and owns what its page
/// displays, including the count beside its sidebar label.
/// </summary>
public abstract partial class PageModel : ObservableObject, IProjectStateParticipant
{
    protected PageModel(WorkspaceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
        ShowPageCommand = new RelayCommand<WorkspacePage>(context.OpenPage);
        context.Attach(this);
    }

    /// <summary>What the page is built from.</summary>
    public WorkspaceContext Context { get; }

    /// <summary>Opens the page passed as the command parameter.</summary>
    public IRelayCommand<WorkspacePage> ShowPageCommand { get; }

    /// <summary>A short count beside the sidebar label, or empty when the page has nothing to count.</summary>
    [ObservableProperty]
    private string _badge = string.Empty;

    /// <summary>Drops whatever the page showed for the previous project.</summary>
    protected virtual void OnProjectCleared()
    {
    }

    /// <summary>Loads what the page shows for the project at <paramref name="projectPath"/>, now open.</summary>
    protected virtual Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    /// <summary>Reloads what the page shows now that a new Baseline has been captured.</summary>
    protected virtual Task OnBaselineCapturedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Stops whatever work the page has running, and returns once it has stopped.</summary>
    protected virtual Task OnStopWorkAsync() => Task.CompletedTask;

    /// <summary>
    /// Shows <paramref name="evidence"/>, just published because a run completed or the stored evidence was read.
    /// A burst of publications reaches a page as one more call with the latest evidence, never one per arrival.
    /// </summary>
    protected virtual Task OnEvidencePublishedAsync(ProjectEvidence evidence, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    /// <summary>Reloads this page's stored summary after a person checked the grammar.</summary>
    protected virtual Task OnGrammarCheckedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Answers <paramref name="request"/> when it is addressed to this page; ignores it otherwise.</summary>
    protected virtual void OnRequested(PageRequest request)
    {
    }

    internal void ProjectCleared() => OnProjectCleared();

    internal Task ProjectOpenedAsync(string projectPath, CancellationToken cancellationToken) =>
        OnProjectOpenedAsync(projectPath, cancellationToken);

    internal Task BaselineCapturedAsync(CancellationToken cancellationToken) => OnBaselineCapturedAsync(cancellationToken);

    internal Task StopWorkAsync() => OnStopWorkAsync();

    internal Task EvidencePublishedAsync(ProjectEvidence evidence, CancellationToken cancellationToken) =>
        OnEvidencePublishedAsync(evidence, cancellationToken);

    internal Task GrammarCheckedAsync(CancellationToken cancellationToken) => OnGrammarCheckedAsync(cancellationToken);

    internal void Requested(PageRequest request) => OnRequested(request);

    void IProjectStateParticipant.ClearProject() => ProjectCleared();

    ProjectOpenStage IProjectStateParticipant.OpenStage => ProjectOpenStage.Independent;

    Task IProjectStateParticipant.OpenProjectAsync(string projectPath, CancellationToken cancellationToken) =>
        ProjectOpenedAsync(projectPath, cancellationToken);
}
