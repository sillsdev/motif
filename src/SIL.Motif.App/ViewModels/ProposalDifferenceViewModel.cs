using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Where the Proposal inspector's Difference section is.</summary>
public enum ProposalDifferenceState
{
    /// <summary>No Proposal is selected, so the section is not shown.</summary>
    None,

    /// <summary>The saved Difference is being read.</summary>
    Loading,

    /// <summary>The Proposal has no saved Difference; the section offers to make one.</summary>
    Absent,

    /// <summary>The Difference is shown.</summary>
    Shown,

    /// <summary>The saved Difference could not be read.</summary>
    Refused,
}

/// <summary>
/// The saved Difference of the selected Proposal: its summary, the chips that filter its words, a page of words, and
/// the Text places of one chosen word. It reads only the shared bounded query and never classifies live state.
/// </summary>
/// <remarks>
/// Once the Proposal is decided or deferred only the summary is left (<see cref="IsSummaryOnly"/>): the chips show their counts
/// but do not filter, and the word list and places give way to a note saying the word detail was removed.
/// A person makes the Difference with <see cref="Maker"/>, from the same command the CLI and MCP use; the section
/// offers that with or without a saved Difference, and the window never applies anything because of it.
/// </remarks>
public sealed partial class ProposalDifferenceViewModel : ObservableObject
{
    /// <summary>How many words, and how many places of a word, one read returns.</summary>
    public const int PageSize = 25;

    /// <summary>
    /// The most words, and the most places of one word, the section lists; past that it asks for a narrower filter,
    /// so what it builds stays small however many words a Difference holds.
    /// </summary>
    public const int MostShown = 100;

    private readonly Func<ProposalDifferenceShowRequest, CancellationToken,
        Task<CommandOutcome<ProposalDifferenceResponse>>> _read;
    private readonly Action<string> _openWord;
    private readonly Func<IReadOnlyList<DifferenceTextChoice>> _textSource;
    private DifferenceOffer _offer;
    private int _generation;
    private int _wordsGeneration;
    private int _placesGeneration;
    private string? _projectPath;
    private string? _proposalId;
    private string? _differenceId;
    private bool _checkOnly;
    private string? _bucket;
    private string? _limitation;
    private string? _beforeState;
    private string? _afterState;
    private int? _nextWordOffset;
    private int? _nextPlaceOffset;
    private int _placesTotal;
    private int _placesListed;
    private int _reads;
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _wordsCancellation;
    private CancellationTokenSource? _placesCancellation;

    /// <summary>Creates the section's model.</summary>
    /// <param name="read">Reads the saved Difference through the shared bounded query.</param>
    /// <param name="openWord">Opens a word on the Texts page.</param>
    /// <param name="produce">Makes the Difference; without it nothing is offered to make.</param>
    /// <param name="texts">The Texts a Difference can compare, as the Baseline lists them now.</param>
    public ProposalDifferenceViewModel(
        Func<ProposalDifferenceShowRequest, CancellationToken, Task<CommandOutcome<ProposalDifferenceResponse>>> read,
        Action<string> openWord,
        Func<ProposalDifferenceProduceRequest, IProgress<AssessmentProgress>, CancellationToken,
            Task<CommandOutcome<ProposalDifferenceResponse>>>? produce = null,
        Func<IReadOnlyList<DifferenceTextChoice>>? texts = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(openWord);
        _read = read;
        _openWord = openWord;
        _textSource = texts ?? (() => []);
        Maker = new ProposalDifferenceMaker(produce ?? NotAvailable, Describe, AdoptMade);
        Maker.PropertyChanged += OnMakerChanged;
        ClearFiltersCommand = new AsyncRelayCommand(ClearFiltersAsync);
        ShowMoreWordsCommand = new AsyncRelayCommand(ShowMoreWordsAsync);
        ShowMorePlacesCommand = new AsyncRelayCommand(ShowMorePlacesAsync);
        OpenWordCommand = new RelayCommand(OpenWord);
    }

