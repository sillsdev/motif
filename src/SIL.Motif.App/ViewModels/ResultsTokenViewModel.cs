using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Globalization;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// One line token and the shared morphology comparison for its word, when it is a word.
/// </summary>
public sealed partial class ResultsTokenViewModel : ObservableObject, IDisposable
{
    private IDisposable? _modelRegistration;
    private readonly ExpectedContext? _expectedContext;
    private SelectionWordActionFacts? _actionFacts;
    private readonly string? _producingAssessmentId;
    private readonly TextToken _source;
    private readonly AssessmentWordResult? _assessment;
    private readonly Guid _textId;
    private Uri? _wordLink;
    private WordComparison? _comparison;
    private readonly bool _storedAnalysesAvailable;
    private IReadOnlyList<ParserReadingMorphViewModel>? _stored;
    private IReadOnlyList<FieldWorksAnalysisDisplayViewModel>? _fieldWorksAnalyses;
    private bool _isUnread = true;
    private WordInteractionState _presentationState;
    private IReadOnlyList<ObjectUseRef> _namedMorphemeRefs = [];

    [ObservableProperty]
    private bool _isCardOpen;

    public ResultsTokenViewModel(string title, int line, TextToken token, AssessmentWordResult? result,
        string? location = null, OccurrenceAnchor? occurrence = null,
        Guid textId = default,
        SelectionReadLease? lease = null, ExpectedContext? expectedContext = null, string? producingAssessmentId = null,
        SelectionWordActionFacts? actionFacts = null, bool storedAnalysesAvailable = true)
    {
        ArgumentNullException.ThrowIfNull(token);
        _source = token;
        _expectedContext = expectedContext;
        _actionFacts = actionFacts;
        _storedAnalysesAvailable = storedAnalysesAvailable;
        _producingAssessmentId = producingAssessmentId;
        _assessment = result;
        Text = token.Text;
        Form = token.Form ?? token.Text;
        TextWritingSystem = token.TextWritingSystem;
        FormWritingSystem = token.FormWritingSystem;
        WordGlossWritingSystem = token.WordGlossWritingSystem;
        CategoryWritingSystem = token.CategoryWritingSystem;
        IsWord = token.Form is not null;
        Location = location ?? $"{title}, line {line}";
        Occurrence = occurrence;
        _textId = textId != Guid.Empty ? textId : occurrence?.TextId ?? Guid.Empty;
        WordformId = token.WordformId;
        OccurrenceIndex = token.OccurrenceIndex;
        PresentationKey = new WordPresentationKey(occurrence is { } anchor
            ? $"occurrence:{anchor.TextId:N}:{anchor.ParagraphId:N}:{anchor.SegmentId:N}:{anchor.Index}"
            : $"token:{_textId:N}:{line}:{OccurrenceIndex}:{Form}");
        _presentationState = new WordInteractionState(PresentationKey);

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

        Verdict = result is null || Comparison.MeaningCode == "refused" ? OccurrenceVerdict.NotAssessed : Comparison.Outcome switch
        {
            WordRowOutcome.Same when Comparison.Tone != WordRowTone.Problem => OccurrenceVerdict.Matches,
            WordRowOutcome.Stopped => OccurrenceVerdict.Limit,
            WordRowOutcome.NoParse => OccurrenceVerdict.NoParse,
            WordRowOutcome.NotParsed => OccurrenceVerdict.NotAssessed,
            WordRowOutcome.Different when !Marking.FieldWorksAnalyses.Any() => OccurrenceVerdict.New,
            _ => OccurrenceVerdict.Differs,
        };
        var first = Readings.FirstOrDefault()?.Text;
        var others = Readings.Count - 1;
        ParserLine = Verdict switch
        {
            OccurrenceVerdict.Matches => others switch
            {
                0 => "parser agrees",
                1 => "parser agrees, with 1 other analysis",
                _ => $"parser agrees, with {others} other analyses",
            },
            OccurrenceVerdict.Differs when first is null => "parser: no parse",
            OccurrenceVerdict.Differs => $"parser: {first}" + (others > 0 ? $" (+{others})" : string.Empty),
            OccurrenceVerdict.New => $"parser: {first}" + (others > 0 ? $" (+{others})" : string.Empty),
            OccurrenceVerdict.NoParse => "no parse",
            OccurrenceVerdict.Limit => Mark.Stopped.Word,
            _ when ParserRefusals.Of(result?.Morphology, result?.Outcome) is { } refused => refused.Reason,
            _ => "not in the last parse",
        };
        _modelRegistration = lease?.RegisterModel(SelectionModelKind.Token);
    }

    /// <summary>Releases this displayed token's registration when its range or card owner removes it.</summary>
    public void Dispose() => Interlocked.Exchange(ref _modelRegistration, null)?.Dispose();

    internal void OwnRegistration(IDisposable registration)
    {
        _modelRegistration?.Dispose();
        _modelRegistration = registration;
    }

    internal void BindActionFacts(SelectionWordActionFacts facts) => _actionFacts = facts;

    internal ResultsTokenViewModel CopyForCard() => new(string.Empty, 0, _source, _assessment,
        location: Location, occurrence: Occurrence, textId: _textId, expectedContext: _expectedContext,
        producingAssessmentId: _producingAssessmentId, actionFacts: _actionFacts,
        storedAnalysesAvailable: _storedAnalysesAvailable);

