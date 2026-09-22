using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;

namespace SIL.Motif.App.ViewModels;

/// <summary>The Texts stage's two views of the checked Texts' words.</summary>
public enum TextsView
{
    /// <summary>One row per distinct word, with where it occurs and what the project holds for it.</summary>
    Words,

    /// <summary>The chosen Texts read in place, each word underlined by status.</summary>
    InText,
}

/// <summary>One bucket a word's project analyses fall into, for the Words table's filter chips.</summary>
public enum WordProjectStatus
{
    /// <summary>No occurrence has an approved analysis, and the project approves none for this form.</summary>
    None,

    /// <summary>Every occurrence that has an analysis agrees, and the project approves it.</summary>
    Approved,

    /// <summary>Two occurrences of the same form carry different chosen analyses.</summary>
    DiffersByOccurrence,
}

/// <summary>
/// Reads the checked Texts' words for the Texts stage: the Words table (one row per distinct form, its
/// occurrences, and what the project holds for it) and the In Text reader (the Texts read in place, each
/// word coloured by the same status). Reloads whenever <see cref="SelectionViewModel.ChosenTextIds"/>
/// changes, guarded by a generation counter rather than a timer so a rapid run of checkbox clicks only
/// ever applies the last one's answer.
/// </summary>
public sealed partial class TextWordsViewModel : ObservableObject
{
    private readonly ICommandClient _commandClient;
    private readonly SelectionViewModel _selection;
    private readonly List<TextWordRowViewModel> _all = [];
    private string? _projectPath;
    private int _generation;

