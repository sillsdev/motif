using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SIL.Motif.App.ViewModels;

/// <summary>Opens the Texts page on one of its tabs.</summary>
/// <param name="Tab">The tab to show.</param>
public sealed record OpenTextsRequest(TextsTab Tab) : PageRequest(WorkspacePage.Texts);

/// <summary>Opens one word in the Texts page's word list, with every filter cleared.</summary>
/// <param name="Word">The word to select.</param>
public sealed record OpenWordRequest(string Word) : PageRequest(WorkspacePage.Texts);

/// <summary>
/// The Texts page's model: which of its <see cref="TextsTab"/>s is showing, the word views it hosts, and the links
/// from those views to the other pages.
/// </summary>
public sealed partial class TextsPageModel : PageModel
{
    public TextsPageModel(WorkspaceContext context) : base(context)
    {
        Words = new TextWordsViewModel(context.Commands, context.Selection);
        Assess.TextWords = Words;
        ShowTabCommand = new RelayCommand<TextsTab>(tab => Tab = tab);
        ResultsInText = new ResultsInTextViewModel(Words, Assess, context.OpenWord, context.TryWord)
        {
            OpenTexts = () => context.OpenTexts(TextsTab.Texts),
        };
        Assess.Compare.Changes = context.Changes;
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

    /// <summary>Which view of the words the Texts page is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMatrix))]
    [NotifyPropertyChangedFor(nameof(ShowWhatChanged))]
    [NotifyPropertyChangedFor(nameof(ShowWords))]
    [NotifyPropertyChangedFor(nameof(ShowTexts))]
    [NotifyPropertyChangedFor(nameof(ShowInText))]
    private TextsTab _tab;

    public bool ShowMatrix => Tab == TextsTab.Matrix;

    public bool ShowWhatChanged => Tab == TextsTab.WhatChanged;

    public bool ShowWords => Tab == TextsTab.Words;

    public bool ShowTexts => Tab == TextsTab.Texts;

    public bool ShowInText => Tab == TextsTab.InText;

    /// <summary>Opens the tab passed as the command parameter.</summary>
    public IRelayCommand<TextsTab> ShowTabCommand { get; }

    /// <summary>Whether the word views have nothing to show and nothing to explain yet: no run, no refusal.</summary>
    public bool ShowEmptyResults => Context.HasNoEvidence && !Assess.IsActive && Assess.Refusal is null;

    protected override void OnProjectCleared() => Words.ShowAssessment(null);

    protected override Task OnProjectOpenedAsync(string projectPath, CancellationToken cancellationToken) =>
        Words.SetProjectAsync(projectPath, cancellationToken);

    protected override void OnEvidencePublished(WorkspaceEvidence evidence)
    {
        // A re-run exists to settle words, so what it settled is the first thing to see.
        if (evidence.WasRerun && Assess.Difference.HasDifference) Tab = TextsTab.WhatChanged;
        Words.ShowAssessment(Assess.Words.Find);
    }

    protected override void OnRequested(PageRequest request)
    {
        switch (request)
        {
            case OpenTextsRequest texts:
                Tab = texts.Tab;
                break;
            case OpenWordRequest word:
                Assess.SelectWord(word.Word);
                Tab = TextsTab.Words;
                break;
        }
    }

    private void OnAssessPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ShowEmptyResults));
        if (e.PropertyName == nameof(AssessViewModel.IsActive) && Assess.IsActive) Tab = TextsTab.Matrix;
    }

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceContext.Evidence)) OnPropertyChanged(nameof(ShowEmptyResults));
    }
}