    internal bool HasUniqueActionTarget => _actionFacts is null || _actionFacts.CandidateWordformIds.Count == 1;

    internal bool AllowsMarkingAction(AnalysisMarkingActionKind kind, string? storedAnalysisId, ParseAnalysis? reading)
    {
        if (_actionFacts is null) return true;
        var exactStored = storedAnalysisId is not null && _actionFacts.StoredAnalysisIds.Any(id =>
            id == storedAnalysisId || Guid.TryParse(id, out var guid) && CanonicalId.FromGuid(guid).Value == storedAnalysisId);
        return kind switch
        {
            AnalysisMarkingActionKind.KeepFieldWorks => _actionFacts.CanMarkRead && Occurrence is not null,
            AnalysisMarkingActionKind.RemoveAnalysis => _actionFacts.CanRemoveAnalysis && exactStored,
            AnalysisMarkingActionKind.AcceptNewSet => _actionFacts.CanAcceptNewSet,
            _ when exactStored => _actionFacts.CanChangeOpinion,
            _ when reading is not null => _actionFacts.CanAddParserReading,
            _ => false,
        };
    }

    internal WordActionTarget CaptureActionTarget(string? fallbackAssessmentId) =>
        new(Form, WordformId, Occurrence, _expectedContext is null ? fallbackAssessmentId : _producingAssessmentId,
            _expectedContext)
        {
            ParserOnlyReadings = (_actionFacts?.Classification?.Readings ?? []).Where(reading => reading.IsParserOnly)
                .Select(reading => new WordActionReading(_assessment?.Morphology?.Analyses.ElementAtOrDefault(reading.Index),
                    null, reading.Index)).ToArray(),
        };

    private WordComparison BuildComparison()
    {
        if (_assessment is { } assessed && _expectedContext is null) return CompareSemantics.Compare(assessed);
        var contextAnalyses = _source.StoredAnalyses.Select(analysis => new ParserReading(analysis.Morphs)
        {
            StoredAnalysisId = analysis.StoredAnalysisId,
            StoredAnalysisOpinion = analysis.StoredAnalysisOpinion,
            Identity = analysis.Identity,
        }).ToArray();
        var standing = _source.IncorrectSpelling ? ProjectStanding.IncorrectSpelling :
            contextAnalyses.Any(analysis => analysis.StoredAnalysisOpinion == ReadingGrade.Approved) ? ProjectStanding.Approved :
            contextAnalyses.Any(analysis => (analysis.StoredAnalysisOpinion ?? ReadingGrade.Candidate) == ReadingGrade.Candidate)
                ? ProjectStanding.Candidate : contextAnalyses.Length > 0 ? ProjectStanding.Rejected : ProjectStanding.NotPresent;
        if (_assessment is { } captured) return CompareSemantics.Compare(new CompareWordFacts(standing,
            captured.Outcome, captured.IsIncomplete, captured.Morphology, null, 0)
        { StoredAnalyses = contextAnalyses, StoredAnalysesAvailable = _storedAnalysesAvailable });
        var comparisonWord = new AssessmentWordResult(Form, "unassessed", false, "Not parsed", null, null)
        {
            ProjectStanding = standing,
            StoredAnalyses = contextAnalyses,
            StoredAnalysesAvailable = true,
        };
        return CompareSemantics.Compare(comparisonWord);
    }

    private object? _stripTarget;
    public object StripTarget => _stripTarget ?? this;
    internal void BindStripTarget(object target)
    {
        _stripTarget = target;
        OnPropertyChanged(nameof(StripTarget));
    }

    public WordMarkingChoice? BoundPrimaryAction => Marking.PrimaryAction is { } action ? new WordMarkingChoice(
        CaptureActionTarget(Actions?.Changes.AssessmentId), new AnalysisMarkingChoice(action.Kind, action.Label,
            string.Empty, action.StoredAnalysisId, action.Reading, action.ReadingIndex, action.Now, action.AfterApply,
            action.ChangeKind), IsWord && AllowsMarkingAction(action.Kind, action.StoredAnalysisId, action.Reading)) : null;

    public string Text { get; }
    public string? TextWritingSystem { get; }

    /// <summary>The accessible name of the checkbox that includes this occurrence in bulk actions.</summary>
    public string SelectionAutomationName => $"Select {Form} for actions";

    /// <summary>The word's form as the Assessment names it, for finding it in the Words view.</summary>
    public string Form { get; }
    public string? FormWritingSystem { get; }
    public string? WordGlossWritingSystem { get; }
    public string? CategoryWritingSystem { get; }

    public string WordAutomationId => AutomationIdForPart("word");

    public string StripAutomationId => AutomationIdForPart("strip");

    public string OpinionMarkAutomationId => AutomationIdForPart("opinion");

    public string DisapprovedAutomationId => AutomationIdForPart("disapproved");

    public string FieldWorksAutomationId => AutomationIdForPart("fieldworks");

    public string PanGlossAutomationId => AutomationIdForPart("pangloss");

    public string PrimaryActionAutomationId => AutomationIdForPart("action");

    public string FixAutomationId => AutomationIdForPart("fix");

    /// <summary>The add button's identifier, for the PanGloss readings FieldWorks does not hold.</summary>
    public string AddAutomationId => AutomationIdForPart("add");