    public TextWordsViewModel(ICommandClient commandClient, SelectionViewModel selection)
    {
        ArgumentNullException.ThrowIfNull(commandClient);
        ArgumentNullException.ThrowIfNull(selection);
        _commandClient = commandClient;
        _selection = selection;
        _selection.PropertyChanged += OnSelectionPropertyChanged;
        SetViewCommand = new RelayCommand<TextsView>(view => View = view);
        SetStatusFilterCommand = new RelayCommand<WordProjectStatus?>(status => StatusFilter = status);
        ReaderTexts.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ReaderMessage));
            OnPropertyChanged(nameof(HasReaderMessage));
        };
    }

    public ObservableCollection<TextWordRowViewModel> Rows { get; } = [];

    public ObservableCollection<ReaderTextViewModel> ReaderTexts { get; } = [];

    /// <summary>Chooses the Words or In Text view, replacing whichever was chosen before.</summary>
    public IRelayCommand<TextsView> SetViewCommand { get; }

    /// <summary>Chooses one of the Words table's status filter chips, or <see langword="null"/> for All.</summary>
    public IRelayCommand<WordProjectStatus?> SetStatusFilterCommand { get; }

    [ObservableProperty]
    private TextsView _view = TextsView.Words;

    public bool ShowWords => View == TextsView.Words;
    public bool ShowInText => View == TextsView.InText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedReaderTextTitle))]
    [NotifyPropertyChangedFor(nameof(ReaderMessage))]
    [NotifyPropertyChangedFor(nameof(HasReaderMessage))]
    private ReaderTextViewModel? _selectedReaderText;

    /// <summary>Why the reader has nothing to show, or <see langword="null"/> when it has lines.</summary>
    public string? ReaderMessage =>
        IsLoading ? "Reading the chosen texts..."
        : RefusalMessage is { } refusal ? refusal
        : ReaderTexts.Count == 0 ? "Check a text on the left to read it here."
        : SelectedReaderText is null ? "Choose a text to read."
        : SelectedReaderText.Lines.Count == 0
            ? "This text has no lines split into words yet. Open it once in FieldWorks' Interlinear Texts, " +
              "then refresh the Baseline."
            : null;

    public bool HasReaderMessage => ReaderMessage is not null;

    public string SelectedReaderTextTitle => SelectedReaderText?.Title ?? string.Empty;

    partial void OnSelectedReaderTextChanged(ReaderTextViewModel? value) => SelectedOccurrence = null;

    [ObservableProperty]
    private ReaderOccurrenceViewModel? _selectedOccurrence;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private WordProjectStatus? _statusFilter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReaderMessage))]
    [NotifyPropertyChangedFor(nameof(HasReaderMessage))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReaderMessage))]
    [NotifyPropertyChangedFor(nameof(HasReaderMessage))]
    private string? _refusalMessage;

    [ObservableProperty]
    private bool _hasBaseline = true;

    [ObservableProperty]
    private int _occurrenceCount;

    [ObservableProperty]
    private int _approvedCount;

    public int WordCount => _all.Count;

    public string SummaryText => WordCount == 0 ? "No words to test yet"
        : $"{WordCount} word{(WordCount == 1 ? string.Empty : "s")} to test · {OccurrenceCount} occurrence{(OccurrenceCount == 1 ? string.Empty : "s")} · {ApprovedCount} approved";

    public int AllCount => _all.Count;
    public int ApprovedFilterCount => _all.Count(row => row.Status == WordProjectStatus.Approved);
    public int NoneFilterCount => _all.Count(row => row.Status == WordProjectStatus.None);
    public int DiffersFilterCount => _all.Count(row => row.Status == WordProjectStatus.DiffersByOccurrence);

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnStatusFilterChanged(WordProjectStatus? value) => ApplyFilter();

    /// <summary>Sets the project to read words from and immediately reloads for whatever is checked now.</summary>
    public async Task SetProjectAsync(string? fwDataPath, CancellationToken cancellationToken = default)
    {
        _projectPath = fwDataPath;
        _generation++;
        _all.Clear();
        Rows.Clear();
        ReaderTexts.Clear();
        SelectedReaderText = null;
        SelectedOccurrence = null;
        RefusalMessage = null;
        HasBaseline = true;
        OccurrenceCount = 0;
        ApprovedCount = 0;
        RaiseCounts();
        if (fwDataPath is not null) await ReloadAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (_projectPath is not { } path) return;

        var textIds = _selection.ChosenTextIds;
        var generation = ++_generation;
        IsLoading = true;

        var outcome = await _commandClient.ListTextWordsAsync(new TextWordsRequest(path, textIds), cancellationToken)
            .ConfigureAwait(true);
        if (generation != _generation) return;

        IsLoading = false;
        if (!outcome.Succeeded)
        {
            RefusalMessage = outcome.Refusal!.Message;
            return;
        }

        RefusalMessage = null;
        HasBaseline = outcome.Value!.HasBaseline;

        _all.Clear();
        _all.AddRange(outcome.Value.Words.Select(word => new TextWordRowViewModel(word)));
        OccurrenceCount = _all.Sum(row => row.OccurrenceCount);
        ApprovedCount = _all.Count(row => row.HasApproved);
        RaiseCounts();
        ApplyFilter();

        var previousTitle = SelectedReaderText?.Title;
        ReaderTexts.Clear();
        foreach (var text in outcome.Value.Texts) ReaderTexts.Add(new ReaderTextViewModel(text, outcome.Value.Words));
        SelectedReaderText = ReaderTexts.FirstOrDefault(text => text.Title == previousTitle) ?? ReaderTexts.FirstOrDefault();

        _selection.ClearTextCounts();
        foreach (var textId in textIds)
        {
            var occurrences = _all.Sum(row => row.Occurrences.Count(occurrence => occurrence.TextId == textId));
            var distinct = _all.Count(row => row.Occurrences.Any(occurrence => occurrence.TextId == textId));
            _selection.SetTextCounts(textId, occurrences, distinct);
        }
    }

    /// <summary>Shows the occurrence a reader token represents in the side panel.</summary>
    public void SelectToken(ReaderTokenViewModel token)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (token.Occurrence is { } occurrence) SelectedOccurrence = occurrence;
    }

    private void ApplyFilter()
    {
        Rows.Clear();
        var matches = _all.AsEnumerable();
        if (StatusFilter is { } status) matches = matches.Where(row => row.Status == status);
        if (!string.IsNullOrWhiteSpace(SearchText))
            matches = matches.Where(row => row.Form.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase));
        foreach (var row in matches) Rows.Add(row);
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(WordCount));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(AllCount));
        OnPropertyChanged(nameof(ApprovedFilterCount));
        OnPropertyChanged(nameof(NoneFilterCount));
        OnPropertyChanged(nameof(DiffersFilterCount));
    }

    private async void OnSelectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectionViewModel.ChosenTextIds)) await ReloadAsync().ConfigureAwait(true);
    }
}

