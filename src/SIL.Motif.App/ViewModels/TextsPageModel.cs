using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SIL.Motif.App.ViewModels;

/// <summary>Opens the Texts page on one of its tabs.</summary>
/// <param name="Tab">The tab to show.</param>
/// <param name="Cells">The matrix cells to select, or <see langword="null"/> to preserve the current selection.</param>
public sealed record OpenTextsRequest(TextsTab Tab, IReadOnlyList<TextsListCell>? Cells = null)
    : PageRequest(WorkspacePage.Texts);

/// <summary>Opens one word in the Texts page's word list, with every filter cleared.</summary>
/// <param name="Word">The word to select.</param>
public sealed record OpenWordRequest(string Word) : PageRequest(WorkspacePage.Texts);

/// <summary>The two views inside Analyze texts.</summary>
public enum AnalyzeTextsView
{
    TextReader,
    WordList,
}

/// <summary>
/// The Texts page's model: which of its <see cref="TextsTab"/>s is showing, the word views it hosts, and the links
/// from those views to the other pages.
/// </summary>
public sealed partial class TextsPageModel : PageModel
{
    public TextsPageModel(WorkspaceContext context) : base(context)
    {
        Words = new TextWordsViewModel(context.Commands, context.Selection);
        Words.OpenWord = context.OpenWord;
        Words.HandOff = context.HandOff;
        Assess.TextWords = Words;
        ShowTabCommand = new RelayCommand<TextsTab>(tab => Tab = tab);
        ShowAnalyzeViewCommand = new RelayCommand<AnalyzeTextsView>(view => AnalyzeView = view);
        ResultsInText = new ResultsInTextViewModel(Words, Assess, context.OpenWord, context.TryWord, context.Changes)
        {
            OpenTexts = () => context.OpenTexts(TextsTab.AnalyzeTexts),
        };
        context.RegisterOccurrenceContextProvider(anchor => ResultsInText.FindOccurrenceLine(anchor)?.Tokens);
        Assess.Compare.Changes = context.Changes;
        TextsLists = new TextsListsViewModel(Assess.Compare);
        TextsLists.HandOff = context.HandOff;
        Assess.Compare.ChosenCellsChanged += OnChosenCellsChanged;
        Assess.Compare.OpenWord = context.OpenWord;
        Assess.Difference.OpenWord = context.OpenWord;
        Assess.Compare.HandOff = context.HandOff;
        Assess.OpenTryWord = context.TryWord;
        Assess.PropertyChanged += OnAssessPropertyChanged;
        context.PropertyChanged += OnContextPropertyChanged;
    }

    public AssessViewModel Assess => Context.Assess;

    public SelectionViewModel Selection => Context.Selection;

    /// <summary>The chosen Texts' words and lines, which the page loads for itself.</summary>
    public TextWordsViewModel Words { get; }

    /// <summary>The changes not applied yet, which the page's header offers to review.</summary>
    public ChangesViewModel Changes => Context.Changes;

    /// <summary>The In text view: the chosen Texts, each occurrence against the Assessment.</summary>
    public ResultsInTextViewModel ResultsInText { get; }

    /// <summary>The fixed word questions, which select their exact cells in the page's Compare matrix.</summary>
    public TextsListsViewModel TextsLists { get; }

