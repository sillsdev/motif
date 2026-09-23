using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Composes one <see cref="SelectionRequest"/> from the four agreed sources (design decision 4): a
/// searchable checked list of the current Baseline's Texts, pasted or typed words, All wordforms, Retry
/// failed, and a Retry-slower-than threshold. Every source is client-side bookkeeping only — actually
/// resolving a Selection's words happens later, inside the Assess command's own
/// <see cref="SIL.Motif.Commands.Assess.SelectionComposer"/>, which is the one place that opens a project.
/// </summary>
public sealed partial class SelectionViewModel : ObservableObject
{
    private const string NothingSelectedYet = "Nothing selected yet.";
    private const string NegativeThresholdMessage = "The retry-slower-than threshold must not be negative.";

    private readonly ICommandClient _commandClient;
    private readonly List<TextChoiceViewModel> _allTexts = [];
    private int _textLoadGeneration;

    public SelectionViewModel(ICommandClient commandClient)
    {
        ArgumentNullException.ThrowIfNull(commandClient);
        _commandClient = commandClient;
        Recompute();
    }

    /// <summary>The current Baseline's Texts matching <see cref="SearchText"/>, most-recently-loaded order.</summary>
    public ObservableCollection<TextChoiceViewModel> Texts { get; } = [];

    /// <summary>Why the Text list is empty, or <c>null</c> when it is not.</summary>
    [ObservableProperty]
    private string? _textsEmptyMessage;

    /// <summary>The chosen Texts' own GUIDs, recomputed whenever a Text is checked or unchecked.</summary>
    public IReadOnlyList<Guid> ChosenTextIds { get; private set; } = [];

    /// <summary>One entry per non-blank line of <see cref="PastedWords"/>, trimmed.</summary>
    public IReadOnlyList<string> PastedWordEntries { get; private set; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _pastedWords = string.Empty;

    [ObservableProperty]
    private bool _allWordforms;

    [ObservableProperty]
    private bool _retryFailed;

    // Seconds the parser may spend on one word in this run; null keeps the project's configured limit.
    [ObservableProperty]
    private decimal? _perWordTimeLimitSeconds;

    [ObservableProperty]
    private decimal? _retrySlowerThanMilliseconds;

    [ObservableProperty]
    private string? _refusalMessage;

    [ObservableProperty]
    private string? _thresholdValidationMessage;

    [ObservableProperty]
    private bool _canAssess;

    [ObservableProperty]
    private string _summaryText = NothingSelectedYet;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnPastedWordsChanged(string value) => Recompute();

    partial void OnAllWordformsChanged(bool value) => Recompute();

    partial void OnRetryFailedChanged(bool value) => Recompute();

    partial void OnRetrySlowerThanMillisecondsChanged(decimal? value) => Recompute();

    /// <summary>Composes the current state into the one request every source agrees to combine into.</summary>
    public SelectionRequest BuildRequest()
    {
        var threshold = ThresholdValidationMessage is null && RetrySlowerThanMilliseconds is { } ms
            ? TimeSpan.FromMilliseconds((double)ms)
            : (TimeSpan?)null;
        return new SelectionRequest(AllWordforms, ChosenTextIds, PastedWordEntries, RetryFailed, threshold);
    }

    /// <summary>Loads the Texts of a newly chosen project's current Baseline, discarding whatever was shown before.</summary>
    public async Task SetProjectAsync(string fwDataPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fwDataPath);

        _allTexts.Clear();
        Texts.Clear();
        SearchText = string.Empty;
        PastedWords = string.Empty;
        AllWordforms = false;
        RetryFailed = false;
        RetrySlowerThanMilliseconds = null;
        PerWordTimeLimitSeconds = null;
        RefusalMessage = null;
        TextsEmptyMessage = null;
        Recompute();

        await LoadTextsAsync(fwDataPath, cancellationToken);
    }

    /// <summary>
    /// Reloads the Text list from the project's current Baseline, keeping every other Selection source and
    /// the checked state of any Text the new Baseline still holds. For after a Refresh: the project chosen a
    /// moment ago had no Baseline, so its list was empty, and the person should not have to choose it again.
    /// </summary>
    public async Task LoadTextsAsync(string fwDataPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fwDataPath);