/// <summary>One distinct word form as the Words table shows it: its occurrences and the project's own analyses.</summary>
public sealed class TextWordRowViewModel
{
    public TextWordRowViewModel(TextWord word)
    {
        ArgumentNullException.ThrowIfNull(word);
        Form = word.Form;
        OccurrenceCount = word.Occurrences.Count;
        HasApproved = word.Approved.Count > 0;

        var chosenKeys = word.Occurrences.Select(occurrence => occurrence.Analysis?.Key)
            .Where(key => key is not null).Distinct().ToList();
        Status = chosenKeys.Count > 1 ? WordProjectStatus.DiffersByOccurrence
            : word.Approved.Count > 0 ? WordProjectStatus.Approved
            : word.Disapproved.Count > 0 ? WordProjectStatus.DiffersByOccurrence
            : WordProjectStatus.None;

        StatusLabel = Status switch
        {
            WordProjectStatus.DiffersByOccurrence => "Differs by occurrence",
            WordProjectStatus.Approved => word.Approved.Count > 1 ? $"Approved ×{word.Approved.Count}" : "Approved",
            _ => "None",
        };

        ProjectSummary = Status == WordProjectStatus.DiffersByOccurrence
            ? $"{chosenKeys.Count} analyses: {string.Join(", ", DistinctGlosses(word))}"
            : word.Approved.Count > 0 ? DistinctGlosses(word).FirstOrDefault() ?? string.Empty
            : "Not analysed in the project";

        Occurrences = word.Occurrences.Select(occurrence => new WordOccurrenceRowViewModel(occurrence)).ToArray();
        ApprovedAnalyses = word.Approved.Select(analysis => new ProjectAnalysisViewModel(analysis)).ToArray();
    }

    public string Form { get; }
    public int OccurrenceCount { get; }
    public bool HasApproved { get; }
    public WordProjectStatus Status { get; }
    public string StatusLabel { get; }
    public string ProjectSummary { get; }
    public IReadOnlyList<WordOccurrenceRowViewModel> Occurrences { get; }
    public IReadOnlyList<ProjectAnalysisViewModel> ApprovedAnalyses { get; }

    private static IEnumerable<string> DistinctGlosses(TextWord word) =>
        word.Occurrences.Select(occurrence => occurrence.Analysis)
            .Where(analysis => analysis is not null)
            .Select(analysis => string.Join(" ", analysis!.Morphs.Select(morph => morph.Gloss)))
            .Distinct();
}

/// <summary>One place a word occurs, as the Words table's expanded row shows it.</summary>
public sealed class WordOccurrenceRowViewModel
{
    public WordOccurrenceRowViewModel(WordOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        TextId = occurrence.TextId;
        TextTitle = occurrence.TextTitle;
        Line = occurrence.Line;
        Sentence = occurrence.Sentence;
        Status = occurrence.Status;
        Analysis = occurrence.Analysis is { } analysis ? new ProjectAnalysisViewModel(analysis) : null;
        Location = $"{TextTitle}, line {Line}";
    }

    public Guid TextId { get; }
    public string TextTitle { get; }
    public int Line { get; }
    public string Sentence { get; }
    public string Status { get; }
    public ProjectAnalysisViewModel? Analysis { get; }
    public string Location { get; }
}

/// <summary>One analysis the project holds, as morphs a person reads.</summary>
public sealed class ProjectAnalysisViewModel
{
    public ProjectAnalysisViewModel(ProjectAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        Morphs = analysis.Morphs.Select(morph => new ParserReadingMorphViewModel(morph)).ToArray();
        Gloss = string.Join(" ", Morphs.Select(morph => morph.Gloss.Length == 0 ? "?" : morph.Gloss));
    }

    public IReadOnlyList<ParserReadingMorphViewModel> Morphs { get; }
    public string Gloss { get; }
}

/// <summary>One chosen Text, line by line, for the In Text reader.</summary>
public sealed class ReaderTextViewModel
{
    public ReaderTextViewModel(TextLines text, IReadOnlyList<TextWord> words)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(words);
        Title = text.Title;
        var byForm = words.ToDictionary(word => word.Form);
        Lines = text.Lines.Select(line => new ReaderLineViewModel(text.TextId, line, byForm)).ToArray();
    }

    public string Title { get; }
    public IReadOnlyList<ReaderLineViewModel> Lines { get; }
}

/// <summary>One line of a Text, for the In Text reader.</summary>
public sealed class ReaderLineViewModel
{
    public ReaderLineViewModel(Guid textId, TextLine line, IReadOnlyDictionary<string, TextWord> byForm)
    {
        ArgumentNullException.ThrowIfNull(line);
        Number = line.Number;
        Tokens = line.Tokens.Select(token => new ReaderTokenViewModel(textId, line.Number, token, byForm)).ToArray();
    }

    public int Number { get; }
    public IReadOnlyList<ReaderTokenViewModel> Tokens { get; }
}

