using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Texts;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker.Baselines;

namespace SIL.Motif.Commands.SelectionReading;

/// <summary>The exact Text word row identified by source wordform, NFD form and writing system.</summary>
public readonly record struct TextWordKey(Guid? WordformId, string Form, string? WritingSystem);

/// <summary>Header facts retained for one Text without its line or token graph.</summary>
public sealed record SelectionTextSummary(
    Guid TextId,
    string Title,
    string? TitleWritingSystem,
    int LineCount,
    int DistinctFormCount,
    int OccurrenceCount,
    int MembershipCount)
{
    /// <summary>Source positions and token counts without sentence, analysis or token display detail.</summary>
    public IReadOnlyList<SelectionLineHeader> Lines { get; init; } = [];
}

/// <summary>The position and size of one captured source line.</summary>
public sealed record SelectionLineHeader(int Number, Guid ParagraphId, Guid SegmentId,
    bool ParseIsCurrent, int TokenCount);

/// <summary>One compact word row with its count and nearest captured locations.</summary>
public sealed record SelectionWordSummary(
    TextWordKey Key,
    string ProjectStanding,
    int OccurrenceCount,
    IReadOnlyDictionary<string, int> StatusCounts,
    int CandidateCount,
    bool IncorrectSpelling,
    bool IsAddedWord,
    SelectionOccurrenceLocation? FirstOccurrence,
    SelectionOccurrenceLocation? LastOccurrence)
{
    public SelectionAssessmentFacts? Assessment { get; init; }
    public SelectionWordActionFacts Actions { get; init; } = new(false, false, false, false, false, []);
    /// <summary>How many occurrences in the requested scope have a valid Read mark, when Read state was requested.</summary>
    public int? ReadOccurrenceCount { get; init; }
    /// <summary>How many occurrences in the requested scope lack a valid Read mark, when Read state was requested.</summary>
    public int? UnreadOccurrenceCount { get; init; }
    /// <summary>The approved stored analysis count for this row's exact wordform target.</summary>
    public int ApprovedAnalysisCount { get; init; }
    /// <summary>How many distinct analysis content keys its scoped occurrences actually choose.</summary>
    public int ChosenAnalysisCount { get; init; }
    /// <summary>Exact stored analyses selected by the row's scoped occurrences, without display morphology.</summary>
    public IReadOnlyList<Guid> ChosenAnalysisIds { get; init; } = [];
}

/// <summary>Identity-safe actions a caller may offer for one exact word row.</summary>
public sealed record SelectionWordActionFacts(
    bool CanMarkRead,
    bool CanChangeOpinion,
    bool CanRemoveAnalysis,
    bool CanAddParserReading,
    bool CanAcceptNewSet,
    IReadOnlyList<string> StoredAnalysisIds)
{
    public SelectionAnalysisClassification? Classification { get; init; }
    /// <summary>Exact compact wordform identities that can target actions for this spelling without guessing.</summary>
    public IReadOnlyList<Guid> CandidateWordformIds { get; init; } = [];
    /// <summary>Captured opinions indexed by exact stored analysis identity, without display morphology.</summary>
    public IReadOnlyDictionary<string, string> StoredOpinions { get; init; } = new Dictionary<string, string>();
    /// <summary>Whether the comparison offers an action beyond Read marking or removal, before Draft staging.</summary>
    public bool NeedsALook { get; init; }
}

/// <summary>A source location retained as compact identity and position facts.</summary>
public sealed record SelectionOccurrenceLocation(
    OccurrenceAnchor Anchor,
    int TextOrder,
    int LineNumber,
    int LineIndex,
    int TokenOffset,
    string Status,
    Guid? AnalysisId);

/// <summary>The compact physical source position and primary writing-system form used by an Analyze strip.</summary>
public sealed record SelectionSourcePosition(SelectionOccurrenceLocation Location, TextWordKey Word);