    public string StagedAutomationId => AutomationIdForPart("staged");

    public string UnreadAutomationId => AutomationIdForPart("unread");

    public bool IsWord { get; }

    private string AutomationIdForPart(string part) => _textId != Guid.Empty
        ? AutomationIds.ForWordPart(_textId, Form, OccurrenceIndex, part)
        : IsWord ? AutomationIds.ForPastedWordPart(Form, OccurrenceIndex, part) : string.Empty;

    /// <summary>The stable identity of this occurrence in the Analyze text.</summary>
    public WordPresentationKey PresentationKey { get; }

    /// <summary>The current open and action-selection state for this occurrence.</summary>
    public WordInteractionState PresentationState
    {
        get => _presentationState;
        set
        {
            if (value.Key != PresentationKey)
                throw new InvalidOperationException("Word interaction state belongs to another occurrence.");
            if (!SetProperty(ref _presentationState, value)) return;
            IsCardOpen = value.IsOpen;
            IsSelectedForActions = value.IsChecked == true;
        }
    }

    /// <summary>The immutable assessment evidence revision shown for this occurrence.</summary>
    public long EvidenceRevision => 0;

    public AnalysisMarkingState Marking { get; private set; }

    private WordDisposition? _disposition;

    /// <summary>The word's two buttons, what FieldWorks holds and the PanGloss readings to add, from <see cref="Marking"/>.</summary>
    public WordDisposition Disposition => _disposition ??= WordDisposition.From(Marking, BindMarkingChoice);

    /// <summary>The common word comparison, independent of this occurrence's action selection.</summary>
    public WordComparison Comparison => _comparison ??= BuildComparison();
    /// <summary>Every stored analysis with its opinion mark and interlinear morphemes.</summary>
    public IReadOnlyList<FieldWorksAnalysisDisplayViewModel> FieldWorksAnalyses => _fieldWorksAnalyses ??=
        Marking.FieldWorksAnalyses.Select(analysis => new FieldWorksAnalysisDisplayViewModel(analysis)).ToArray();

    /// <summary>The opinion mark used beside the word, or the dashed mark when nothing is stored.</summary>
    public OpinionMarkKind PrimaryOpinionMarkKind => FieldWorksAnalyses.Count == 0
        ? OpinionMarkKind.None : FieldWorksAnalyses[0].OpinionMarkKind;

    /// <summary>Whether FieldWorks stores at least one analysis for the word.</summary>
    public bool HasFieldWorksAnalyses => Marking.FieldWorksAnalyses.Count > 0;

    /// <summary>Whether PanGloss agrees with every stored reading.</summary>
    public bool IsPanGlossSame => Comparison.Outcome == WordRowOutcome.Same && Comparison.Tone != WordRowTone.Problem;

    /// <summary>Whether PanGloss conflicts with a stored reading or opinion.</summary>
    public bool IsPanGlossDifferent => Comparison.Outcome == WordRowOutcome.Different ||
        Comparison.Outcome == WordRowOutcome.Same && Comparison.Tone == WordRowTone.Problem;

    /// <summary>Whether PanGloss has readings beyond the approved FieldWorks readings.</summary>
    public bool IsPanGlossExtra => !IsPanGlossCapped && !HasOpinionConflict &&
        Comparison.Readings.Any(reading => reading.Matches.Any(match => match.Opinion == ReadingGrade.Approved) ||
            reading.RecordedGrade == ReadingGrade.Approved) &&
        (Comparison.ExtraReadingIndices.Count > 0 ||
            Comparison.Availability == AnalysisComparisonAvailability.RecordedGradesOnly &&
            Comparison.Readings.Any(reading => reading.RecordedGrade == ReadingGrade.NoOpinion));

    /// <summary>Whether PanGloss completed without finding a reading.</summary>
    public bool IsPanGlossNone => Comparison.Outcome == WordRowOutcome.NoParse;

    /// <summary>Whether PanGloss stopped before completing its search.</summary>
    public bool IsPanGlossCapped => Comparison.Outcome == WordRowOutcome.Stopped;

    /// <summary>The comparison meaning, using the same wording as the Matrix cell.</summary>
    public string PanGlossSummary => Comparison.Outcome == WordRowOutcome.NotParsed && _assessment is null
        ? "Not parsed yet"
        : Comparison.MeaningCode == "refused" ? ParserRefusals.Title
        : CompareSemantics.MeaningOf(Comparison.Standing, ColumnOf(Comparison.Outcome)).Label;

    public string PanGlossCardSummary => IsPanGlossNone
        ? $"∅ No parse · the grammar builds nothing for this word · {PanGlossSummary}"
        : PanGlossSummary;

    private bool HasOpinionConflict => Comparison.Standing == ProjectStanding.IncorrectSpelling ||
        HasDisapprovedReading || Comparison.MissingApprovedAnalyses.Count > 0 || Comparison.MissingApproved.Count > 0;

    /// <summary>Whether the strip marks the word Unread; a word nobody has parsed has nothing to read yet.</summary>
    public bool ShowUnread => Marking.IsUnread && _assessment is not null;

    /// <summary>Whether the word has a primary action available in its strip.</summary>
    public bool HasPrimaryAction => Marking.PrimaryAction is not null;

