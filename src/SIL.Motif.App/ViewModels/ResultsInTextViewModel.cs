using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>How the parser's answer for one occurrence compares with what the project stores there.</summary>
public enum OccurrenceVerdict
{
    /// <summary>The parser produced the analysis stored at this occurrence.</summary>
    Matches,

    /// <summary>An analysis is stored here, and the parser did not produce it.</summary>
    Differs,

    /// <summary>Nothing is stored here, and the parser proposes something.</summary>
    New,

    /// <summary>Nothing is stored here, and the parser found no way to build the word.</summary>
    NoParse,

    /// <summary>The parser stopped at a time or step limit before finishing this word.</summary>
    Limit,

    /// <summary>This word was not part of the Assessment.</summary>
    NotAssessed,
}

/// <summary>The Results In text view's filter chips; the last two verdicts show only under All.</summary>
public enum ResultsInTextFilter
{
    All,
    Differs,
    New,
    NoParse,
    Matches,
}

/// <summary>
/// The Results stage's In text view: the checked Texts read in place, each word's stored analysis with the
/// parser's verdict for that very occurrence beneath it. Rebuilt whenever the Texts stage reads new words or
/// an Assessment finishes; a filter keeps only the lines holding a matching word and dims the rest of them.
/// </summary>
public sealed partial class ResultsInTextViewModel : ObservableObject
{
    private readonly TextWordsViewModel _texts;
    private readonly AssessViewModel _assess;
    private readonly Action<string> _showWord;
    private readonly Action<string> _tryWord;
    private IReadOnlyList<ResultsTokenViewModel> _allWords = [];

    public ResultsInTextViewModel(
        TextWordsViewModel texts, AssessViewModel assess, Action<string> showWord, Action<string> tryWord)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(assess);
        ArgumentNullException.ThrowIfNull(showWord);
        ArgumentNullException.ThrowIfNull(tryWord);
        _texts = texts;
        _assess = assess;
        _showWord = showWord;
        _tryWord = tryWord;
        SetFilterCommand = new RelayCommand<ResultsInTextFilter>(filter => Filter = filter);
        ShowInWordsCommand = new RelayCommand(() => { if (SelectedToken is { } token) _showWord(token.Form); });
        TryWordCommand = new RelayCommand(() => { if (SelectedToken is { } token) _tryWord(token.Form); });
        _texts.PropertyChanged += OnSourceChanged;
        _assess.PropertyChanged += OnSourceChanged;
        Rebuild();
    }

    public ObservableCollection<ResultsTextViewModel> Texts { get; } = [];

    /// <summary>The selected Text's lines that hold a word matching <see cref="Filter"/>; every line under All.</summary>
    public ObservableCollection<ResultsLineViewModel> VisibleLines { get; } = [];

    public IRelayCommand<ResultsInTextFilter> SetFilterCommand { get; }

    /// <summary>Opens the selected word in the Words view.</summary>
    public IRelayCommand ShowInWordsCommand { get; }

    /// <summary>Opens the selected word in the Words view and traces it there.</summary>
    public IRelayCommand TryWordCommand { get; }

    [ObservableProperty]
    private ResultsTextViewModel? _selectedText;

    [ObservableProperty]
    private ResultsInTextFilter _filter = ResultsInTextFilter.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedToken))]
    private ResultsTokenViewModel? _selectedToken;

    public bool HasSelectedToken => SelectedToken is not null;

    public int AllCount => _allWords.Count;
    public int DiffersCount => Count(OccurrenceVerdict.Differs);
    public int NewCount => Count(OccurrenceVerdict.New);
    public int NoParseCount => Count(OccurrenceVerdict.NoParse);
    public int MatchesCount => Count(OccurrenceVerdict.Matches);

    /// <summary>Why nothing is shown, or <see langword="null"/> when there are lines to read.</summary>
    public string? Message =>
        _assess.Result is null ? "Run an Assessment to compare its answers with the words in your texts."
        : Texts.Count == 0 ? "Check a text in Texts to read the results in place."
        : SelectedText is null ? "Choose a text to read."
        : SelectedText.Lines.Count == 0
            ? "This text has no lines split into words yet. Open it once in FieldWorks' Interlinear Texts, then refresh the Baseline."
        : VisibleLines.Count == 0 ? "No word in this text matches the chosen filter."
        : null;

    public bool HasMessage => Message is not null;

    /// <summary>Shows a clicked word's comparison in the side panel.</summary>
    public void SelectToken(ResultsTokenViewModel token)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (token.IsWord) SelectedToken = token;
    }

    partial void OnSelectedTextChanged(ResultsTextViewModel? value) => RefreshLines();

    partial void OnFilterChanged(ResultsInTextFilter value) => RefreshLines();

    private int Count(OccurrenceVerdict verdict) => _allWords.Count(token => token.Verdict == verdict);

    private void OnSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, _texts) && e.PropertyName == nameof(TextWordsViewModel.Response)) Rebuild();
        else if (ReferenceEquals(sender, _assess) && e.PropertyName == nameof(AssessViewModel.Result)) Rebuild();
    }

    private void Rebuild()
    {
        var previousTitle = SelectedText?.Title;
        var results = (_assess.Result?.Words ?? [])
            .GroupBy(word => word.Word, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var hasAssessment = _assess.Result is not null;

        Texts.Clear();
        if (hasAssessment && _texts.Response is { } response)
        {
            foreach (var text in response.Texts) Texts.Add(new ResultsTextViewModel(text, results));
        }
        _allWords = Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens).Where(token => token.IsWord).ToArray();
        SelectedToken = null;
        OnPropertyChanged(nameof(AllCount));
        OnPropertyChanged(nameof(DiffersCount));
        OnPropertyChanged(nameof(NewCount));
        OnPropertyChanged(nameof(NoParseCount));
        OnPropertyChanged(nameof(MatchesCount));

        var reselected = Texts.FirstOrDefault(text => text.Title == previousTitle) ?? Texts.FirstOrDefault();
        if (ReferenceEquals(reselected, SelectedText)) RefreshLines();
        else SelectedText = reselected;
    }

    private void RefreshLines()
    {
        VisibleLines.Clear();
        foreach (var line in SelectedText?.Lines ?? [])
        {
            var any = false;
            foreach (var token in line.Tokens.Where(token => token.IsWord))
            {
                var matches = MatchesFilter(token.Verdict);
                token.IsDimmed = !matches;
                any |= matches;
            }
            if (any) VisibleLines.Add(line);
        }
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(HasMessage));
    }

    private bool MatchesFilter(OccurrenceVerdict verdict) => Filter switch
    {
        ResultsInTextFilter.Differs => verdict == OccurrenceVerdict.Differs,
        ResultsInTextFilter.New => verdict == OccurrenceVerdict.New,
        ResultsInTextFilter.NoParse => verdict == OccurrenceVerdict.NoParse,
        ResultsInTextFilter.Matches => verdict == OccurrenceVerdict.Matches,
        _ => true,
    };
}