/// <summary>The Selection summary returned without constructing display lines or tokens.</summary>
public sealed record SelectionSummary(
    IReadOnlyList<SelectionTextSummary> Texts,
    IReadOnlyList<SelectionWordSummary> Words,
    int DistinctFormCount,
    int WordRowCount,
    int PhysicalOccurrenceCount,
    int MembershipCount,
    IReadOnlyDictionary<Guid, IReadOnlyList<int>> MatchingLineNumbers)
{
    /// <summary>Physical occurrences per normalized parser form, deduplicated across writing-system alternatives.</summary>
    public IReadOnlyDictionary<string, int> OccurrencesByForm { get; init; } = new Dictionary<string, int>();
    /// <summary>Complete row totals by captured Project standing, independent of spelling/search filters.</summary>
    public IReadOnlyDictionary<string, int> WordRowsByProjectStanding { get; init; } = new Dictionary<string, int>();
    /// <summary>Rows whose scoped occurrences choose more than one distinct analysis content key.</summary>
    public int SeveralAnalysisRowCount { get; init; }
    /// <summary>Physical word positions counted once, independent of alternative writing-system memberships.</summary>
    public IReadOnlyList<SelectionSourcePosition> SourcePositions { get; init; } = [];
    public SelectionAnalyzeFacts? Analyze { get; init; }
}

/// <summary>Filters summary rows while retaining totals for the complete Selection.</summary>
/// <param name="Search">Optional normalized spelling substring.</param>
/// <param name="ProjectStanding">Optional exact Project standing.</param>
/// <param name="TextIds">Optional Text scope for occurrence rows.</param>
/// <param name="IncludeAddedWords">Whether rows added to the Selection are included.</param>
/// <param name="ReadState">Optional filter for rows with Read or unread source occurrences.</param>
/// <param name="AnalysisClass">Optional parser-agreement classification filter.</param>
/// <param name="ComparisonAvailability">Optional analysis-identity comparison availability filter.</param>
/// <param name="Action">Optional filter for a reader-approved row action.</param>
public sealed record SelectionViewRequest(
    string? Search = null,
    string? ProjectStanding = null,
    IReadOnlySet<Guid>? TextIds = null,
    bool IncludeAddedWords = true,
    SelectionReadFilter? ReadState = null,
    SelectionAnalysisClass? AnalysisClass = null,
    SIL.Motif.Contract.Responses.AnalysisComparisonAvailability? ComparisonAvailability = null,
    SelectionActionFilter? Action = null);

/// <summary>Filters a Selection row by whether any of its scoped occurrences have a valid Read mark.</summary>
public enum SelectionReadFilter
{
    /// <summary>Include rows with at least one valid Read mark.</summary>
    Read,
    /// <summary>Include rows with at least one unmarked occurrence.</summary>
    Unread,
}

/// <summary>Filters rows by one identity-safe action the Selection reader permits.</summary>
public enum SelectionActionFilter
{
    /// <summary>Rows with source occurrences that can be marked Read.</summary>
    MarkRead,
    /// <summary>Rows with one exact target and a stored analysis opinion to change.</summary>
    ChangeOpinion,
    /// <summary>Rows with one exact target and a stored analysis to remove.</summary>
    RemoveAnalysis,
    /// <summary>Rows with one exact target and an unmatched parser reading that can be added.</summary>
    AddParserReading,
    /// <summary>Rows with one exact target and an eligible parser reading to accept as a new set.</summary>
    AcceptNewSet,
}

/// <summary>The exact word occurrence anchors and per-word capabilities in a requested action scope.</summary>
public sealed record SelectionActionFacts(IReadOnlyList<OccurrenceAnchor> Occurrences)
{
    /// <summary>Stored analyses actually chosen by the requested source occurrences.</summary>
    public IReadOnlyList<Guid> ChosenAnalysisIds { get; init; } = [];
    public IReadOnlyDictionary<TextWordKey, SelectionWordActionFacts> Words { get; init; } =
        new Dictionary<TextWordKey, SelectionWordActionFacts>();
}