    /// <summary>The strip's one-click action, marked with a tick as Round 4 draws it.</summary>
    public string PrimaryActionLabel => Marking.PrimaryAction is { } action ? $"✓ {action.Label}" : string.Empty;

    /// <summary>The first stored analysis, which the strip's FieldWorks line shows; the card shows them all.</summary>
    public IReadOnlyList<ParserReadingMorphViewModel> PrimaryFieldWorksMorphs =>
        FieldWorksAnalyses.Count == 0 ? [] : FieldWorksAnalyses[0].Morphs;

    /// <summary>The first FieldWorks reading's morphemes, marked where a warning names their identities.</summary>
    public IReadOnlyList<ResultsStripMorphViewModel> WarningMarkedFieldWorksMorphs =>
        PrimaryFieldWorksMorphs.Select(morph => new ResultsStripMorphViewModel(morph,
            _namedMorphemeRefs.Any(reference =>
                (reference.AllomorphId is null || reference.AllomorphId == morph.AllomorphId) &&
                (reference.GrammaticalInfoId is null || reference.GrammaticalInfoId == morph.GrammaticalInfoId))))
            .ToArray();

    /// <summary>The primary FieldWorks form indices named by exact grammar-warning evidence.</summary>
    public IReadOnlyList<int> WarningMarkedFieldWorksMorphIndices => WarningMarkedFieldWorksMorphs
        .Select((morph, index) => (morph, index)).Where(item => item.morph.IsNamedInWarning)
        .Select(item => item.index).ToArray();

    /// <summary>The stored grammar findings whose exact identity evidence names this word.</summary>
    public IReadOnlyList<GrammarWarning> NamedWarnings { get; private set; } = [];
    /// <summary>The PanGloss message for each exact finding naming this word.</summary>
    public IReadOnlyList<NamedWarningCardViewModel> NamedWarningDetails { get; private set; } = [];
    /// <summary>Grammar findings that reach this word only by membership, kept as candidates.</summary>
    public IReadOnlyList<WarningCandidateViewModel> MembershipCandidateWarnings { get; private set; } = [];
    /// <summary>Grammar findings that reach this word only by spelling, kept as candidates.</summary>
    public IReadOnlyList<WarningCandidateViewModel> SpellingCandidateWarnings { get; private set; } = [];
    /// <summary>Other Selection words with a stored, non-disapproved use of the named morpheme.</summary>
    public IReadOnlyList<string> OtherWordsUsingMorpheme { get; private set; } = [];
    /// <summary>The stored per-word rule shares returned by Timing.</summary>
    public IReadOnlyList<TimingShareViewModel> TimingShares { get; private set; } = [];
    /// <summary>The stored share of this word's time that no object timer recorded.</summary>
    public string NotAttributedShareLabel { get; private set; } = "Timing unavailable for this word";
    /// <summary>The stored share of this word's time that no object timer recorded, when known.</summary>
    public double? NotAttributedShare { get; private set; }
    /// <summary>Whether a Timing response was read for this word.</summary>
    public bool HasTimingEvidence { get; private set; }
    /// <summary>Whether an exact grammar finding names this word.</summary>
    public bool HasNamedWarning => NamedWarnings.Count > 0;
    /// <summary>Whether the card has no exact finding naming this word.</summary>
    public bool HasNoNamedWarning => !HasNamedWarning;
    /// <summary>Whether exact or candidate warning evidence names this word.</summary>
    public bool HasWarningEvidence => HasNamedWarning || MembershipCandidateWarnings.Count > 0 ||
        SpellingCandidateWarnings.Count > 0;
    /// <summary>Whether either candidate list has an entry.</summary>
    public bool HasWarningCandidates => MembershipCandidateWarnings.Count > 0 || SpellingCandidateWarnings.Count > 0;
    public bool ShowsWarningCandidates => HasWarningCandidates && !HasNamedWarning;
    /// <summary>Whether a warning names this word through a membership candidate.</summary>
    public bool HasMembershipCandidateWarnings => MembershipCandidateWarnings.Count > 0;
    /// <summary>Whether a warning names this word through a spelling candidate.</summary>
    public bool HasSpellingCandidateWarnings => SpellingCandidateWarnings.Count > 0;
    /// <summary>Whether other words use the same named morpheme.</summary>
    public bool HasOtherWordsUsingMorpheme => OtherWordsUsingMorpheme.Count > 0;
    /// <summary>The other word forms that use the same named morpheme.</summary>
    public string OtherWordsUsingMorphemeLabel => string.Join(", ", OtherWordsUsingMorpheme);
    /// <summary>The heading for the other words reached through a named morpheme.</summary>
    public string OtherWordsUsingMorphemeHeading => WarningMorphemeNames() is [var name]
        ? $"Other words that use {name}" : "Other words that use the named morphemes";
    /// <summary>Whether the stored finding names a morpheme that appears in this word's reading.</summary>
    public bool HasNamedMorphemeNames => WarningMorphemeNames().Count > 0;
    /// <summary>The morpheme names stated by exact grammar warning evidence.</summary>
    public string NamedMorphemeSentence => WarningMorphemeNames() is { Count: > 0 } names
        ? $"{(names.Count == 1 ? "A grammar warning names" : "Grammar warnings name")} {string.Join(", ", names)}."
        : string.Empty;
    /// <summary>The word card's heading for warning evidence and possible matches.</summary>
    public string WarningSectionHeading => IsPanGlossNone
        ? "Why it might not parse" : "What a warning names in this word";
    /// <summary>Whether exact or candidate warning evidence, or this word's Assessment, supports the card section.</summary>
    public bool HasWhySection => HasWarningEvidence || _assessment is not null;
    /// <summary>Whether the Timing response included any rule shares for this word.</summary>
    public bool HasTimingShares => TimingShares.Count > 0;
    /// <summary>The warning mark for exact findings naming this word.</summary>
    public Mark WarningMark => Mark.Warning;

