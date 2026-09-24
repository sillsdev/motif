namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The Overview page's model: where the project stands, meaning its Baseline, its latest Assessment, its grammar
/// and its history, with a link from each summary to the page that shows it in full.
/// </summary>
public sealed class OverviewPageModel : PageModel
{
    public OverviewPageModel(WorkspaceContext context) : base(context) =>
        context.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceContext.GrammarSummary)) OnPropertyChanged(nameof(Grammar));
        };

    public ProjectViewModel Project => Context.Project;

    public BaselineViewModel Baseline => Context.Baseline;

    public ProjectHistoryViewModel History => Context.ProjectHistory;

    public AssessViewModel Assess => Context.Assess;

    /// <summary>The grammar check in one line, as the Warnings page last published it.</summary>
    public GrammarSummary? Grammar => Context.GrammarSummary;

    // The history lists this Assessment as soon as it is stored.
    protected override void OnEvidencePublished(WorkspaceEvidence evidence) => _ = History.LoadAsync();
}