/// <summary>One chosen Text, line by line, with every word compared against the Assessment.</summary>
public sealed class ResultsTextViewModel
{
    public ResultsTextViewModel(TextLines text, IReadOnlyDictionary<string, AssessmentWordResult> results)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(results);
        Title = text.Title;
        Lines = text.Lines.Select(line => new ResultsLineViewModel(text.Title, line, results)).ToArray();
    }

    public string Title { get; }
    public IReadOnlyList<ResultsLineViewModel> Lines { get; }
}

/// <summary>One line of a Text in the Results In text view.</summary>
public sealed class ResultsLineViewModel
{
    public ResultsLineViewModel(string title, TextLine line, IReadOnlyDictionary<string, AssessmentWordResult> results)
    {
        ArgumentNullException.ThrowIfNull(line);
        Number = line.Number;
        Tokens = line.Tokens.Select(token => new ResultsTokenViewModel(title, line.Number, token,
            token.Form is { } form && results.TryGetValue(form, out var result) ? result : null)).ToArray();
    }

    public int Number { get; }
    public IReadOnlyList<ResultsTokenViewModel> Tokens { get; }
}

/// <summary>
/// One token of a line: punctuation, or a word with the analysis stored at this occurrence and the parser's
/// verdict on it — whether the parser produced that analysis, something else, or nothing.
/// </summary>
public sealed partial class ResultsTokenViewModel : ObservableObject
{
    public ResultsTokenViewModel(string title, int line, TextToken token, AssessmentWordResult? result)
    {
        ArgumentNullException.ThrowIfNull(token);
        Text = token.Text;
        Form = token.Form ?? token.Text;
        IsWord = token.Form is not null;
        Location = $"{title}, line {line}";
        WordLink = token.WordLink is { } link ? new Uri(link) : null;
        Stored = token.Analysis?.Morphs.Select(morph => new ParserReadingMorphViewModel(morph)).ToArray() ?? [];

        var storedKey = token.Analysis?.Key;
        var analyses = result?.Morphology?.Analyses ?? [];
        var keys = analyses.Select(ProjectAnalysisKey.For).ToArray();
        var resolved = result?.Readings;
        var grades = result?.ReadingGrades;
        Readings = keys.Select((key, index) => new ResultsReadingViewModel(
                ReadingText(resolved is not null && index < resolved.Count ? resolved[index] : null),
                grades is not null && index < grades.Count ? grades[index] : null,
                storedKey is not null && key == storedKey))
            .ToArray();

        Verdict = !IsWord || result is null || result.Outcome == "skipped" ? OccurrenceVerdict.NotAssessed
            : storedKey is not null && keys.Contains(storedKey) ? OccurrenceVerdict.Matches
            : result.IsIncomplete ? OccurrenceVerdict.Limit
            : storedKey is not null ? OccurrenceVerdict.Differs
            : keys.Length > 0 ? OccurrenceVerdict.New
            : OccurrenceVerdict.NoParse;

        var first = Readings.FirstOrDefault()?.Text;
        var others = Readings.Count - 1;
        ParserLine = Verdict switch
        {
            OccurrenceVerdict.Matches => others > 0 ? $"✓ parser agrees, with {others} other reading{Plural(others)}" : "✓ parser agrees",
            OccurrenceVerdict.Differs when first is null => "✗ parser: no parse",
            OccurrenceVerdict.Differs => $"≠ parser: {first}" + (others > 0 ? $" (+{others})" : string.Empty),
            OccurrenceVerdict.New => $"parser: {first}" + (others > 0 ? $" (+{others})" : string.Empty),
            OccurrenceVerdict.NoParse => "no parse",
            OccurrenceVerdict.Limit => "parser stopped at a limit",
            _ => "not in this Assessment",
        };
        VerdictLabel = Verdict switch
        {
            OccurrenceVerdict.Matches => "Parser agrees with what is stored here",
            OccurrenceVerdict.Differs => "Parser differs from what is stored here",
            OccurrenceVerdict.New => "Nothing stored here; the parser proposes an analysis",
            OccurrenceVerdict.NoParse => "Nothing stored here, and the parser found no parse",
            OccurrenceVerdict.Limit => "The parser stopped at a time or step limit",
            _ => "This word was not part of the Assessment",
        };
    }