    /// <summary>Which view of the words the Texts page is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMatrix))]
    [NotifyPropertyChangedFor(nameof(ShowAnalyzeTexts))]
    [NotifyPropertyChangedFor(nameof(ShowLists))]
    [NotifyPropertyChangedFor(nameof(ShowWhatChanged))]
    private TextsTab _tab;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTextReader))]
    [NotifyPropertyChangedFor(nameof(ShowWordList))]
    private AnalyzeTextsView _analyzeView = AnalyzeTextsView.TextReader;

    public bool ShowMatrix => Tab == TextsTab.Matrix;

    public bool ShowMatrixContent => ShowMatrix && !Context.NeedsAssessment;

    public bool ShowAnalyzeTexts => Tab == TextsTab.AnalyzeTexts;

    public bool ShowAnalyzeTextsContent => ShowAnalyzeTexts && !Context.NeedsAssessment;

    public bool ShowParsePrompt => Context.NeedsAssessment && (ShowMatrix || ShowAnalyzeTexts);

    public bool ShowLists => Tab == TextsTab.Lists;

    public bool ShowWhatChanged => Tab == TextsTab.WhatChanged;

    public bool ShowTextReader => AnalyzeView == AnalyzeTextsView.TextReader;

    public bool ShowWordList => AnalyzeView == AnalyzeTextsView.WordList;

    public bool ShowAssessStatus => Assess.IsActive || Assess.Refusal is not null;

    public bool ShowAssessRefusal => Assess.Refusal is not null;

    /// <summary>Opens the tab passed as the command parameter.</summary>
    public IRelayCommand<TextsTab> ShowTabCommand { get; }

    /// <summary>Opens the text reader or the word list inside Analyze texts.</summary>
    public IRelayCommand<AnalyzeTextsView> ShowAnalyzeViewCommand { get; }

    /// <summary>Whether the word views have nothing to show and nothing to explain yet: no run, no refusal.</summary>
    public bool ShowEmptyResults => Context.HasNoEvidence && !Assess.IsActive && Assess.Refusal is null;

    protected override void OnProjectCleared()
    {
        ResultsInText.ClearProject();
        Words.ClearProject();
        Words.ShowAssessment(null);
    }

    protected override Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken) =>
        Words.SetProjectAsync(projectPath, cancellationToken);

    protected override Task OnStopWorkAsync() => Words.StopAsync();

    protected override Task OnEvidencePublishedAsync(ProjectEvidence evidence, CancellationToken cancellationToken)
    {
        if (evidence.Assessment is not { } shown) return Task.CompletedTask;
        Words.ShowAssessment(Assess.Words.Find);
        if (shown.WasRerun && Assess.Difference.HasDifference) Tab = TextsTab.WhatChanged;
        return Task.CompletedTask;
    }

    protected override void OnRequested(PageRequest request)
    {
        switch (request)
        {
            case OpenTextsRequest texts:
                Tab = texts.Tab;
                if (texts.Cells is { } cells) Assess.Compare.SelectCells(cells);
                break;
            case OpenWordRequest word:
                Assess.SelectWord(word.Word);
                ResultsInText.SelectWord(word.Word);
                AnalyzeView = AnalyzeTextsView.TextReader;
                Tab = TextsTab.AnalyzeTexts;
                break;
        }
    }

    private void OnAssessPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ShowEmptyResults));
        OnPropertyChanged(nameof(ShowParsePrompt));
        OnPropertyChanged(nameof(ShowAssessStatus));
        OnPropertyChanged(nameof(ShowAssessRefusal));
        if (e.PropertyName == nameof(AssessViewModel.IsActive) && Assess.IsActive) Tab = TextsTab.Matrix;
    }

    partial void OnTabChanged(TextsTab value)
    {
        OnPropertyChanged(nameof(ShowMatrixContent));
        OnPropertyChanged(nameof(ShowAnalyzeTextsContent));
        OnPropertyChanged(nameof(ShowParsePrompt));
        if (value == TextsTab.Lists) TextsLists.SelectFirstIfNeeded();
    }

    private void OnChosenCellsChanged(object? sender, EventArgs e)
    {
        if (Tab == TextsTab.Lists) TextsLists.SelectFirstIfNeeded();
    }

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceContext.NeedsAssessment))
        {
            OnPropertyChanged(nameof(ShowMatrixContent));
            OnPropertyChanged(nameof(ShowAnalyzeTextsContent));
            OnPropertyChanged(nameof(ShowParsePrompt));
        }
    }
}