    public string WarningMarkCount => NamedWarnings.Count.ToString(CultureInfo.CurrentCulture);

    internal static ResultsWarningEvidence WarningEvidenceFor(string form, GrammarCheckResponse? check,
        IReadOnlyList<AssessmentWordResult> assessmentWords)
    {
        var findings = check?.Findings ?? [];
        var namedWarnings = findings.Where(finding => finding.YourWords is
            { Match: WarningWordsMatch.Identity } words && words.Words.Any(word => word.Row.Word == form)).ToArray();
        var namedWarningDetails = namedWarnings.Select(finding => new NamedWarningCardViewModel(finding.Description))
            .ToArray();
        var membershipCandidateWarnings = CandidateWarnings(findings, form, membership: true);
        var spellingCandidateWarnings = CandidateWarnings(findings, form, membership: false);

        var refs = namedWarnings.SelectMany(finding => finding.Subject)
            .Where(part => part.Reach is { Path: WarningWordsPath.Uses })
            .SelectMany(part => MorphemeRefs(part.Reach!))
            .DistinctBy(reference => (reference.AllomorphId, reference.GrammaticalInfoId)).ToArray();
        var otherWordsUsingMorpheme = refs.SelectMany(reference => ObjectUsesQuery.UsesOf(assessmentWords, reference).Words)
            .Select(word => word.Row.Word).Where(word => word != form)
            .Distinct(StringComparer.Ordinal).ToArray();

        return new ResultsWarningEvidence(namedWarnings, namedWarningDetails, membershipCandidateWarnings,
            spellingCandidateWarnings, refs, otherWordsUsingMorpheme);
    }

    internal void SetWarningEvidence(ResultsWarningEvidence evidence)
    {
        NamedWarnings = evidence.NamedWarnings;
        NamedWarningDetails = evidence.NamedWarningDetails;
        MembershipCandidateWarnings = evidence.MembershipCandidateWarnings;
        SpellingCandidateWarnings = evidence.SpellingCandidateWarnings;
        _namedMorphemeRefs = evidence.NamedMorphemeRefs;
        OtherWordsUsingMorpheme = evidence.OtherWordsUsingMorpheme;

        OnPropertyChanged(nameof(NamedWarnings));
        OnPropertyChanged(nameof(NamedWarningDetails));
        OnPropertyChanged(nameof(MembershipCandidateWarnings));
        OnPropertyChanged(nameof(SpellingCandidateWarnings));
        OnPropertyChanged(nameof(OtherWordsUsingMorpheme));
        OnPropertyChanged(nameof(HasNamedWarning));
        OnPropertyChanged(nameof(HasNoNamedWarning));
        OnPropertyChanged(nameof(HasWarningEvidence));
        OnPropertyChanged(nameof(HasWarningCandidates));
        OnPropertyChanged(nameof(ShowsWarningCandidates));
        OnPropertyChanged(nameof(HasMembershipCandidateWarnings));
        OnPropertyChanged(nameof(HasSpellingCandidateWarnings));
        OnPropertyChanged(nameof(HasOtherWordsUsingMorpheme));
        OnPropertyChanged(nameof(OtherWordsUsingMorphemeLabel));
        OnPropertyChanged(nameof(OtherWordsUsingMorphemeHeading));
        OnPropertyChanged(nameof(HasNamedMorphemeNames));
        OnPropertyChanged(nameof(NamedMorphemeSentence));
        OnPropertyChanged(nameof(WarningSectionHeading));
        OnPropertyChanged(nameof(HasWhySection));
        OnPropertyChanged(nameof(WarningMarkCount));
        OnPropertyChanged(nameof(WarningMarkedFieldWorksMorphs));
        OnPropertyChanged(nameof(WarningMarkedFieldWorksMorphIndices));
    }

    internal void SetTimingEvidence(TimingResponse? timing, bool responseAvailable)
    {
        HasTimingEvidence = responseAvailable;
        var timingShares = timing?.Aggregates
            .Where(row => row.ShareOfWordTime is > 0)
            .Select(row => new TimingShareViewModel(row.Name, row.ShareOfWordTime!.Value, row.SelfMs))
            .ToList() ?? [];
        if (timing is not null && timing.Attribution.WordTimeMs > 0)
        {
            var allRulesShare = timing.Attribution.AttributedMs / timing.Attribution.WordTimeMs;
            var otherRulesShare = allRulesShare - timingShares.Sum(row => row.Share);
            if (double.IsFinite(otherRulesShare) && otherRulesShare >= 0.0005)
                timingShares.Add(new TimingShareViewModel("Other rules", otherRulesShare,
                    Math.Max(0, timing.Attribution.AttributedMs - timingShares.Sum(row => row.Milliseconds))));
        }
        TimingShares = timingShares;
        NotAttributedShare = timing?.Attribution.NotAttributedShare;
        NotAttributedShareLabel = NotAttributedShare is { } share
            ? $"Not attributed · {share:P0}"
            : responseAvailable ? "Not attributed · no object time recorded" : "Timing unavailable for this word";
        OnPropertyChanged(nameof(HasTimingEvidence));
        OnPropertyChanged(nameof(TimingShares));
        OnPropertyChanged(nameof(NotAttributedShareLabel));
        OnPropertyChanged(nameof(NotAttributedShare));
        OnPropertyChanged(nameof(HasTimingShares));
    }

