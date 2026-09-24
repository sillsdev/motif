namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The Overview page's model: where the project stands, meaning its Baseline, its latest Assessment, its grammar
/// and its history, with a link from each summary to the page that shows it in full.
/// </summary>
public sealed class OverviewPageModel(WorkspaceContext context) : PageModel(context)
{
    public ProjectViewModel Project => Context.Project;

    public BaselineViewModel Baseline => Context.Baseline;

    public ProjectHistoryViewModel History => Context.ProjectHistory;

    public AssessViewModel Assess => Context.Assess;

    public GrammarViewModel Grammar => Context.Grammar;

    // The history lists this Assessment as soon as it is stored.
    protected override void OnEvidencePublished(WorkspaceEvidence evidence) => _ = History.LoadAsync();
}
