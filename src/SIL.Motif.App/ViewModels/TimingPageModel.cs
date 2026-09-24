namespace SIL.Motif.App.ViewModels;

/// <summary>Opens the Timing page on some words, filtered to one rule when one is named.</summary>
/// <param name="Words">The words whose time is in question.</param>
/// <param name="Rule">The grammar object to filter to, or <see langword="null"/> for every one.</param>
public sealed record OpenTimingRequest(IReadOnlyList<string> Words, string? Rule) : PageRequest(WorkspacePage.Timing);

/// <summary>The Timing page's model: where the latest Assessment's parse time went.</summary>
public sealed class TimingPageModel : PageModel
{
    public TimingPageModel(WorkspaceContext context) : base(context)
    {
        Statistics.AssessedWord = context.Assess.Words.Find;
        Statistics.TryWord = context.TryWord;
        Statistics.OpenTimeLimit = () => context.OpenTexts(TextsTab.Texts);
    }

    public StatisticsViewModel Statistics => Context.Statistics;

    /// <summary>What another page last opened Timing on, or <see langword="null"/> when none has.</summary>
    public OpenTimingRequest? Focus { get; private set; }

    protected override void OnProjectCleared()
    {
        Focus = null;
        Statistics.Reset();
    }

    protected override Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken)
    {
        Statistics.ProjectPath = projectPath;
        return Task.CompletedTask;
    }

    protected override void OnEvidencePublished(WorkspaceEvidence evidence)
    {
        Statistics.Reset();
        Statistics.SummaryMarkdown = evidence.Assessment.SummaryMarkdown;
        Statistics.AssessmentId = evidence.Assessment.Measurements
            .SingleOrDefault(measurement => measurement.Kind == "ObjectTiming")?.AssessmentId;
    }

    protected override void OnRequested(PageRequest request)
    {
        if (request is not OpenTimingRequest timing) return;
        Focus = timing;
        if (timing.Rule is not { } rule) return;
        Statistics.SelectedGroup = "object";
        Statistics.FilterText = rule;
    }
}