    private static IReadOnlyList<WarningCandidateViewModel> CandidateWarnings(
        IReadOnlyList<GrammarWarning> findings, string word, bool membership) => findings
        .Select(finding =>
        {
            var source = finding.YourWords;
            var candidates = source is null ? null : membership
                ? source.Match == WarningWordsMatch.Membership ? source.Words : source.MembershipCandidates
                : source.Match == WarningWordsMatch.Spelling ? source.Words : source.SpellingCandidates;
            var namesWord = source is { Match: WarningWordsMatch.Identity }
                ? source.Words.Any(candidate => candidate.Row.Word == word)
                : candidates?.Any(candidate => candidate.Row.Word == word) == true;
            return (Finding: finding, Candidates: candidates, NamesWord: namesWord);
        })
        .Where(item => item.NamesWord && item.Candidates is { Count: > 0 })
        .Select(item => new WarningCandidateViewModel(
            item.Candidates!.Select(candidate => candidate.Row.Word).Distinct(StringComparer.Ordinal).ToArray()))
        .ToArray();

    private static IEnumerable<ObjectUseRef> MorphemeRefs(WarningReach reach)
    {
        foreach (var allomorph in reach.AllomorphIds)
            yield return new ObjectUseRef { AllomorphId = allomorph };
        foreach (var grammaticalInfo in reach.GrammaticalInfoIds)
            yield return new ObjectUseRef { GrammaticalInfoId = grammaticalInfo };
    }

    private IReadOnlyList<string> WarningMorphemeNames() => NamedWarnings.SelectMany(finding => finding.Subject)
        .Where(part => part.Role == GrammarWarningPartRole.Object && part.Reach is { Path: WarningWordsPath.Uses })
        .Select(part => part.Text).Where(text => !string.IsNullOrWhiteSpace(text))
        .Distinct(StringComparer.Ordinal).ToArray();

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
    public Mark? PanGlossNoteMark => IsPanGlossCapped ? Mark.Stopped
        : IsPanGlossSame ? Mark.Same
        : IsPanGlossNone ? Mark.NoParse
        : null;

    public string PanGlossNote => IsPanGlossCapped
        ? Mark.Stopped.Word
        : IsPanGlossSame ? "same"
        : IsPanGlossNone ? Mark.NoParse.Word
        : PanGlossSummary;

    /// <summary>Whether a staged change replaces the strip's actions with its Staged note.</summary>
    public bool HasStagedChanges => StagedChanges.Count > 0;

    /// <summary>Whether the strip offers its actions, which it does until a change is staged for the word.</summary>
    public bool ShowsActions => !HasStagedChanges;

    /// <summary>Recorded comparison detail beneath the Matrix cell's shared meaning.</summary>
    public string ComparisonDetail => string.Join("; ", new[]
    {
        Comparison.Detail,
        Comparison.MeaningCode == "disapproved-rebuilt"
            ? "PanGloss matched an analysis FieldWorks marked Disapproved."
            : string.Empty,
    }.Where(detail => detail.Length > 0));

    public bool HasComparisonDetail => ComparisonDetail.Length > 0;

    private static CompareColumnKind ColumnOf(WordRowOutcome outcome) => outcome switch
    {
        WordRowOutcome.Same => CompareColumnKind.Match,
        WordRowOutcome.Different => CompareColumnKind.NoMatch,
        WordRowOutcome.NoParse => CompareColumnKind.NoParse,
        WordRowOutcome.Stopped => CompareColumnKind.Timeout,
        _ => CompareColumnKind.Skipped,
    };

    /// <summary>The read-only hover summary for the stored opinion and current PanGloss result.</summary>
    public string HoverSummary => $"{Form} · {FieldWorksSummary} · PanGloss: {PanGlossSummary}";

    private string FieldWorksSummary => FieldWorksAnalyses.Count == 0
        ? "No analysis in FieldWorks"
        : string.Join(", ", FieldWorksAnalyses.Select(analysis =>
            ReadingGradeLabels.Of(analysis.Opinion) is { Length: > 0 } label ? label : analysis.Opinion));
    public string Location { get; }
    public Uri? WordLink => _wordLink ??= _source.WordLink is { } link ? new Uri(link) : null;
    public OccurrenceAnchor? Occurrence { get; }
    public string? MarkUnreadDisabledReason => Occurrence is null
        ? "Choose a word occurrence in Analyze texts first."
        : null;
    public Guid? WordformId { get; }
    public int OccurrenceIndex { get; }
    public bool HasWordLink => WordLink is not null;
    public bool HasNoWordLink => IsWord && WordLink is null;
    public string WordLinkName => $"Open {Text} in {FieldWorksLinks.ToolNameOf(WordLink?.OriginalString)}";

