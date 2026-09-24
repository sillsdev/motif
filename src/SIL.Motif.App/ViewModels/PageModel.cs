using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// A page's own view model, built from a <see cref="WorkspaceContext"/> alone. It reacts to the project and
/// evidence the context publishes and to the <see cref="PageRequest"/>s addressed to it, and owns what its page
/// displays, including the count beside its sidebar label.
/// </summary>
public abstract partial class PageModel : ObservableObject
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

    /// <summary>Shows <paramref name="evidence"/>, just published.</summary>
    protected virtual void OnEvidencePublished(WorkspaceEvidence evidence)
    {
    }

    /// <summary>Answers <paramref name="request"/> when it is addressed to this page; ignores it otherwise.</summary>
    protected virtual void OnRequested(PageRequest request)
    {
    }

    internal void ProjectCleared() => OnProjectCleared();

    internal Task ProjectOpenedAsync(string projectPath, CancellationToken cancellationToken) =>
        OnProjectOpenedAsync(projectPath, cancellationToken);

    internal Task BaselineCapturedAsync(CancellationToken cancellationToken) => OnBaselineCapturedAsync(cancellationToken);

    internal void EvidencePublished(WorkspaceEvidence evidence) => OnEvidencePublished(evidence);

    internal void Requested(PageRequest request) => OnRequested(request);
}
