using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Controls;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// One line token and the shared morphology comparison for its word, when it is a word.
/// </summary>
public sealed partial class ResultsTokenViewModel : ObservableObject
{
    private readonly TextToken _source;
    private readonly AssessmentWordResult? _assessment;
    private readonly Guid _textId;
    private bool _isUnread = true;

    [ObservableProperty]
    private bool _isCardOpen;

    public ResultsTokenViewModel(string title, int line, TextToken token, AssessmentWordResult? result,
        TextWordRowViewModel? projectWord = null, string? location = null, OccurrenceAnchor? occurrence = null,
        Guid textId = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        _source = token;
        _assessment = result;
        Text = token.Text;
        Form = token.Form ?? token.Text;
        IsWord = token.Form is not null;
        Location = location ?? $"{title}, line {line}";
        Occurrence = occurrence;
        _textId = textId != Guid.Empty ? textId : occurrence?.TextId ?? Guid.Empty;
        WordformId = token.WordformId;
        OccurrenceIndex = token.OccurrenceIndex;
        WordLink = token.WordLink is { } link ? new Uri(link) : null;
        Stored = token.Analysis?.Morphs.Select(morph => new ParserReadingMorphViewModel(morph)).ToArray() ?? [];
        ProjectSummary = projectWord?.ProjectSummary ?? "No project entry is loaded for this word.";
        ProjectStatusLabel = projectWord?.StatusLabel ?? ReadingGradeLabels.NotPresent;
        ProjectStatusVerdict = projectWord?.Verdict ?? global::SIL.Motif.App.ViewModels.Verdict.New;
        ProjectApprovedAnalyses = projectWord?.ApprovedAnalyses ?? [];

        Marking = AnalysisMarkingState.Create(token, result, _isUnread);
        var storedId = token.Analysis?.StoredAnalysisId;
        var analyses = result?.Morphology?.Analyses ?? [];
        var resolved = result?.Readings;
        var grades = result?.ReadingGrades;
        Readings = analyses.Select((analysis, index) => new ResultsReadingViewModel(
                ReadingText(resolved is not null && index < resolved.Count ? resolved[index] : null),
                grades is not null && index < grades.Count ? grades[index] : null,
                storedId is not null && Marking.PanGlossReadings[index].MatchingAnalysisIds.Contains(
                    storedId, StringComparer.Ordinal), analysis, index,
                resolved is not null && index < resolved.Count ? resolved[index] : null))
            .ToArray();

        Verdict = Marking.PanGlossClass switch
        {
            AnalysisMarkingClass.Same => OccurrenceVerdict.Matches,
            AnalysisMarkingClass.Capped => OccurrenceVerdict.Limit,
            AnalysisMarkingClass.None => OccurrenceVerdict.NoParse,
            AnalysisMarkingClass.NotAssessed => OccurrenceVerdict.NotAssessed,
            AnalysisMarkingClass.Different when !Marking.FieldWorksAnalyses.Any() => OccurrenceVerdict.New,
            _ => OccurrenceVerdict.Differs,
        };
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
            _ when result?.Outcome == "skipped" => "skipped: a character the grammar does not define",
            _ => "not in the last parse",
        };
        FieldWorksAnalyses = Marking.FieldWorksAnalyses
            .Select(analysis => new FieldWorksAnalysisDisplayViewModel(analysis)).ToArray();
    }

    public string Text { get; }

    /// <summary>The accessible name of the checkbox that includes this occurrence in bulk actions.</summary>
    public string SelectionAutomationName => $"Select {Form} for actions";

    /// <summary>The word's form as the Assessment names it, for finding it in the Words view.</summary>
    public string Form { get; }

    public string WordAutomationId => AutomationIds.ForWordPart(_textId, Form, OccurrenceIndex, "word");

    public string StripAutomationId => AutomationIds.ForWordPart(_textId, Form, OccurrenceIndex, "strip");

    public string OpinionMarkAutomationId => AutomationIds.ForWordPart(_textId, Form, OccurrenceIndex, "opinion");

    public string DisapprovedAutomationId => AutomationIds.ForWordPart(_textId, Form, OccurrenceIndex, "disapproved");

    public string FieldWorksAutomationId => AutomationIds.ForWordPart(_textId, Form, OccurrenceIndex, "fieldworks");

    public string PanGlossAutomationId => AutomationIds.ForWordPart(_textId, Form, OccurrenceIndex, "pangloss");

    public string PrimaryActionAutomationId => AutomationIds.ForWordPart(_textId, Form, OccurrenceIndex, "action");

    public string FixAutomationId => AutomationIds.ForWordPart(_textId, Form, OccurrenceIndex, "fix");

    public string StagedAutomationId => AutomationIds.ForWordPart(_textId, Form, OccurrenceIndex, "staged");

    public string UnreadAutomationId => AutomationIds.ForWordPart(_textId, Form, OccurrenceIndex, "unread");

    public bool IsWord { get; }

    public AnalysisMarkingState Marking { get; private set; }
    /// <summary>Every stored analysis with its opinion mark and interlinear morphemes.</summary>
    public IReadOnlyList<FieldWorksAnalysisDisplayViewModel> FieldWorksAnalyses { get; }

    /// <summary>The opinion mark used beside the word, or the dashed mark when nothing is stored.</summary>
    public OpinionMarkKind PrimaryOpinionMarkKind => FieldWorksAnalyses.Count == 0
        ? OpinionMarkKind.None : FieldWorksAnalyses[0].OpinionMarkKind;

    /// <summary>Whether FieldWorks stores at least one analysis for the word.</summary>
    public bool HasFieldWorksAnalyses => FieldWorksAnalyses.Count > 0;

    /// <summary>Whether PanGloss agrees with every stored reading.</summary>
    public bool IsPanGlossSame => Marking.PanGlossClass == AnalysisMarkingClass.Same;

    /// <summary>Whether PanGloss conflicts with a stored reading or opinion.</summary>
    public bool IsPanGlossDifferent => Marking.PanGlossClass is AnalysisMarkingClass.Conflict or
        AnalysisMarkingClass.Different;

    /// <summary>Whether PanGloss has readings beyond the approved FieldWorks readings.</summary>
    public bool IsPanGlossExtra => Marking.PanGlossClass == AnalysisMarkingClass.Extra;

    /// <summary>Whether PanGloss completed without finding a reading.</summary>
    public bool IsPanGlossNone => Marking.PanGlossClass == AnalysisMarkingClass.None;

    /// <summary>Whether PanGloss stopped before completing its search.</summary>
    public bool IsPanGlossCapped => Marking.PanGlossClass == AnalysisMarkingClass.Capped;

    /// <summary>The short PanGloss result shown in the word strip and hover summary.</summary>
    public string PanGlossSummary => Marking.PanGlossClass switch
    {
        AnalysisMarkingClass.Same => "Agrees with FieldWorks",
        AnalysisMarkingClass.Conflict => "Conflicts with a FieldWorks opinion",
        AnalysisMarkingClass.Different => "Different from FieldWorks",
        AnalysisMarkingClass.Extra => "Has additional readings",
        AnalysisMarkingClass.None => "No parse",
        AnalysisMarkingClass.Capped => "Search stopped at a limit",
        _ when _assessment is null => "Not parsed yet",
        _ => "Skipped: a character the grammar does not define",
    };

    /// <summary>Whether the strip marks the word Unread; a word nobody has parsed has nothing to read yet.</summary>
    public bool ShowUnread => Marking.IsUnread && _assessment is not null;

    /// <summary>Whether the word has a primary action available in its strip.</summary>
    public bool HasPrimaryAction => Marking.PrimaryAction is not null;

    /// <summary>The strip's one-click action, marked with a tick as Round 4 draws it.</summary>
    public string PrimaryActionLabel => Marking.PrimaryAction is { } action ? $"✓ {action.Label}" : string.Empty;

    /// <summary>The first stored analysis, which the strip's FieldWorks line shows; the card shows them all.</summary>
    public IReadOnlyList<ParserReadingMorphViewModel> PrimaryFieldWorksMorphs =>
        FieldWorksAnalyses.Count == 0 ? [] : FieldWorksAnalyses[0].Morphs;

    /// <summary>Whether FieldWorks stores more analyses than the strip's one line shows.</summary>
    public bool HasMoreFieldWorksAnalyses => FieldWorksAnalyses.Count > 1;

    /// <summary>How many stored analyses the strip leaves to the card.</summary>
    public string MoreFieldWorksLabel => $"+{FieldWorksAnalyses.Count - 1}";

    /// <summary>
    /// The PanGloss reading the strip shows when PanGloss differs from FieldWorks: the first one not stored at this
    /// occurrence, so an extra reading shows rather than the one FieldWorks already holds.
    /// </summary>
    public IReadOnlyList<ParserReadingMorphViewModel> PanGlossReadingMorphs =>
        (Readings.FirstOrDefault(reading => !reading.IsStoredHere) ?? Readings.FirstOrDefault())?.Morphs ?? [];

    /// <summary>Whether the strip's PanGloss line shows a reading rather than a short note.</summary>
    public bool ShowsPanGlossReading => (IsPanGlossDifferent || IsPanGlossExtra) && PanGlossReadingMorphs.Count > 0;

    /// <summary>Whether the strip's PanGloss line shows a short note rather than a reading.</summary>
    public bool ShowsPanGlossNote => !ShowsPanGlossReading;

    /// <summary>Whether PanGloss has more readings than the strip's one line shows.</summary>
    public bool HasMorePanGlossReadings => ShowsPanGlossReading && Readings.Count > 1;

    /// <summary>How many PanGloss readings the strip leaves to the card.</summary>
    public string MorePanGlossLabel => $"+{Readings.Count - 1}";

    /// <summary>The strip's PanGloss line when it shows no reading, in Round 4's short words.</summary>
    public string PanGlossNote => Marking.PanGlossClass switch
    {
        AnalysisMarkingClass.Same => "= same",
        AnalysisMarkingClass.None => "∅ No parse",
        AnalysisMarkingClass.Capped => "Stopped at the step limit",
        _ => PanGlossSummary,
    };

    /// <summary>Whether a staged change replaces the strip's actions with its Staged note.</summary>
    public bool HasStagedChanges => StagedChanges.Count > 0;

    /// <summary>Whether the strip offers its actions, which it does until a change is staged for the word.</summary>
    public bool ShowsActions => !HasStagedChanges;

    /// <summary>The read-only hover summary for the stored opinion and current PanGloss result.</summary>
    public string HoverSummary => $"{Form} · {FieldWorksSummary} · PanGloss: {PanGlossSummary}";

    private string FieldWorksSummary => FieldWorksAnalyses.Count == 0
        ? "No analysis in FieldWorks"
        : string.Join(", ", FieldWorksAnalyses.Select(analysis =>
            analysis.Opinion == ReadingGrade.Candidate ? "Unknown" : analysis.Opinion));
    public string Location { get; }
    public Uri? WordLink { get; }
    public OccurrenceAnchor? Occurrence { get; }
    public Guid? WordformId { get; }
    public int OccurrenceIndex { get; }
    public bool HasWordLink => WordLink is not null;
    public bool HasNoWordLink => IsWord && WordLink is null;
    public string WordLinkName => $"Open {Text} in FieldWorks";

    /// <summary>The Analyze texts actions available to this word card.</summary>
    public ResultsInTextViewModel? Actions { get; internal set; }

    public IAsyncRelayCommand<AnalysisMarkingChoice>? StageMarkingChoiceForTokenCommand { get; internal set; }

    /// <summary>The morphs of the analysis stored at this occurrence, each linked to its entry.</summary>
    public IReadOnlyList<ParserReadingMorphViewModel> Stored { get; }

    public string ProjectSummary { get; }

    public string ProjectStatusLabel { get; }

    public Verdict ProjectStatusVerdict { get; }

    public IReadOnlyList<ProjectAnalysisViewModel> ProjectApprovedAnalyses { get; }

    public bool HasProjectApprovedAnalyses => ProjectApprovedAnalyses.Count > 0;

    public bool HasStored => Stored.Count > 0;
    public bool HasNothingStored => IsWord && Stored.Count == 0;

    /// <summary>Every reading the parser produced for this word, graded, with the one stored here marked.</summary>
    public IReadOnlyList<ResultsReadingViewModel> Readings { get; }

    [ObservableProperty]
    private ResultsReadingViewModel? _selectedReading;

    [ObservableProperty]
    private bool _isPending;

    [ObservableProperty]
    private bool _isUncertainChanged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PendingChangeStatus))]
    private PendingChangeState _pendingState;

    public string? PendingChangeStatus => PendingChangeStates.Label(PendingState);

    public bool HasReadings => Readings.Count > 0;

    public OccurrenceVerdict Verdict { get; }

    /// <summary>The short line under the word: the parser's answer against what is stored.</summary>
    public string ParserLine { get; }

    /// <summary>The same PanGloss result label shown in the word strip and side panel.</summary>
    public string VerdictLabel => PanGlossSummary;

    public bool IsMatch => Verdict == OccurrenceVerdict.Matches;
    public bool IsDiffers => Verdict == OccurrenceVerdict.Differs;
    public bool IsNew => Verdict == OccurrenceVerdict.New;
    public bool IsNoParse => Verdict is OccurrenceVerdict.NoParse or OccurrenceVerdict.Limit;

    internal void SetReadState(bool isRead)
    {
        _isUnread = !isRead;
        Marking = Marking with { IsUnread = _isUnread };
        OnMarkingChanged();
    }

    internal void SetStagedMarkings(IReadOnlyList<ChangeViewModel> changes)
    {
        StagedChanges = changes.Select(change => new StagedMarkingDisplayViewModel(change)).ToArray();
        Marking = AnalysisMarkingState.Create(_source, _assessment, _isUnread);
        Marking = Marking.WithStagedTransitions(changes.Select(change => change.StagedTransition with
        {
            StoredAnalysisId = change.StoredAnalysisId,
            ReadingIndex = change.ReadingIndex,
            FitStatus = change.Fit?.Status,
        }).ToArray());
        OnPropertyChanged(nameof(StagedChanges));
        OnPropertyChanged(nameof(HasStagedChanges));
        OnPropertyChanged(nameof(ShowsActions));
        OnMarkingChanged();
    }

    private void OnMarkingChanged()
    {
        foreach (var name in new[]
                 {
                     nameof(Marking), nameof(ShowUnread), nameof(IsPanGlossSame), nameof(IsPanGlossDifferent),
                     nameof(IsPanGlossExtra), nameof(IsPanGlossNone), nameof(IsPanGlossCapped), nameof(PanGlossSummary),
                     nameof(PanGlossNote), nameof(ShowsPanGlossReading), nameof(ShowsPanGlossNote),
                     nameof(HasMorePanGlossReadings), nameof(HasPrimaryAction), nameof(PrimaryActionLabel),
                     nameof(HoverSummary),
                 })
            OnPropertyChanged(name);
    }

    /// <summary>Pending changes that affect this word and can be undone from its strip.</summary>
    public IReadOnlyList<StagedMarkingDisplayViewModel> StagedChanges { get; private set; } = [];

    /// <summary>The shared meaning behind <see cref="Verdict"/>, used for its colour and glyph.</summary>
    public Verdict Meaning => Verdict switch
    {
        OccurrenceVerdict.Matches => ViewModels.Verdict.Agrees,
        OccurrenceVerdict.Differs => ViewModels.Verdict.Differs,
        OccurrenceVerdict.New => ViewModels.Verdict.New,
        OccurrenceVerdict.NoParse => ViewModels.Verdict.NoResult,
        _ => ViewModels.Verdict.Limit,
    };

    /// <summary>Whether the parser also produced a reading FieldWorks has disapproved for this word.</summary>
    public bool HasDisapprovedReading => Readings.Any(reading => reading.IsDisapproved);

    /// <summary>What the disapproved marker says when a reader stops on it.</summary>
    public string DisapprovedTip => $"The parser also produced a reading FieldWorks has disapproved for {Text}.";

    /// <summary>Whether the active filter passes over this word, so it recedes rather than disappears.</summary>
    [ObservableProperty]
    private bool _isDimmed;

    [ObservableProperty]
    private bool _isSelectedForActions;

    // Forms already carry their own hyphens ("a-", "-a"), so they join as written; glosses join with one.
    private static string ReadingText(ParserReading? reading) => reading is null ? "?"
        : JoinForms(reading.Morphs.Select(morph => morph.Form)) + " ‘" +
          string.Join("-", reading.Morphs.Select(morph => morph.Gloss.Length == 0 ? "?" : morph.Gloss)) + "’";

    private static string JoinForms(IEnumerable<string> forms)
    {
        var parts = forms.ToArray();
        return parts.Any(form => form.StartsWith('-') || form.EndsWith('-'))
            ? string.Concat(parts).Replace("--", "-", StringComparison.Ordinal)
            : string.Join("-", parts);
    }

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}

/// <summary>One parser reading of a word as the side panel lists it.</summary>
public sealed class ResultsReadingViewModel
{
    public ResultsReadingViewModel(string text, string? grade, bool isStoredHere,
        ParseAnalysis? analysis = null, int index = -1, ParserReading? reading = null)
    {
        Text = text;
        IsStoredHere = isStoredHere;
        GradeLabel = ReadingGradeLabels.Of(grade);
        IsDisapproved = grade == ReadingGrade.Disapproved;
        Analysis = analysis;
        Index = index;
        Morphs = reading?.Morphs.Select(morph => new ParserReadingMorphViewModel(morph)).ToArray() ?? [];
    }

    public string Text { get; }

    public string GradeLabel { get; }
    public bool HasGrade => GradeLabel.Length > 0;
    public bool IsDisapproved { get; }

    public ParseAnalysis? Analysis { get; }

    public int Index { get; }

    /// <summary>Whether this is the analysis stored at the occurrence being looked at.</summary>
    public bool IsStoredHere { get; }

    public IReadOnlyList<ParserReadingMorphViewModel> Morphs { get; }

    public bool HasMorphs => Morphs.Count > 0;
}