/// <summary>One token of a reader line: a word coloured by its project status, or plain punctuation.</summary>
public sealed class ReaderTokenViewModel
{
    public ReaderTokenViewModel(Guid textId, int line, TextToken token, IReadOnlyDictionary<string, TextWord> byForm)
    {
        ArgumentNullException.ThrowIfNull(token);
        Text = token.Text;
        IsWord = token.Form is not null;
        Gloss = token.Gloss;
        Morphs = token.Analysis?.Morphs.Select(morph => new ParserReadingMorphViewModel(morph)).ToArray() ?? [];
        WordGloss = token.WordGloss;
        Category = token.Category;
        WordLink = token.WordLink is { } link ? new Uri(link) : null;

        if (token.Form is not { } form || !byForm.TryGetValue(form, out var word))
        {
            Status = null;
            Occurrence = null;
            return;
        }

        var chosenKeys = word.Occurrences.Select(occurrence => occurrence.Analysis?.Key)
            .Where(key => key is not null).Distinct().Count();
        Status = chosenKeys > 1 ? WordProjectStatus.DiffersByOccurrence
            : word.Approved.Count > 0 ? WordProjectStatus.Approved
            : word.Disapproved.Count > 0 ? WordProjectStatus.DiffersByOccurrence
            : WordProjectStatus.None;

        var occurrence = word.Occurrences.FirstOrDefault(candidate => candidate.TextId == textId && candidate.Line == line);
        Occurrence = occurrence is null ? null : new ReaderOccurrenceViewModel(word, occurrence);
    }

    public string Text { get; }
    public bool IsWord { get; }
    public string? Gloss { get; }

    /// <summary>The morphs of the analysis chosen here, each with its gloss and a link to its entry.</summary>
    public IReadOnlyList<ParserReadingMorphViewModel> Morphs { get; }

    public bool HasAnalysis => Morphs.Count > 0;

    public bool IsUnanalysed => IsWord && Morphs.Count == 0;

    public string? WordGloss { get; }

    public string? Category { get; }

    /// <summary>What sits under the morphs: the word gloss, then the category, whichever the project has.</summary>
    public string? WordLine => (WordGloss, Category) switch
    {
        ({ Length: > 0 } gloss, { Length: > 0 } category) => $"{gloss}  ·  {category}",
        ({ Length: > 0 } gloss, _) => gloss,
        (_, { Length: > 0 } category) => category,
        _ => null,
    };

    public Uri? WordLink { get; }

    public bool HasWordLink => WordLink is not null;

    public bool HasNoWordLink => IsWord && WordLink is null;

    /// <summary>The accessible name of the word's link into FieldWorks.</summary>
    public string WordLinkName => $"Open {Text} in FieldWorks";

    public WordProjectStatus? Status { get; }
    public ReaderOccurrenceViewModel? Occurrence { get; }
}

/// <summary>The side panel shown when a reader token is clicked: this occurrence, and every other one.</summary>
public sealed class ReaderOccurrenceViewModel
{
    public ReaderOccurrenceViewModel(TextWord word, WordOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(word);
        ArgumentNullException.ThrowIfNull(occurrence);
        Form = word.Form;
        Location = $"{occurrence.TextTitle}, line {occurrence.Line}";
        var index = word.Occurrences.ToList().FindIndex(candidate =>
            candidate.TextId == occurrence.TextId && candidate.Line == occurrence.Line);
        OccurrenceLabel = $"Occurrence {index + 1} of {word.Occurrences.Count} · {Location}";
        Status = occurrence.Status;
        Analysis = occurrence.Analysis is { } analysis ? new ProjectAnalysisViewModel(analysis) : null;

        var distinctKeys = word.Occurrences.Select(o => o.Analysis?.Key).Where(key => key is not null).Distinct().ToList();
        Differs = distinctKeys.Count > 1;
        ApprovedAnalyses = word.Approved.Select(analysis => new ProjectAnalysisViewModel(analysis)).ToArray();
        AllOccurrences = word.Occurrences.Select(candidate => new WordOccurrenceRowViewModel(candidate)).ToArray();
    }

    public string Form { get; }
    public string Location { get; }
    public string OccurrenceLabel { get; }
    public string Status { get; }
    public ProjectAnalysisViewModel? Analysis { get; }
    public bool Differs { get; }
    public IReadOnlyList<ProjectAnalysisViewModel> ApprovedAnalyses { get; }
    public IReadOnlyList<WordOccurrenceRowViewModel> AllOccurrences { get; }
}
