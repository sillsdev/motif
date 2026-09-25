using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

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
        Statistics = new StatisticsViewModel(context.Commands);
        Statistics.AssessedWord = context.Assess.Words.Find;
        Statistics.TryWord = context.TryWord;
        Statistics.OpenTimeLimit = () => context.OpenTexts(TextsTab.Texts);
        LoadFocusedTimingCommand = new AsyncRelayCommand(LoadFocusedTimingAsync,
            () => Focus is not null && Context.ProjectPath is not null);
    }

    /// <summary>The page's own statistics, read through the context's commands.</summary>
    public StatisticsViewModel Statistics { get; }

    /// <summary>What another page last opened Timing on, or <see langword="null"/> when none has.</summary>
    public OpenTimingRequest? Focus { get; private set; }

    /// <summary>Stored timing for the words and rule another page opened.</summary>
    public TimingResponse? FocusedTiming { get; private set; }

    /// <summary>Why the requested stored timing could not be read.</summary>
    public string? FocusedTimingError { get; private set; }

    public bool HasFocus => Focus is not null;

    public bool ShowNoEvidence => Context.HasNoEvidence && Focus is null;

    public bool ShowStatistics => Context.HasEvidence && Focus is null;

    public string FocusSummary => Focus is not { } focus ? string.Empty :
        focus.Rule is null
            ? $"Timing for {(focus.Words.Count == 0 ? "all words" : $"{focus.Words.Count} selected words")}"
            : $"Timing for {(focus.Words.Count == 0 ? "all words" : $"{focus.Words.Count} selected words")} under {focus.Rule}";

    public bool HasFocusedTiming => FocusedTiming is not null;

    public bool HasFocusedTimingError => FocusedTimingError is not null;

    /// <summary>Refreshes the stored timing for the words and rule last opened.</summary>
    public IAsyncRelayCommand LoadFocusedTimingCommand { get; }

    protected override void OnProjectCleared()
    {
        Focus = null;
        FocusedTiming = null;
        FocusedTimingError = null;
        RaiseFocusState();
        Statistics.Reset();
    }

    protected override Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken)
    {
        Statistics.ProjectPath = projectPath;
        LoadFocusedTimingCommand.NotifyCanExecuteChanged();
        return Task.CompletedTask;
    }

    protected override void OnEvidencePublished(WorkspaceEvidence evidence)
    {
        Statistics.Reset();
        Statistics.SummaryMarkdown = evidence.Assessment.SummaryMarkdown;
        Statistics.AssessmentId = evidence.Assessment.Measurements
            .SingleOrDefault(measurement => measurement.Kind == "ObjectTiming")?.AssessmentId;
        OnPropertyChanged(nameof(ShowNoEvidence));
        OnPropertyChanged(nameof(ShowStatistics));
    }

    protected override void OnRequested(PageRequest request)
    {
        if (request is not OpenTimingRequest timing) return;
        Focus = timing with { Words = timing.Words.ToArray() };
        FocusedTiming = null;
        FocusedTimingError = null;
        RaiseFocusState();
        if (LoadFocusedTimingCommand.CanExecute(null)) _ = LoadFocusedTimingCommand.ExecuteAsync(null);
    }

    private async Task LoadFocusedTimingAsync()
    {
        if (Focus is not { } focus || Context.ProjectPath is not { } projectPath) return;
        var request = new TimingRequest(projectPath, Statistics.AssessmentId,
            By: focus.Rule is null ? "kind" : "rule", Rule: focus.Rule,
            ExplicitWords: focus.Words.Count > 0 ? focus.Words : null);
        var outcome = await Context.Commands.TimingAsync(request, CancellationToken.None).ConfigureAwait(true);
        if (!ReferenceEquals(Focus, focus) || !string.Equals(projectPath, Context.ProjectPath, StringComparison.Ordinal))
            return;
        FocusedTiming = outcome.Succeeded ? outcome.Value : null;
        FocusedTimingError = outcome.Succeeded ? null : outcome.Refusal?.Message;
        RaiseFocusState();
    }

    private void RaiseFocusState()
    {
        OnPropertyChanged(nameof(Focus));
        OnPropertyChanged(nameof(HasFocus));
        OnPropertyChanged(nameof(ShowNoEvidence));
        OnPropertyChanged(nameof(ShowStatistics));
        OnPropertyChanged(nameof(FocusSummary));
        OnPropertyChanged(nameof(FocusedTiming));
        OnPropertyChanged(nameof(HasFocusedTiming));
        OnPropertyChanged(nameof(FocusedTimingError));
        OnPropertyChanged(nameof(HasFocusedTimingError));
        LoadFocusedTimingCommand.NotifyCanExecuteChanged();
    }
}
