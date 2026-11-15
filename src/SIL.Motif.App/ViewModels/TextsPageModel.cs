using System.ComponentModel;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Opens the Texts page on its reader.</summary>
public sealed record OpenTextsRequest() : PageRequest(WorkspacePage.Texts);

/// <summary>Opens one word in the Texts reader.</summary>
/// <param name="Word">The word to select.</param>
/// <param name="WordformId">The exact wordform to select, or null for spelling-only navigation.</param>
public sealed record OpenWordRequest(string Word, string? WordformId = null) : PageRequest(WorkspacePage.Texts);

/// <summary>The current Selection and its Text reader.</summary>
public sealed class TextsPageModel : PageModel
{
    public TextsPageModel(WorkspaceContext context) : base(context)
    {
        Sources = new SourcesViewModel(context.Commands, context.Selection, context.Changes.ReloadAsync,
            () => context.Changes.TargetProposalId is null, context.LeaveProposalAsync);
        Sources.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(SourcesViewModel.CanReadPassage)) return;
            OnPropertyChanged(nameof(ShowParsePrompt));
            OnPropertyChanged(nameof(ShowAssessRefusal));
        };
        context.Changes.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ChangesViewModel.TargetProposalId)) Sources.RefreshStagingTarget();
        };
        Words = context.GetOrCreateTextWords();
        ResultsInText = new ResultsInTextViewModel(Words, Assess, context.OpenWord, context.Changes,
            context.Commands, context.SelectionReads)
        {
            OpenTexts = () =>
            {
                if (context.ConfigureCommand is { } configure && configure.CanExecute(null)) configure.Execute(null);
            },
            OpenWarnings = () => context.OpenDiagnostics(DiagnosticsTab.Warnings),
        };
        ResultsInText.AskAboutWord = (form, _) =>
            AskAssistantAction.RunAsync(context, new AskAssistantTarget("word", form, form, null));
        Words.WordCardActions = ResultsInText;
        WhyThisAnalysis = new Views.Diagnostics.WhyThisAnalysisHost(context, ResultsInText);
        context.RegisterOccurrenceLocationProvider(ResultsInText.LocateOccurrence);
        context.RegisterOccurrenceNavigator(anchor =>
        {
            Sources.Navigate(new(null));
            ResultsInText.ShowReader();
            if (!ResultsInText.SelectOccurrenceText(anchor)) return false;
            ResultsInText.NavigateOccurrence(anchor);
            return true;
        });
        Assess.PropertyChanged += OnAssessPropertyChanged;
        context.PropertyChanged += OnContextPropertyChanged;
        context.SelectionReads.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(WorkspaceSelection.ListCounts) && context.EvidenceLens.Today.Assessment is not null)
                UpdateListedOccurrences();
        };
    }

    public SourcesViewModel Sources { get; }

    /// <summary>Fills the word panel's diagnostic slot while Diagnostics is on.</summary>
    internal Views.Diagnostics.WhyThisAnalysisHost WhyThisAnalysis { get; }

    public AssessViewModel Assess => Context.Assess;
    public SelectionViewModel Selection => Context.Selection;

    /// <summary>The chosen Texts' words and lines.</summary>
    public TextWordsViewModel Words { get; }

    /// <summary>The current Text and its recorded word analyses.</summary>
    public ResultsInTextViewModel ResultsInText { get; }

    /// <summary>Whether a parse can be started before results exist.</summary>
    public bool ShowParsePrompt => Sources.CanReadPassage && Context.NeedsAssessment &&
        (Assess.Refusal is null or { Reason: FailureReason.Cancelled });

    public bool ShowAssessRefusal => Sources.CanReadPassage && Assess.Refusal is not null;

    protected override void OnProjectCleared()
    {
        Sources.Clear();
        ResultsInText.ClearProject();
        Words.ClearProject();
        Words.ShowAssessment(null);
    }

    protected override Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken) =>
        Task.WhenAll(Words.SetProjectAsync(projectPath, cancellationToken), Sources.LoadAsync(projectPath, cancellationToken));

    protected override Task OnBaselineCapturedAsync(CancellationToken cancellationToken) =>
        Context.ProjectPath is { } project ? Sources.LoadAsync(project, cancellationToken) : Task.CompletedTask;

    protected override Task OnStopWorkAsync() => Task.WhenAll(Words.StopAsync(), ResultsInText.StopAsync(), Sources.StopAsync(), WhyThisAnalysis.StopAsync());

    protected override Task OnEvidencePublishedAsync(EvidenceLens lens, CancellationToken cancellationToken)
    {
        if (lens.Today.Assessment is not null)
        {
            UpdateListedOccurrences();
            Words.ShowAssessmentSource(Assess.Words.SourceOf);
        }
        return Task.CompletedTask;
    }

    private void UpdateListedOccurrences() =>
        Assess.Words.UpdateOccurrences(Words.OccurrenceCountOf, Words.PlacesAreFromWordList);

    protected override void OnRequested(PageRequest request)
    {
        if (request is OpenPassageRequest passage)
        {
            Sources.Navigate(passage.Choice);
            if (passage.Choice.SourceId is not null)
            {
                if (Sources.SelectedSource is null) Sources.Notice = MotifAppStrings.SourcesNoLongerAvailable;
                return;
            }
            ResultsInText.ShowReader();
            if (passage.Choice.Occurrence is { } anchor && ResultsInText.SelectOccurrenceText(anchor))
                ResultsInText.NavigateOccurrence(anchor);
            else if (passage.Choice.Occurrence is not null) Sources.RejectFieldWorksPassage();
            return;
        }
        if (request is OpenTextsRequest) { Sources.Navigate(new(null)); return; }
        if (request is not OpenWordRequest word) return;
        Sources.Navigate(new(null));
        ResultsInText.ShowReader();
        Assess.SelectWord(word.Word);
        ResultsInText.SelectWord(word.Word, word.WordformId);
    }

    private void OnAssessPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ShowParsePrompt));
        OnPropertyChanged(nameof(ShowAssessRefusal));
    }

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceContext.NeedsAssessment)) OnPropertyChanged(nameof(ShowParsePrompt));
    }
}