        var previouslyChecked = _allTexts.Where(text => text.IsChecked).Select(text => text.Id).ToHashSet();
        var generation = ++_textLoadGeneration;
        var outcome = await _commandClient.ListTextsAsync(new TextInventoryRequest(fwDataPath), cancellationToken);
        // A Refresh or project switch mid-load starts a newer load; the older answer must not overwrite it.
        if (generation != _textLoadGeneration) return;
        if (!outcome.Succeeded)
        {
            RefusalMessage = outcome.Refusal!.Message;
            return;
        }

        foreach (var text in _allTexts) text.PropertyChanged -= OnTextChoicePropertyChanged;
        _allTexts.Clear();
        foreach (var choice in outcome.Value!.Texts)
        {
            var textChoice = new TextChoiceViewModel(choice.Id, choice.Title) { IsChecked = previouslyChecked.Contains(choice.Id) };
            textChoice.PropertyChanged += OnTextChoicePropertyChanged;
            _allTexts.Add(textChoice);
        }

        RefusalMessage = null;
        TextsEmptyMessage = _allTexts.Count > 0
            ? null
            : outcome.Value!.HasBaseline ? "This Baseline has no Texts." : "Capture a Baseline to choose Texts.";
        ApplyFilter();
        Recompute();
    }

    private void OnTextChoicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TextChoiceViewModel.IsChecked)) Recompute();
    }

    /// <summary>
    /// Sets one Text's occurrence and distinct-word counts, once its words have been read. A Text nothing
    /// has set counts for shows none, rather than a stale count from before it was last unchecked.
    /// </summary>
    public void SetTextCounts(Guid textId, int occurrenceCount, int distinctWordCount)
    {
        var text = _allTexts.FirstOrDefault(candidate => candidate.Id == textId);
        if (text is null) return;
        text.OccurrenceCount = occurrenceCount;
        text.DistinctWordCount = distinctWordCount;
    }

    /// <summary>Clears every Text's counts, for a reload whose response no longer covers some of them.</summary>
    public void ClearTextCounts()
    {
        foreach (var text in _allTexts)
        {
            text.OccurrenceCount = null;
            text.DistinctWordCount = null;
        }
    }

    private void ApplyFilter()
    {
        Texts.Clear();
        var matches = string.IsNullOrWhiteSpace(SearchText)
            ? _allTexts
            : _allTexts.Where(text => text.Title.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase));
        foreach (var text in matches) Texts.Add(text);
    }

    // Recomputed from raw, client-side state alone: no project is opened here (design decision 4).
    private void Recompute()
    {
        var chosenBefore = ChosenTextIds;
        ChosenTextIds = _allTexts.Where(text => text.IsChecked).Select(text => text.Id).ToList();
        if (!chosenBefore.SequenceEqual(ChosenTextIds)) OnPropertyChanged(nameof(ChosenTextIds));
        PastedWordEntries = SplitPastedWords(PastedWords);

        var hasThreshold = RetrySlowerThanMilliseconds is not null;
        var thresholdValid = !hasThreshold || RetrySlowerThanMilliseconds >= 0;
        ThresholdValidationMessage = thresholdValid ? null : NegativeThresholdMessage;

        var hasAnySource = AllWordforms || ChosenTextIds.Count > 0 || PastedWordEntries.Count > 0
            || RetryFailed || (hasThreshold && thresholdValid);

        CanAssess = hasAnySource && thresholdValid;
        SummaryText = BuildSummary(hasAnySource, hasThreshold && thresholdValid);
    }

    private string BuildSummary(bool hasAnySource, bool includeThreshold)
    {
        if (!hasAnySource) return NothingSelectedYet;

        var parts = new List<string>();
        if (AllWordforms) parts.Add("all wordforms");
        if (ChosenTextIds.Count > 0) parts.Add(Pluralize(ChosenTextIds.Count, "text"));
        if (PastedWordEntries.Count > 0) parts.Add(Pluralize(PastedWordEntries.Count, "pasted word"));
        if (RetryFailed) parts.Add("retry failed");
        if (includeThreshold) parts.Add($"retry slower than {RetrySlowerThanMilliseconds} ms");
        return string.Join(", ", parts);
    }

    private static string Pluralize(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private static IReadOnlyList<string> SplitPastedWords(string pastedWords) =>
        pastedWords.Replace("\r\n", "\n").Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
}