    /// <summary>The text of the word's link into FieldWorks, naming the FieldWorks tool it opens.</summary>
    public string WordLinkText => $"{FieldWorksLinks.ToolNameOf(WordLink?.OriginalString)} ↗";

    /// <summary>The Analyze texts actions available to this word card.</summary>
    public ResultsInTextViewModel? Actions { get; internal set; }

    public IAsyncRelayCommand<WordChangeAction>? AddChangeForTokenCommand => Actions?.AddChangeForTargetCommand;

    public IRelayCommand<string>? TryWordForTokenCommand => Actions?.TryWordForTargetCommand;

    public IAsyncRelayCommand<WordMarkingChoice>? StageMarkingChoiceForTokenCommand => Actions?.StageMarkingChoiceForTargetCommand;

    /// <summary>The spelling action captured from this card rather than the reader's later selected token.</summary>
    public WordChangeAction IncorrectSpellingAction => new(CaptureActionTarget(Actions?.Changes.AssessmentId),
        ChangeKinds.IncorrectSpelling, [], IsWord && HasUniqueActionTarget);

    private IReadOnlyList<WordMarkingChoice>? _boundFixChoices;

    /// <summary>Fix choices that retain the target and producing evidence named when the menu is built.</summary>
    public IReadOnlyList<WordMarkingChoice> BoundFixChoices => _boundFixChoices ??=
        Marking.FixChoices.Select(BindMarkingChoice).ToArray();

    /// <summary>Captures an offered Fix or keyboard opinion choice for this word.</summary>
    public WordMarkingChoice BindMarkingChoice(AnalysisMarkingChoice choice) => new(
        CaptureActionTarget(Actions?.Changes.AssessmentId), choice,
        ResultsInTextViewModel.CanStageMarkingChoice(this, choice));

    /// <summary>The morphs of the analysis stored at this occurrence, each linked to its entry.</summary>
    public IReadOnlyList<ParserReadingMorphViewModel> Stored => _stored ??=
        (_source.Analysis?.Morphs ?? _source.StoredAnalyses.FirstOrDefault()?.Morphs)
            ?.Select(morph => new ParserReadingMorphViewModel(morph)).ToArray() ?? [];

    private IReadOnlyList<ProjectAnalysisViewModel>? _capturedApprovedAnalyses;
    private WordProjectStatus CapturedProjectStatus => _source.IncorrectSpelling ? WordProjectStatus.IncorrectSpelling :
        _source.StoredAnalyses.Any(analysis => analysis.StoredAnalysisOpinion == ReadingGrade.Approved) ? WordProjectStatus.Approved :
        _source.StoredAnalyses.Any(analysis => analysis.StoredAnalysisOpinion != ReadingGrade.Disapproved) ? WordProjectStatus.Candidate :
        _source.StoredAnalyses.Count > 0 ? WordProjectStatus.Rejected : WordProjectStatus.NotPresent;

    public string ProjectSummary => CapturedProjectStatus switch
    {
        WordProjectStatus.Approved => string.Join(", ", ProjectApprovedAnalyses.Select(analysis => analysis.Gloss).Distinct()),
        WordProjectStatus.Candidate => "Unknown: stored, but nobody has approved it",
        WordProjectStatus.Rejected => "Only disapproved analyses are stored",
        WordProjectStatus.IncorrectSpelling => "FieldWorks marks this spelling as incorrect",
        _ => "Not analysed in the project",
    };

    public string ProjectStatusLabel => WordProjectStatuses.LabelOf(CapturedProjectStatus,
        _source.StoredAnalyses.Count(analysis => analysis.StoredAnalysisOpinion == ReadingGrade.Approved));

    /// <summary>The opinion mark for what the project holds for this word; an incorrect spelling has none.</summary>
    public Mark? ProjectStatusMark => WordProjectStatuses.MarkOf(CapturedProjectStatus);

    public IReadOnlyList<ProjectAnalysisViewModel> ProjectApprovedAnalyses => (_capturedApprovedAnalyses ??= _source.StoredAnalyses
            .Where(analysis => analysis.StoredAnalysisOpinion == ReadingGrade.Approved)
            .Select(analysis => new ProjectAnalysisViewModel(analysis)).ToArray());

    public bool HasProjectApprovedAnalyses => _source.StoredAnalyses.Any(analysis => analysis.StoredAnalysisOpinion == ReadingGrade.Approved);

    public bool HasStored => (_source.Analysis?.Morphs ?? _source.StoredAnalyses.FirstOrDefault()?.Morphs)?.Count > 0;
    public bool HasNothingStored => IsWord && !HasFieldWorksAnalyses;