    /// <summary>Where the section is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible), nameof(IsLoading), nameof(IsShown), nameof(IsRefused),
        nameof(IsReadingMore), nameof(MakeLabel))]
    private ProposalDifferenceState _state;

    /// <summary>Why the Difference or a page of it could not be read, in the window's words.</summary>
    [ObservableProperty]
    private WindowRefusal? _shownRefusal;

    /// <summary>The saved Difference's totals.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeadlineText), nameof(ComparedText), nameof(UnboundText), nameof(HasUnbound),
        nameof(HasListedNote), nameof(ListedNoteText),
        nameof(OutsideText), nameof(HasOutside), nameof(HasNotes), nameof(Notes), nameof(AdviceText),
        nameof(ReferenceNoteText), nameof(HasReferenceNote), nameof(UnreadableEvidenceText), nameof(HasUnreadableEvidence),
        nameof(BasisText))]
    private ProposalDifferenceSummary? _summary;

    /// <summary>The Applied Proposals whose staged decisions could not be read at the last read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnreadableEvidenceText), nameof(HasUnreadableEvidence))]
    private IReadOnlyList<ProposalDifferenceUnreadableEvidence> _unreadableEvidence = [];

    /// <summary>Whether the word detail is still kept; false once the Proposal is decided or deferred.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSummaryOnly), nameof(ShowsWordList), nameof(ShowsDetailRemoved),
        nameof(ShowsDetailUnreadable), nameof(ShowsRemakeHint), nameof(HasListedNote), nameof(ListedNoteText))]
    private bool _wordDetailKept = true;

    /// <summary>Whether the summary reads but the word rows are damaged, so the words cannot be shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsDetailRemoved), nameof(ShowsDetailUnreadable), nameof(ShowsRemakeHint))]
    private bool _wordDetailUnreadable;

    partial void OnStateChanged(ProposalDifferenceState value) => RaiseMake();

    partial void OnWordDetailKeptChanged(bool value) => RaiseMake();

    /// <summary>Every before and after pair with its word count.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMoved))]
    private IReadOnlyList<DifferenceChipViewModel> _movedChips = [];

    /// <summary>The categories that mean a word may have got worse, with the Words to Check chip first.</summary>
    [ObservableProperty]
    private IReadOnlyList<DifferenceChipViewModel> _checkChips = [];

    /// <summary>The categories that mean a word improved.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImproved))]
    private IReadOnlyList<DifferenceChipViewModel> _improvedChips = [];

    /// <summary>The search limits that touched words.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLimitations))]
    private IReadOnlyList<DifferenceChipViewModel> _limitationChips = [];

    /// <summary>The page of words read so far, one chip each.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WordsStatusText), nameof(ShowsNoMatchingWords), nameof(ShowsNarrowHint))]
    private IReadOnlyList<DifferenceChipViewModel> _words = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WordsStatusText), nameof(ShowsNoMatchingWords), nameof(ShowsNarrowHint))]
    private int _matchingWords;

    /// <summary>Whether a chip filters the words now.</summary>
    [ObservableProperty]
    private bool _hasFilter;

    /// <summary>The word whose places are shown, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedWord), nameof(SelectedWordTitle), nameof(SelectedWordMovedText),
        nameof(SelectedWordBucketsText), nameof(SelectedWordEvidenceText), nameof(HasSelectedWordEvidence))]
    private ProposalDifferenceWord? _selectedWord;

    /// <summary>The selected word's places in the Text, a page at a time.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsNoPlaces), nameof(PlacesStatusText), nameof(ShowsPlacesStatus))]
    private IReadOnlyList<string> _places = [];

    /// <summary>Whether more words are waiting.</summary>
    [ObservableProperty]
    private bool _canShowMoreWords;

    /// <summary>Whether more places of the selected word are waiting.</summary>
    [ObservableProperty]
    private bool _canShowMorePlaces;

    public bool IsVisible => State != ProposalDifferenceState.None;

    public bool IsLoading => State == ProposalDifferenceState.Loading;

    public bool IsShown => State == ProposalDifferenceState.Shown;

    public bool IsRefused => State == ProposalDifferenceState.Refused;

    /// <summary>Whether a later read, such as a filter's words or a word's places, is still running.</summary>
    public bool IsReadingMore => _reads > 0 && State == ProposalDifferenceState.Shown;

    /// <summary>Whether only the summary is left because the Proposal was decided or deferred.</summary>
    public bool IsSummaryOnly => !WordDetailKept;

    public bool ShowsWordList => WordDetailKept;

    /// <summary>Whether the note that the words were removed with the decision is shown.</summary>
    public bool ShowsDetailRemoved => IsSummaryOnly && !WordDetailUnreadable;

    /// <summary>Whether the note that the words are damaged is shown.</summary>
    public bool ShowsDetailUnreadable => IsSummaryOnly && WordDetailUnreadable;

    /// <summary>Whether some compared words are counted and not listed.</summary>
    public bool HasListedNote => WordDetailKept && Summary is { } summary && summary.ListedWordCount < summary.Words;

    /// <summary>"40 of 900 words are listed; 860 words are counted but not listed, because …".</summary>
    public string ListedNoteText => HasListedNote
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalDifferenceListedNoteFormat,
            Summary!.ListedWordCount, Summary.Words,
            PluralText.Format(MotifAppStrings.ProposalDifferenceNotListedPattern, Summary.Words - Summary.ListedWordCount),
            ProposalDifferenceListing.MostWordsPerGroup, ProposalDifferenceListing.MostPlacesPerWord)
        : string.Empty;

    public bool HasSelectedWord => SelectedWord is not null;

    public bool ShowsNoPlaces => Places.Count == 0;

    public bool ShowsNoMatchingWords => MatchingWords == 0;

    /// <summary>Whether the list stopped at <see cref="MostShown"/> words and a narrower filter is the way on.</summary>
    public bool ShowsNarrowHint => Words.Count >= MostShown && MatchingWords > Words.Count;

    /// <summary>Whether the selected word has more places than are listed.</summary>
    public bool ShowsPlacesStatus => _placesTotal > Places.Count;

    /// <summary>"Showing 25 of 40." for the selected word's places, or what is counted but not listed.</summary>
    public string PlacesStatusText => _placesListed < _placesTotal
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalDifferencePlacesNotListedFormat,
            Places.Count, _placesListed, PluralText.Format(MotifAppStrings.ProposalDifferencePlacesMorePattern,
                _placesTotal - _placesListed), ProposalDifferenceListing.MostPlacesPerWord)
        : string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalDifferenceShowingFormat, Places.Count, _placesTotal);

    public bool HasMoved => MovedChips.Count > 0;

    public bool HasImproved => ImprovedChips.Count > 0;

    public bool HasLimitations => LimitationChips.Count > 0;

    public bool HasUnbound => Summary is { UnboundOccurrences: > 0 };

    public bool HasOutside => Summary is { MeasuredWordsOutsideText: > 0 };

    public bool HasNotes => Notes.Count > 0;

    /// <summary>The intro line, true for the decisions this Difference was read against.</summary>
    public string AdviceText => ProposalDifferenceWords.Advice(Summary);

    /// <summary>Says at the top when the decisions are a named program's, not a person's.</summary>
    public string ReferenceNoteText => ProposalDifferenceWords.ReferenceNote(Summary);

    // A run's reference is the store's to pick, so neither line speaks until the run ends.
    public bool HasReferenceNote => !Maker.IsActive && ReferenceNoteText.Length > 0;

    /// <summary>Whether the intro line shows; it waits while a Difference is being made.</summary>
    public bool HasAdviceText => !Maker.IsActive;

    private void OnMakerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ProposalDifferenceMaker.IsActive)) return;
        OnPropertyChanged(nameof(HasAdviceText));
        OnPropertyChanged(nameof(HasReferenceNote));
    }

    /// <summary>Names the Applied Proposals whose staged decisions could not be read.</summary>
    public string UnreadableEvidenceText => ProposalDifferenceWords.Unreadable(UnreadableEvidence, Summary?.Warnings);

    public bool HasUnreadableEvidence => UnreadableEvidenceText.Length > 0;

    /// <summary>Why the selected word's decisions were staged, when a named program staged them.</summary>
    public string SelectedWordEvidenceText => ProposalDifferenceWords.Evidence(SelectedWord?.Evidence);

    public bool HasSelectedWordEvidence => SelectedWordEvidenceText.Length > 0;

    /// <summary>The summary's notes in the window's words; see <see cref="ProposalDifferenceWords.Notes"/>.</summary>
    public IReadOnlyList<string> Notes => ProposalDifferenceWords.Notes(Summary?.Warnings);

    /// <summary>"3 words to check, 5 words improved".</summary>
    public string HeadlineText => Summary is { } summary
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalDifferenceHeadlineFormat,
            WordCount(summary.CouldBeBadWords), WordCount(summary.ImprovedWords))
        : string.Empty;

    /// <summary>"Compared 12 words (40 occurrences) in Genesis 1."</summary>
    public string ComparedText => Summary is { } summary
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalDifferenceComparedFormat,
            WordCount(summary.Words), Occurrences(summary.Occurrences), summary.TextTitle)
        : string.Empty;

    public string UnboundText => Summary is { } summary
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalDifferenceUnboundFormat,
            Occurrences(summary.UnboundOccurrences))
        : string.Empty;

    public string OutsideText => Summary is { } summary
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalDifferenceOutsideFormat,
            WordCount(summary.MeasuredWordsOutsideText))
        : string.Empty;

    /// <summary>"Showing 25 of 40.", and how many more words are counted and not listed when that is known.</summary>
    public string WordsStatusText
    {
        get
        {
            var shown = string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalDifferenceShowingFormat,
                Words.Count, MatchingWords);
            return NotListedInScope is > 0 and var more
                ? shown + " " + PluralText.Format(MotifAppStrings.ProposalDifferenceMoreNotListedPattern, more) : shown;
        }
    }

    /// <summary>Says no words match, or that the words of the chosen count are only counted.</summary>
    public string NoMatchingText => NotListedInScope is > 0 and var counted
        ? PluralText.Format(MotifAppStrings.ProposalDifferenceNoneListedPattern, counted)
        : Summary?.WordsListed is null ? MotifAppStrings.ProposalDifferenceNoMatching
        : MotifAppStrings.ProposalDifferenceNoListedMatching;

    // Counted and not listed in the one chosen count or the whole Difference; with several filters it is unknown.
    private int? NotListedInScope
    {
        get
        {
            if (Summary is not { WordsListed: not null } summary || !WordDetailKept) return null;
            var active = MovedChips.Concat(CheckChips).Concat(ImprovedChips).Concat(LimitationChips)
                .Where(chip => chip.IsActive).ToArray();
            var total = active.Length switch { 0 => summary.Words, 1 => active[0].Count, _ => (int?)null };
            return total is { } known ? Math.Max(0, known - MatchingWords) : null;
        }
    }

    public string SelectedWordTitle => SelectedWord is { } word
        ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalDifferencePlacesTitleFormat, word.Word)
        : string.Empty;

    /// <summary>"Before → After" for the selected word.</summary>
    public string SelectedWordMovedText => SelectedWord is { } word
        ? ProposalDifferenceWords.Moved(word.BeforeState, word.AfterState) : string.Empty;

    /// <summary>The categories the selected word is filed under, in the window's words.</summary>
    public string SelectedWordBucketsText => SelectedWord is { } word
        ? string.Join(", ", word.Buckets.Select(ProposalDifferenceWords.Bucket)) : string.Empty;

    public ICommand ClearFiltersCommand { get; }

    public ICommand ShowMoreWordsCommand { get; }

    public ICommand ShowMorePlacesCommand { get; }

    public ICommand OpenWordCommand { get; }

    private static string WordCount(int count) =>
        PluralText.Format(MotifAppStrings.ProposalDifferenceWordCountPattern, count);

    private static string Occurrences(int count) =>
        PluralText.Format(MotifAppStrings.ProposalDifferenceOccurrenceCountPattern, count);

    private void OpenWord()
    {
        if (SelectedWord is { } word) _openWord(word.Word);
    }
}