    public string Text { get; }

    /// <summary>The word's form as the Assessment names it, for finding it in the Words view.</summary>
    public string Form { get; }

    public bool IsWord { get; }
    public string Location { get; }
    public Uri? WordLink { get; }
    public bool HasWordLink => WordLink is not null;
    public bool HasNoWordLink => IsWord && WordLink is null;
    public string WordLinkName => $"Open {Text} in FieldWorks";

    /// <summary>The morphs of the analysis stored at this occurrence, each linked to its entry.</summary>
    public IReadOnlyList<ParserReadingMorphViewModel> Stored { get; }

    public bool HasStored => Stored.Count > 0;
    public bool HasNothingStored => IsWord && Stored.Count == 0;

    /// <summary>Every reading the parser produced for this word, graded, with the one stored here marked.</summary>
    public IReadOnlyList<ResultsReadingViewModel> Readings { get; }

    public bool HasReadings => Readings.Count > 0;

    public OccurrenceVerdict Verdict { get; }

    /// <summary>The short line under the word: the parser's answer against what is stored.</summary>
    public string ParserLine { get; }

    /// <summary>The verdict as a sentence, for the side panel.</summary>
    public string VerdictLabel { get; }

    public bool IsMatch => Verdict == OccurrenceVerdict.Matches;
    public bool IsDiffers => Verdict == OccurrenceVerdict.Differs;
    public bool IsNew => Verdict == OccurrenceVerdict.New;
    public bool IsNoParse => Verdict is OccurrenceVerdict.NoParse or OccurrenceVerdict.Limit;

    /// <summary>Whether the active filter passes over this word, so it recedes rather than disappears.</summary>
    [ObservableProperty]
    private bool _isDimmed;

    private static string ReadingText(ParserReading? reading) => reading is null ? "?"
        : string.Join("-", reading.Morphs.Select(morph => morph.Form)) + " ‘" +
          string.Join("-", reading.Morphs.Select(morph => morph.Gloss.Length == 0 ? "?" : morph.Gloss)) + "’";

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}

/// <summary>One parser reading of a word as the side panel lists it.</summary>
public sealed class ResultsReadingViewModel
{
    public ResultsReadingViewModel(string text, string? grade, bool isStoredHere)
    {
        Text = text;
        IsStoredHere = isStoredHere;
        GradeLabel = grade switch
        {
            "approved" => "Approved",
            "disapproved" => "Disapproved",
            "no-opinion" => "No opinion",
            _ => string.Empty,
        };
        IsDisapproved = grade == "disapproved";
    }

    public string Text { get; }
    public string GradeLabel { get; }
    public bool HasGrade => GradeLabel.Length > 0;
    public bool IsDisapproved { get; }

    /// <summary>Whether this is the analysis stored at the occurrence being looked at.</summary>
    public bool IsStoredHere { get; }
}