    internal bool HasAssessmentResult(AssessmentWordResult result) => ReferenceEquals(_assessment, result);

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
        _disposition = null;
        _boundFixChoices = null;
        OnPropertyChanged(nameof(BoundFixChoices));
        foreach (var name in new[]
                 {
                     nameof(Marking), nameof(ShowUnread), nameof(IsPanGlossSame), nameof(IsPanGlossDifferent),
                     nameof(IsPanGlossExtra), nameof(IsPanGlossNone), nameof(IsPanGlossCapped), nameof(PanGlossSummary),
                     nameof(PanGlossNote), nameof(PanGlossNoteMark), nameof(ShowsPanGlossReading), nameof(ShowsPanGlossNote),
                     nameof(HasMorePanGlossReadings), nameof(HasPrimaryAction), nameof(PrimaryActionLabel), nameof(BoundPrimaryAction),
                     nameof(HoverSummary), nameof(Disposition),
                 })
            OnPropertyChanged(name);
    }

    /// <summary>Pending changes that affect this word and can be undone from its strip.</summary>
    public IReadOnlyList<StagedMarkingDisplayViewModel> StagedChanges { get; private set; } = [];

    /// <summary>What PanGloss built for this occurrence against what FieldWorks stores here.</summary>
    public ParserOutcome Outcome => WindowWords.OutcomeOf(CompareSemantics.PlacementOf(Comparison).Column);

    /// <summary>Whether the parser also produced an analysis FieldWorks has disapproved for this word.</summary>
    public bool HasDisapprovedReading => Comparison.RebuiltDisapproved.Count > 0 ||
        Comparison.Readings.Any(reading => reading.RecordedGrade == ReadingGrade.Disapproved);

    /// <summary>The disapproved marker's mark: the Problem tone the Matrix gives a disapproved word PanGloss builds.</summary>
    public Mark DisapprovedMark => Mark.Of(DisapprovedMeaning.Tone);

    /// <summary>The disapproved marker's words, the Matrix's own for a disapproved word PanGloss builds.</summary>
    public string DisapprovedLabel => DisapprovedMeaning.Word;

    private static (string Word, MeaningTone Tone) DisapprovedMeaning =>
        WindowWords.MeaningOf(ProjectStanding.Rejected, ParserOutcome.Same);

    /// <summary>What the disapproved marker says when a reader stops on it.</summary>
    public string DisapprovedTip => $"The parser also produced an analysis FieldWorks has disapproved for {Text}.";

    /// <summary>Whether the active filter passes over this word, so it recedes rather than disappears.</summary>
    [ObservableProperty]
    private bool _isDimmed;

    [ObservableProperty]
    private bool _isSelectedForActions;

    partial void OnIsCardOpenChanged(bool value) => SyncPresentationState(state => state with { IsOpen = value });

    partial void OnIsSelectedForActionsChanged(bool value) =>
        SyncPresentationState(state => state with { IsChecked = value });

    private void SyncPresentationState(Func<WordInteractionState, WordInteractionState> update)
    {
        var next = update(_presentationState);
        if (next != _presentationState) SetProperty(ref _presentationState, next, nameof(PresentationState));
    }

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

}

/// <summary>One strip morpheme and whether its exact identity is named by a warning.</summary>
/// <param name="Morph">The existing morpheme display data.</param>
/// <param name="IsNamedInWarning">Whether the warning names this morpheme by identity.</param>
public sealed record ResultsStripMorphViewModel(ParserReadingMorphViewModel Morph, bool IsNamedInWarning)
{
    /// <summary>The morpheme's displayed form.</summary>
    public string Form => Morph.Form;
    public string? FormWritingSystem => Morph.FormWritingSystem;
    /// <summary>The morpheme's displayed gloss or placeholder.</summary>
    public string GlossOrPlaceholder => Morph.GlossOrPlaceholder;
    public string? GlossWritingSystem => Morph.GlossWritingSystem;
}

/// <summary>The PanGloss message shown for an exact finding in a word card.</summary>
/// <param name="Message">The warning description PanGloss supplied.</param>
public sealed record NamedWarningCardViewModel(string Message)
{
    /// <summary>Whether PanGloss supplied a message to show with the warning.</summary>
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
}

/// <summary>Words reached through one spelling or membership candidate group.</summary>
/// <param name="Words">The candidate word forms reached through this route.</param>
public sealed record WarningCandidateViewModel(IReadOnlyList<string> Words)
{
    /// <summary>The candidate word forms as a comma-separated label.</summary>
    public string WordsLabel => string.Join(", ", Words);
    /// <summary>The heading for words reached through members of a named resource.</summary>
    public string MembershipLabel => "Other words that use this resource";
    /// <summary>The heading for words matched by their spelling alone.</summary>
    public string SpellingLabel => "Matched by spelling only";
}

/// <summary>One stored rule's share of the selected word time.</summary>
/// <param name="Name">The rule name returned by Timing.</param>
/// <param name="Share">The rule's stored share of word time.</param>
/// <param name="Milliseconds">The rule's stored time in this word.</param>
public sealed record TimingShareViewModel(string Name, double Share, double Milliseconds)
{
    /// <summary>The stored share formatted as a percentage.</summary>
    public string ShareLabel => $"{Share.ToString("P0", System.Globalization.CultureInfo.CurrentCulture)} · {TimingShare.FormatDuration(Milliseconds)}";
}

internal sealed record ResultsWarningEvidence(
    IReadOnlyList<GrammarWarning> NamedWarnings,
    IReadOnlyList<NamedWarningCardViewModel> NamedWarningDetails,
    IReadOnlyList<WarningCandidateViewModel> MembershipCandidateWarnings,
    IReadOnlyList<WarningCandidateViewModel> SpellingCandidateWarnings,
    IReadOnlyList<ObjectUseRef> NamedMorphemeRefs,
    IReadOnlyList<string> OtherWordsUsingMorpheme);

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