internal sealed class SelectionIndexBuilder
{
    private readonly Dictionary<Guid, int> _textOrder = [];
    private readonly List<SelectionTextSummary> _texts = [];
    private readonly Dictionary<TextWordKey, WordEntry> _words = [];
    private readonly Dictionary<Guid, WordformFacts> _wordforms = [];
    private readonly Dictionary<string, HashSet<TextWordKey>> _candidateWords = new(StringComparer.Ordinal);
    private readonly HashSet<Guid> _textWordformIds = [];

    public int SemanticIdentityCount { get; private set; }
    private readonly Dictionary<string, SelectionAssessmentFacts> _assessmentWords = new(StringComparer.Ordinal);
    private readonly Dictionary<OccurrenceAnchor, SelectionOccurrenceLocation> _physical = [];
    private readonly Dictionary<OccurrenceAnchor, int> _physicalOrderIndex = [];
    private readonly List<SelectionOccurrenceLocation> _physicalOrder = [];
    private readonly Dictionary<OccurrenceAnchor, TextWordKey> _primaryWords = [];
    private IReadOnlyList<SelectionSourcePosition> _sourcePositions = [];
    private bool _sealed;

    public int PhysicalOccurrenceCount => _physical.Count;
    public int MembershipCount => _words.Values.Sum(word => word.Occurrences.Count);
    public int WordRowCount => _words.Count;

    public void Complete()
    {
        ThrowIfMutable();
        _sourcePositions = Array.AsReadOnly(_physicalOrder.Select(location =>
            new SelectionSourcePosition(location, _words[_primaryWords[location.Anchor]].Key)).ToArray());
        _primaryWords.Clear();
        _sealed = true;
    }

    public void AddText(BaselineTextReadIndex text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ThrowIfMutable();
        if (_textOrder.ContainsKey(text.TextId))
            throw new InvalidDataException("A Selection reader received the same Text row twice.");

        var order = _texts.Count;
        _textOrder.Add(text.TextId, order);
        var textForms = new HashSet<string>(StringComparer.Ordinal);
        var textPhysical = new HashSet<OccurrenceAnchor>();
        var textMemberships = 0;
        for (var lineIndex = 0; lineIndex < text.Lines.Count; lineIndex++)
        {
            var line = text.Lines[lineIndex];
            for (var tokenOffset = 0; tokenOffset < line.Tokens.Count; tokenOffset++)
            {
                var token = line.Tokens[tokenOffset];
                if (token.Status is null) continue;
                var forms = token.Forms.Select(form => (Form: Normalize(form.Text), form.WritingSystem))
                    .Where(form => form.Form.Length > 0).Distinct().ToArray();
                if (forms.Length == 0) continue;

                _textWordformIds.Add(token.WordformId!.Value);
                foreach (var form in forms)
                    AddCandidate(new TextWordKey(token.WordformId, form.Form, form.WritingSystem));

                var anchor = new OccurrenceAnchor(text.TextId, line.ParagraphId, line.SegmentId,
                    token.OccurrenceIndex);
                var location = new SelectionOccurrenceLocation(anchor, order, line.Number, lineIndex,
                    tokenOffset, token.Status, token.AnalysisId);
                textPhysical.Add(anchor);
                if (!_physical.ContainsKey(anchor))
                {
                    _physical.Add(anchor, location);
                    _primaryWords.Add(anchor, new TextWordKey(token.WordformId, forms[0].Form, forms[0].WritingSystem));
                    _physicalOrderIndex.Add(anchor, _physicalOrder.Count);
                    _physicalOrder.Add(location);
                }

                foreach (var form in forms)
                {
                    textForms.Add(form.Form);
                    var key = new TextWordKey(token.WordformId, form.Form, form.WritingSystem);
                    if (!_words.TryGetValue(key, out var word))
                    {
                        word = new WordEntry(key);
                        _words.Add(key, word);
                    }
                    if (!word.Occurrences.Add(anchor)) continue;
                    word.Locations.Add(location);
                    textMemberships++;
                }
            }
        }

        _texts.Add(new SelectionTextSummary(text.TextId, text.Title, text.TitleWritingSystem,
            text.Lines.Count, textForms.Count, textPhysical.Count, textMemberships)
        {
            Lines = Array.AsReadOnly(text.Lines.Select(line => new SelectionLineHeader(line.Number,
                line.ParagraphId, line.SegmentId, line.ParseIsCurrent, line.Tokens.Count)).ToArray()),
        });
    }

