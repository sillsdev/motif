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
        Words.WordCardActions = ResultsInText;
        context.RegisterOccurrenceLocationProvider(ResultsInText.LocateOccurrence);
        context.RegisterOccurrenceNavigator(anchor =>
        {
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

    public AssessViewModel Assess => Context.Assess;
    public SelectionViewModel Selection => Context.Selection;

    /// <summary>The chosen Texts' words and lines.</summary>
    public TextWordsViewModel Words { get; }

    /// <summary>The current Text and its recorded word analyses.</summary>
    public ResultsInTextViewModel ResultsInText { get; }

    /// <summary>Whether a parse can be started before results exist.</summary>
    public bool ShowParsePrompt => Context.NeedsAssessment &&
        (Assess.Refusal is null or { Reason: FailureReason.Cancelled });

    public bool ShowAssessRefusal => Assess.Refusal is not null;

    protected override void OnProjectCleared()
    {
        ResultsInText.ClearProject();
        Words.ClearProject();
        Words.ShowAssessment(null);
    }

    protected override Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken) =>
        Words.SetProjectAsync(projectPath, cancellationToken);

    protected override Task OnStopWorkAsync() => Task.WhenAll(Words.StopAsync(), ResultsInText.StopAsync());

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
        if (request is not OpenWordRequest word) return;
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