    public void AddWordform(BaselineTextReadWordform wordform)
    {
        ArgumentNullException.ThrowIfNull(wordform);
        ThrowIfMutable();
        SemanticIdentityCount += wordform.Analyses.Count;
        var facts = WordformFacts.Of(wordform);
        if (_wordforms.TryGetValue(wordform.WordformId, out var previous))
        {
            if (!previous.HasSameIdentity(facts))
                throw new InvalidDataException("A wordform has conflicting copies in the compact Text indexes.");
            return;
        }
        _wordforms.Add(wordform.WordformId, facts);
    }

    public void AddWordform(BaselineTextReadWordform wordform, IEnumerable<WritingSystemText> forms)
    {
        AddWordform(wordform);
        foreach (var form in forms)
            AddCandidate(new TextWordKey(wordform.WordformId, Normalize(form.Text), form.WritingSystem));
    }

    private void AddCandidate(TextWordKey key)
    {
        if (!_candidateWords.TryGetValue(key.Form, out var candidates))
            _candidateWords.Add(key.Form, candidates = []);
        candidates.Add(key);
    }

    public void AddWord(string form)
    {
        ThrowIfMutable();
        var normalized = Normalize(form);
        if (normalized.Length == 0) return;
        var key = new TextWordKey(null, normalized, null);
        if (!_words.ContainsKey(key)) _words.Add(key, new WordEntry(key) { IsAddedWord = true });
    }

    public void AddAssessmentWords(IReadOnlyList<AssessedWord> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        ThrowIfMutable();
        foreach (var word in words)
            _assessmentWords[Normalize(word.Word)] = new SelectionAssessmentFacts(
                word.Origin?.AssessmentId ?? string.Empty, word.Outcome, word.IsIncomplete,
                word.ProjectStanding, word.OccurrenceCount, word.ReadingGrades, word.Origin)
            {
                Morphology = word.Morphology,
                AnalysisComparison = word.AnalysisComparison,
                MissedApprovedCount = word.MissedApprovedCount ?? word.MissedApproved?.Count ?? 0,
                MissedApproved = word.MissedApproved,
            };
    }

    public IReadOnlyDictionary<string, SelectionAssessmentFacts> AssessmentFacts(IEnumerable<string> forms)
    {
        RequireSealed();
        return forms.Select(Normalize).Distinct(StringComparer.Ordinal).Where(_assessmentWords.ContainsKey)
            .ToDictionary(form => form, form => _assessmentWords[form], StringComparer.Ordinal);
    }

    public SelectionSummary Build(SelectionViewRequest? request = null,
        IReadOnlySet<OccurrenceAnchor>? readOccurrences = null)
    {
        if (!_sealed) Complete();
        request ??= new SelectionViewRequest();
        if (request.ReadState is not null && readOccurrences is null)
            throw new InvalidOperationException("Read-state filtering requires validated Read marks.");
        var rows = _words.Values.Select(word => Freeze(word, request.TextIds, readOccurrences))
            .OrderByDescending(word => word.OccurrenceCount)
            .ThenBy(word => word.Key.Form, StringComparer.Ordinal)
            .ThenBy(word => word.Key.WritingSystem, StringComparer.Ordinal)
            .ThenBy(word => word.Key.WordformId.HasValue ? 1 : 0)
            .ThenBy(word => word.Key.WordformId?.ToString("D"), StringComparer.Ordinal)
            .ToArray();
        var forms = _words.Keys.Select(key => key.Form).Distinct(StringComparer.Ordinal).Count();
        var result = rows.Where(word => Matches(word, request)).ToArray();
        var matchedKeys = result.Select(row => row.Key).ToHashSet();
        var lineNumbers = new Dictionary<Guid, SortedSet<int>>();
        foreach (var word in result)
        foreach (var location in _words[word.Key].Locations)
        {
            if (request.TextIds is not null && !request.TextIds.Contains(location.Anchor.TextId)) continue;
            if (!lineNumbers.TryGetValue(location.Anchor.TextId, out var numbers))
                lineNumbers.Add(location.Anchor.TextId, numbers = []);
            numbers.Add(location.LineNumber);
        }

        var texts = request.TextIds is null ? _texts : _texts.Where(text => request.TextIds.Contains(text.TextId)).ToList();
        var occurrencesByForm = _words.Values.GroupBy(word => word.Key.Form, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.SelectMany(word => word.Occurrences)
                .Where(anchor => request.TextIds is null || request.TextIds.Contains(anchor.TextId)).Distinct().Count(),
                StringComparer.Ordinal);
        var summary = new SelectionSummary(texts.ToArray(), result, forms, _words.Count,
            _physical.Count, _words.Values.Sum(word => word.Occurrences.Count),
            lineNumbers.ToDictionary(pair => pair.Key,
                pair => (IReadOnlyList<int>)pair.Value.ToArray()))
        {
            OccurrencesByForm = occurrencesByForm,
            WordRowsByProjectStanding = rows.GroupBy(row => row.ProjectStanding, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            SeveralAnalysisRowCount = rows.Count(row => row.ChosenAnalysisCount > 1),
            SourcePositions = Array.AsReadOnly(_sourcePositions.Where(position => matchedKeys.Contains(position.Word) &&
                (request.TextIds is null || request.TextIds.Contains(position.Location.Anchor.TextId)) &&
                (request.ReadState is null || readOccurrences!.Contains(position.Location.Anchor) ==
                    (request.ReadState == SelectionReadFilter.Read))).ToArray()),
        };
        return summary with { Analyze = SelectionAnalyzeProjection.Build(summary) };
    }

    public SelectionActionFacts ResolveActionScope(
        IReadOnlyCollection<TextWordKey> wordKeys,
        IReadOnlySet<Guid>? textIds = null,
        IReadOnlySet<OccurrenceAnchor>? excluded = null,
        IReadOnlySet<OccurrenceAnchor>? included = null)
    {
        ArgumentNullException.ThrowIfNull(wordKeys);
        RequireSealed();
        var anchors = new HashSet<OccurrenceAnchor>();
        foreach (var key in wordKeys)
            if (_words.TryGetValue(key, out var word))
                foreach (var anchor in word.Occurrences)
                    if ((textIds is null || textIds.Contains(anchor.TextId)) &&
                        (excluded is null || !excluded.Contains(anchor)) &&
                        (included is null || included.Contains(anchor)))
                        anchors.Add(anchor);

        return new SelectionActionFacts(anchors.Select(anchor => _physical[anchor])
            .OrderBy(location => location.TextOrder).ThenBy(location => location.LineIndex)
            .ThenBy(location => location.TokenOffset).Select(location => location.Anchor).ToArray())
        {
            ChosenAnalysisIds = anchors.Select(anchor => _physical[anchor].AnalysisId).OfType<Guid>().Distinct().ToArray(),
            Words = wordKeys.Distinct().Where(key => _words.TryGetValue(key, out var word) &&
                (word.Occurrences.Any(anchors.Contains) || word.IsAddedWord && textIds is null && included is null))
                .ToDictionary(key => key,
                key => Freeze(_words[key], null, null).Actions),
        };
    }

    public SelectionOccurrenceLocation? Adjacent(OccurrenceAnchor anchor, int offset)
    {
        RequireSealed();
        var index = _physicalOrderIndex.GetValueOrDefault(anchor, -1);
        var target = index + offset;
        return index < 0 || target < 0 || target >= _physicalOrder.Count ? null : _physicalOrder[target];
    }

    public SelectionOccurrenceLocation? Occurrence(OccurrenceAnchor anchor)
    {
        RequireSealed();
        return _physical.GetValueOrDefault(anchor);
    }

    public void ValidateWordformDetail(Guid wordformId, TextWordsProjectedWordform detail)
    {
        RequireSealed();
        if (!_wordforms.TryGetValue(wordformId, out var compact))
            throw new InvalidDataException("A Text detail wordform is absent from its compact index.");
        var projected = new BaselineTextReadWordform(wordformId, detail.Approved.Count,
            detail.CandidateCount, detail.Disapproved.Count, detail.IncorrectSpelling,
            detail.Analyses.Select(analysis => new BaselineTextReadAnalysis(analysis.Key,
                analysis.AnalysisId, analysis.Opinion, analysis.Identity ??
                throw new InvalidDataException("A Text detail analysis has no semantic identity."))).ToArray());
        if (!compact.HasSameIdentity(WordformFacts.Of(projected)))
            throw new InvalidDataException("A Text detail wordform disagrees with its compact index.");
    }

    public IReadOnlyList<SelectionOccurrenceLocation> Occurrences(TextWordKey key)
    {
        RequireSealed();
        return _words.TryGetValue(key, out var word)
            ? word.Locations.OrderBy(location => location.TextOrder)
                .ThenBy(location => location.LineIndex).ThenBy(location => location.TokenOffset).ToArray()
            : [];
    }

    public bool HasTextWordform(Guid id)
    {
        RequireSealed();
        return _textWordformIds.Contains(id);
    }

    public IReadOnlyList<TextWordKey> WordformCandidates(string form)
    {
        RequireSealed();
        var normalized = Normalize(form);
        return _candidateWords.TryGetValue(normalized, out var candidates)
            ? candidates.OrderBy(key => key.WordformId).ThenBy(key => key.WritingSystem, StringComparer.Ordinal).ToArray()
            : [];
    }

    public SelectionWordSummary? WordSummary(TextWordKey key)
    {
        RequireSealed();
        return _words.TryGetValue(key, out var word) ? Freeze(word, null, null) : null;
    }

    public SelectionTextSummary? Text(Guid textId)
    {
        RequireSealed();
        return _texts.FirstOrDefault(text => text.TextId == textId);
    }

    private SelectionWordSummary Freeze(WordEntry word, IReadOnlySet<Guid>? textIds,
        IReadOnlySet<OccurrenceAnchor>? readOccurrences)
    {
        var facts = word.Key.WordformId is { } id && _wordforms.TryGetValue(id, out var value) ? value : null;
        var candidateIds = word.Key.WordformId is { } exactId
            ? [exactId]
            : (_candidateWords.GetValueOrDefault(word.Key.Form) ?? [])
                .Select(key => key.WordformId!.Value).Distinct().Order().ToArray();
        var targetFacts = facts;
        if (targetFacts is null && candidateIds.Length == 1)
            _wordforms.TryGetValue(candidateIds[0], out targetFacts);
        var locations = word.Locations.Where(location => textIds is null || textIds.Contains(location.Anchor.TextId))
            .OrderBy(location => location.TextOrder)
            .ThenBy(location => location.LineIndex).ThenBy(location => location.TokenOffset).ToArray();
        var statusCounts = locations.GroupBy(location => location.Status, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var recordedAssessment = _assessmentWords.GetValueOrDefault(word.Key.Form);
        var assessment = recordedAssessment is null ? null : recordedAssessment with
        {
            Comparison = Compare(recordedAssessment, targetFacts),
        };
        var comparison = assessment?.Comparison;
        var analysisIds = targetFacts?.Analyses.Select(analysis => analysis.AnalysisId.ToString("D")).ToArray() ?? [];
        var storedReadings = targetFacts?.Analyses.Select(analysis => new ParserReading([])
        {
            StoredAnalysisId = analysis.AnalysisId.ToString("D"),
            StoredAnalysisOpinion = analysis.Opinion,
            Identity = analysis.Identity,
        }).ToArray() ?? [];
        var classification = SelectionAnalysisRules.Build(
            targetFacts?.IncorrectSpelling ?? false, assessment?.Outcome, assessment?.IsIncomplete ?? false,
            assessment?.Morphology, storedReadings);
        var opinions = storedReadings.ToDictionary(reading => reading.StoredAnalysisId!, reading =>
            SelectionAnalysisRules.NormalizeOpinion(reading.StoredAnalysisOpinion), StringComparer.Ordinal);
        var canUseParserReading = classification is { Class: not (SelectionAnalysisClass.Capped or
            SelectionAnalysisClass.Refused or SelectionAnalysisClass.NotAssessed) } &&
            classification.Readings.Any(reading => reading.IsParserOnly);
        var hasUnambiguousTarget = candidateIds.Length == 1;
        var actions = new SelectionWordActionFacts(locations.Length > 0,
            hasUnambiguousTarget && analysisIds.Length > 0,
            hasUnambiguousTarget && analysisIds.Length > 0,
            hasUnambiguousTarget && canUseParserReading,
            hasUnambiguousTarget && canUseParserReading &&
                targetFacts?.Analyses.Any(analysis => analysis.Opinion == ReadingGrade.Approved) != true,
            analysisIds)
        {
            Classification = classification,
            CandidateWordformIds = candidateIds,
            StoredOpinions = opinions,
            NeedsALook = SelectionAnalysisRules.NeedsALook(classification, opinions),
        };
        var readCount = readOccurrences is null ? (int?)null : locations.Count(location =>
            readOccurrences.Contains(location.Anchor));
        var chosenIds = locations.Where(location => location.AnalysisId is not null)
            .Select(location => location.AnalysisId!.Value).ToHashSet();
        return new SelectionWordSummary(word.Key, facts?.Standing ?? targetFacts?.Standing ??
            assessment?.ProjectStanding ?? ProjectStanding.NotPresent,
            locations.Length, statusCounts,
            targetFacts?.CandidateCount ?? 0, targetFacts?.IncorrectSpelling ?? false, word.IsAddedWord,
            locations.FirstOrDefault(), locations.LastOrDefault())
        {
            Assessment = assessment,
            Actions = actions,
            ReadOccurrenceCount = readCount,
            UnreadOccurrenceCount = readOccurrences is null ? null : locations.Length - readCount,
            ApprovedAnalysisCount = targetFacts?.Analyses.Count(analysis => analysis.Opinion == ReadingGrade.Approved) ?? 0,
            ChosenAnalysisIds = Array.AsReadOnly(chosenIds.ToArray()),
            ChosenAnalysisCount = targetFacts?.Analyses.Where(analysis => chosenIds.Contains(analysis.AnalysisId))
                .Select(analysis => analysis.Key).Distinct(StringComparer.Ordinal).Count() ?? 0,
        };
    }

    private static WordComparison? Compare(SelectionAssessmentFacts assessment, WordformFacts? facts)
    {
        var stored = facts?.Analyses.Select(analysis => new ParserReading([])
        {
            StoredAnalysisId = analysis.AnalysisId.ToString("D"),
            StoredAnalysisOpinion = analysis.Opinion,
            Identity = analysis.Identity,
        }).ToArray() ?? [];
        var comparison = new CompareWordFacts(facts?.Standing ?? assessment.ProjectStanding,
            assessment.Outcome, assessment.IsIncomplete, assessment.Morphology,
            facts is null ? assessment.ReadingGrades : null,
            assessment.MissedApprovedCount)
        {
            StoredAnalyses = facts is null ? null : stored,
            StoredAnalysesAvailable = facts is not null,
            AnalysisComparison = facts is null ? assessment.AnalysisComparison : null,
            MissedApproved = facts is null ? assessment.MissedApproved : null,
        };
        return CompareSemantics.Compare(comparison);
    }

    private static bool Matches(SelectionWordSummary word, SelectionViewRequest request) =>
        (string.IsNullOrWhiteSpace(request.Search) || word.Key.Form.Contains(
            request.Search.Trim().Normalize(System.Text.NormalizationForm.FormD), StringComparison.OrdinalIgnoreCase)) &&
        (request.ProjectStanding is null || word.ProjectStanding == request.ProjectStanding) &&
        (request.TextIds is null || word.IsAddedWord || word.OccurrenceCount > 0) &&
        (request.IncludeAddedWords || !word.IsAddedWord) &&
        MatchesReadState(word, request.ReadState) &&
        (request.AnalysisClass is null || word.Actions.Classification?.Class == request.AnalysisClass) &&
        (request.ComparisonAvailability is null ||
            word.Assessment?.Comparison?.Availability == request.ComparisonAvailability) &&
        (request.Action is null || HasAction(word.Actions, request.Action.Value));

    private static bool MatchesReadState(SelectionWordSummary word, SelectionReadFilter? readState) => readState switch
    {
        null => true,
        SelectionReadFilter.Read => word.ReadOccurrenceCount > 0,
        SelectionReadFilter.Unread => word.UnreadOccurrenceCount > 0,
        _ => false,
    };

    private static bool HasAction(SelectionWordActionFacts actions, SelectionActionFilter filter) => filter switch
    {
        SelectionActionFilter.MarkRead => actions.CanMarkRead,
        SelectionActionFilter.ChangeOpinion => actions.CanChangeOpinion,
        SelectionActionFilter.RemoveAnalysis => actions.CanRemoveAnalysis,
        SelectionActionFilter.AddParserReading => actions.CanAddParserReading,
        SelectionActionFilter.AcceptNewSet => actions.CanAcceptNewSet,
        _ => false,
    };

    private void ThrowIfMutable()
    {
        if (_sealed) throw new InvalidOperationException("A completed Selection index cannot change.");
    }

    private void RequireSealed()
    {
        if (!_sealed) throw new InvalidOperationException("A Selection index must be completed before it is read.");
    }

    private static string Normalize(string value) => (value ?? string.Empty).Trim()
        .Normalize(System.Text.NormalizationForm.FormD);

    private sealed class WordEntry(TextWordKey key)
    {
        public TextWordKey Key { get; } = key;
        public HashSet<OccurrenceAnchor> Occurrences { get; } = [];
        public List<SelectionOccurrenceLocation> Locations { get; } = [];
        public bool IsAddedWord { get; init; }
    }

    private static readonly System.Text.Json.JsonSerializerOptions IdentityJson = MotifJson.CreateOptions();

    private sealed record WordformFacts(string Standing, int CandidateCount, bool IncorrectSpelling,
        IReadOnlyList<BaselineTextReadAnalysis> Analyses)
    {
        public static WordformFacts Of(BaselineTextReadWordform wordform) => new(
            ProjectStandings.Of(wordform.ApprovedCount, wordform.CandidateCount,
                wordform.DisapprovedCount, wordform.IncorrectSpelling), wordform.CandidateCount,
            wordform.IncorrectSpelling, wordform.Analyses);

        public bool HasSameIdentity(WordformFacts other) => Standing == other.Standing &&
            CandidateCount == other.CandidateCount && IncorrectSpelling == other.IncorrectSpelling &&
            Analyses.Count == other.Analyses.Count && Analyses.Zip(other.Analyses).All(pair =>
                pair.First.Key == pair.Second.Key && pair.First.AnalysisId == pair.Second.AnalysisId &&
                pair.First.Opinion == pair.Second.Opinion && SameIdentity(pair.First.Identity, pair.Second.Identity));

        private static bool SameIdentity(ApprovedMorphology left, ApprovedMorphology right) =>
            System.Text.Json.JsonSerializer.Serialize(left, IdentityJson) ==
            System.Text.Json.JsonSerializer.Serialize(right, IdentityJson);
    }
}
