using SIL.Motif.Contract.Review;

namespace SIL.Motif.Worker.Runtime.Review;

/// <summary>Accumulates coverage and unique events as frozen cases arrive.</summary>
public sealed class ReviewSummaryAccumulator
{
    private readonly HashSet<string> _cases = new(StringComparer.Ordinal);
    private readonly HashSet<string> _noParse = new(StringComparer.Ordinal);
    private readonly HashSet<string> _approved = new(StringComparer.Ordinal);
    private readonly HashSet<string> _negative = new(StringComparer.Ordinal);
    private readonly HashSet<string> _alreadyAcceptedNegative = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unknownNegative = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ControlTotals> _controlTotals = new(StringComparer.Ordinal);
    private readonly HashSet<(string Set, string Case)> _negativeMemberships = [];
    private readonly HashSet<string> _negativeSets = new(StringComparer.Ordinal);
    private readonly HashSet<(string Word, string Reading)> _losses = [];
    private readonly HashSet<(string Word, string Reading)> _approvedReadings = [];
    private readonly HashSet<(string Word, string Reading)> _referenceReadings = [];
    private readonly Dictionary<(string Word, string Reading), (bool? Before, bool? After)> _approvedExpectations = [];
    private readonly HashSet<(string Set, string Case)> _controls = [], _negativeCases = [];
    private long _tokenPositions;
    private readonly HashSet<string> _incompleteWords = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Set, string Cell), (bool? Before, bool? After, bool BeforePartial, bool AfterPartial)> _cells = [];
    private readonly HashSet<(string Set, string Role)> _sets = [];
    private readonly Counter _wordBefore = new(), _wordAfter = new(), _tokenBefore = new(), _tokenAfter = new();
    private long _incompleteCases;
    private readonly HashSet<string> _incompleteBefore = new(StringComparer.Ordinal);
    private readonly HashSet<string> _incompleteAfter = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _beforeReasons = new(StringComparer.Ordinal), _afterReasons = new(StringComparer.Ordinal);
    private readonly HashSet<(string Set, string Cell)> _incompleteCells = [];
    private long _beforeNotMeasured, _beforeStopped, _afterNotMeasured, _afterStopped, _incompleteTokens;
    private string? _incompleteTokenProblem;
    private bool _approvedIdentityIncomplete;
    private WorkChange? _parserWork;
    private bool _workFlag;
    private ReviewFindingDelta? _findings;

    /// <summary>Starts an accumulator with the named sets captured for the Trial.</summary>
    public ReviewSummaryAccumulator(IEnumerable<ReviewSetBinding>? sets = null)
    {
        if (sets is null) return;
        foreach (var set in sets) _sets.Add((set.Id, set.Role));
    }

    /// <summary>Sets the same-scope work evidence and its exact threshold result.</summary>
    public void SetWork(ReviewWorkInputs inputs)
    {
        _parserWork = ReviewWorkComparison.Compare(inputs);
        _workFlag = ReviewWorkComparison.IsRedFlag(inputs);
    }

    /// <summary>Sets the independent warning and Parsimony comparison.</summary>
    public void SetFindings(ReviewFindingInputs inputs) => SetFindings(ReviewFindingComparison.Compare(inputs));

    /// <summary>Sets an already computed frozen finding comparison.</summary>
    public void SetFindings(ReviewFindingDelta findings) => _findings = findings ?? throw new ArgumentNullException(nameof(findings));

    /// <summary>Adds one case and its frozen memberships exactly once.</summary>
    public void Add(ReviewCaseDelta item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!_cases.Add(item.CaseKey)) return;
        _wordBefore.Add(ReviewCoverage.Observed(item.Before), 1, item.Before.Completion != Completion.Complete);
        _wordAfter.Add(ReviewCoverage.Observed(item.After), 1, item.After.Completion != Completion.Complete);
        if (item.NewNoParse) _noParse.Add(item.WordKey);
        if (item.NewApprovedLoss) _approved.Add(item.WordKey);
        if (item.NewlyAcceptedNegative)
        {
            _negative.Add(item.WordKey);
            var confirmed = item.Assertions.Where(assertion => assertion.Eligible && assertion.WholeWordNegative)
                .Select(assertion => assertion.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var member in item.Memberships.Where(member => member.Role == "negative" &&
                member.ExpectationIds.Any(confirmed.Contains)))
            {
                _negativeMemberships.Add((member.SetId, member.CaseId));
                _negativeSets.Add(member.SetId);
            }
        }
        if (item.Limitations.Contains("expectation-identity-unavailable", StringComparer.Ordinal) &&
            item.Assertions.Any(assertion => assertion.Eligible && assertion.Approved && !assertion.WholeWordNegative))
            _approvedIdentityIncomplete = true;
        foreach (var assertion in item.Assertions.Where(assertion => assertion.Eligible && assertion.Approved &&
            !assertion.WholeWordNegative))
        {
            if (assertion.Morphology is null || string.IsNullOrWhiteSpace(assertion.ReadingIdentity))
            {
                _approvedIdentityIncomplete = true;
                continue;
            }
            var key = (item.WordKey, assertion.ReadingIdentity);
            var before = Variant([assertion.Id], item.Before);
            var after = Variant([assertion.Id], item.After);
            _approvedExpectations[key] = _approvedExpectations.TryGetValue(key, out var old)
                ? (Merge(old.Before, before), Merge(old.After, after)) : (before, after);
        }
        foreach (var loss in item.ReadingLosses)
        {
            var key = (item.WordKey, loss.ReadingIdentity);
            _losses.Add(key);
            if (loss.Approved) _approvedReadings.Add(key);
            if (loss.ReferenceConfirmed) _referenceReadings.Add(key);
        }
        if (item.Before.Completion != Completion.Complete || item.After.Completion != Completion.Complete)
        {
            _incompleteCases++;
            _incompleteWords.Add(item.WordKey);
        }
        if (item.Before.Completion != Completion.Complete) _incompleteBefore.Add(item.CaseKey);
        if (item.After.Completion != Completion.Complete) _incompleteAfter.Add(item.CaseKey);
        AddSide(item.Before, ref _beforeNotMeasured, ref _beforeStopped, _beforeReasons);
        AddSide(item.After, ref _afterNotMeasured, ref _afterStopped, _afterReasons);
        foreach (var member in item.Memberships)
        {
            _sets.Add((member.SetId, member.Role));
            if (member.Role is "reference" or "regression")
            {
                _controls.Add((member.SetId, member.CaseId));
                AddControl(item, member);
            }
            if (member.Role == "negative")
            {
                _negativeCases.Add((member.SetId, member.CaseId));
                AddNegative(item, member);
            }
            if (member.Role == "sampled-tokens")
            {
                _tokenPositions++;
                if (item.Before.Completion != Completion.Complete || item.After.Completion != Completion.Complete)
                {
                    if (member.Weight <= 0) _incompleteTokenProblem = "invalid-weight";
                    else
                    {
                        try { _incompleteTokens = checked(_incompleteTokens + member.Weight); }
                        catch (OverflowException) { _incompleteTokenProblem = "count-overflow"; }
                    }
                }
                _tokenBefore.Add(ReviewCoverage.Observed(item.Before), member.Weight, item.Before.Completion != Completion.Complete);
                _tokenAfter.Add(ReviewCoverage.Observed(item.After), member.Weight, item.After.Completion != Completion.Complete);
            }
            if (member.Role != "paradigm-cells" || member.CellId is null) continue;
            var key = (member.SetId, member.CellId);
            if (item.Before.Completion != Completion.Complete || item.After.Completion != Completion.Complete)
                _incompleteCells.Add(key);
            var before = Variant(member.ExpectationIds, item.Before);
            var after = Variant(member.ExpectationIds, item.After);
            var beforePartial = item.Before.Completion != Completion.Complete;
            var afterPartial = item.After.Completion != Completion.Complete;
            _cells[key] = _cells.TryGetValue(key, out var old)
                ? (Merge(old.Before, before), Merge(old.After, after), old.BeforePartial || beforePartial, old.AfterPartial || afterPartial)
                : (before, after, beforePartial, afterPartial);
        }
    }

    /// <summary>Returns counts and rates without consulting live project state.</summary>
    public ReviewAccumulatedFacts Snapshot()
    {
        var cellBefore = ReviewCoverage.WithPartial(ReviewCoverage.Count(_cells.Values.Select(value => (value.Before, 1L))),
            _cells.Values.Any(value => value.BeforePartial));
        var cellAfter = ReviewCoverage.WithPartial(ReviewCoverage.Count(_cells.Values.Select(value => (value.After, 1L))),
            _cells.Values.Any(value => value.AfterPartial));
        var wordBefore = _wordBefore.Value();
        var wordAfter = _wordAfter.Value();
        var tokenBefore = _tokenBefore.Value();
        var tokenAfter = _tokenAfter.Value();
        var limitations = new HashSet<string>(StringComparer.Ordinal);
        if (_approvedIdentityIncomplete) limitations.Add("expectation-identity-unavailable");
        foreach (var coverage in new[] { wordBefore, wordAfter, tokenBefore, tokenAfter, cellBefore, cellAfter })
            foreach (var reason in coverage.ReasonCodes.Where(reason => reason is "count-overflow" or "invalid-weight"))
                limitations.Add(reason);
        if (_incompleteTokenProblem is not null) limitations.Add(_incompleteTokenProblem);
        if (_incompleteTokens > 9007199254740991L) limitations.Add("count-overflow");
        return new(wordBefore, wordAfter, tokenBefore, tokenAfter,
            cellBefore, cellAfter, _noParse.Union(_approved).Count(), _noParse.Count, _approved.Count,
            _noParse.Intersect(_approved).Count(), _negative.Count, _negativeMemberships.Count, _negativeSets.Count, _losses.Count, _approvedReadings.Count, _referenceReadings.Count,
            _approvedReadings.Intersect(_referenceReadings).Count(), _incompleteCases, _incompleteWords.Count,
            _incompleteBefore.Count, _incompleteAfter.Count, Side(_beforeNotMeasured, _beforeStopped, _beforeReasons),
            Side(_afterNotMeasured, _afterStopped, _afterReasons),
            _incompleteTokenProblem is not null || _incompleteTokens > 9007199254740991L ? null : _incompleteTokens,
            _incompleteCells.Count, _sets.Count)
        {
            ApprovedExpected = _approvedIdentityIncomplete ? null : _approvedExpectations.Count,
            ApprovedBeforeMatched = _approvedIdentityIncomplete ? null : _approvedExpectations.Values.Count(value => value.Before == true),
            ApprovedAfterMatched = _approvedIdentityIncomplete ? null : _approvedExpectations.Values.Count(value => value.After == true),
            ApprovedAfterUnknown = _approvedIdentityIncomplete ? null : _approvedExpectations.Values.Count(value => value.After is null),
            Limitations = limitations.Order(StringComparer.Ordinal).ToArray(),
            Controls = ControlSummaries(),
            Negatives = new NegativeSummary(_sets.LongCount(set => set.Role == "negative"), _negative.Count,
                _alreadyAcceptedNegative.Count, _unknownNegative.Count),
            ApprovedNewlyLost = _approvedReadings.Count,
            Flags = ObservedFlags(),
            ParserWork = _parserWork,
            Findings = _findings,
            SetCountsByRole = _sets.GroupBy(set => set.Role, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => (long)group.Count(), StringComparer.Ordinal),
            Sections =
            [
                new("wordforms", _cases.Count, "cases", null),
                new("sampled-tokens", _tokenPositions, "positions", null),
                new("paradigm-cells", _cells.Count, "cells", null),
                new("controls", _controls.Count, "memberships", null),
                new("negative-controls", _negativeCases.Count, "memberships", null),
                new("parser-work", _parserWork?.WordCount, "cases",
                    _parserWork?.Comparison == WorkComparison.Comparable ? _parserWork.WordCount : null),
                new("warnings", null, "diagnostics", null),
                new("parsimony", null, "findings", null),
                new("incomplete", _incompleteCases, "cases", null),
                new("effects", null, "changes", null),
                new("evidence", null, "items", null),
            ],
        };
    }

    private void AddNegative(ReviewCaseDelta item, ReviewMembership member)
    {
        var confirmed = item.Assertions.Any(assertion => assertion.Eligible && assertion.WholeWordNegative &&
            member.ExpectationIds.Contains(assertion.Id));
        var before = ReviewCoverage.Observed(item.Before);
        var after = ReviewCoverage.Observed(item.After);
        if (!confirmed || before is null || (before == false && after is null)) _unknownNegative.Add(item.WordKey);
        else if (before == true) _alreadyAcceptedNegative.Add(item.WordKey);
    }

    private void AddControl(ReviewCaseDelta item, ReviewMembership member)
    {
        if (!_controlTotals.TryGetValue(member.Role, out var totals))
            _controlTotals[member.Role] = totals = new ControlTotals();
        totals.Words.Add(item.WordKey);
        if (member.ExpectationIds.Count == 0) totals.Unresolved.Add((member.SetId, member.CaseId, ""));
        foreach (var id in member.ExpectationIds.Distinct(StringComparer.Ordinal))
        {
            var assertion = item.Assertions.FirstOrDefault(assertion => assertion.Id == id && assertion.Eligible &&
                !assertion.WholeWordNegative);
            if (assertion?.Morphology is null || string.IsNullOrWhiteSpace(assertion.ReadingIdentity) ||
                item.Limitations.Contains("expectation-identity-unavailable", StringComparer.Ordinal))
            {
                totals.Unresolved.Add((member.SetId, member.CaseId, id));
                continue;
            }
            var identity = (item.WordKey, assertion.ReadingIdentity);
            totals.Expectations.Add(identity);
            if (item.ReadingLosses.Any(loss => loss.ReadingIdentity == assertion.ReadingIdentity && loss.AssertionIds.Contains(id)))
                totals.Lost.Add(identity);
        }
    }

    private IReadOnlyList<ControlSummary> ControlSummaries() => _sets
        .Where(set => set.Role is "reference" or "regression").GroupBy(set => set.Role, StringComparer.Ordinal)
        .OrderBy(group => group.Key, StringComparer.Ordinal).Select(group =>
        {
            _controlTotals.TryGetValue(group.Key, out var totals);
            return new ControlSummary(group.Key, group.LongCount(), totals?.Words.Count ?? 0,
                totals is { Unresolved.Count: > 0 } ? null : totals?.Expectations.Count ?? 0,
                totals?.Lost.Count ?? 0, null, totals?.Unresolved.Count ?? 0);
        }).ToArray();

    private sealed class ControlTotals
    {
        public HashSet<string> Words { get; } = new(StringComparer.Ordinal);
        public HashSet<(string Word, string Reading)> Expectations { get; } = [];
        public HashSet<(string Word, string Reading)> Lost { get; } = [];
        public HashSet<(string Set, string Case, string Assertion)> Unresolved { get; } = [];
    }

    private IReadOnlyList<ReviewFlagId> ObservedFlags()
    {
        var flags = new List<ReviewFlagId>();
        if (_noParse.Count > 0 || _approved.Count > 0) flags.Add(ReviewFlagId.NewWordFailures);
        if (_losses.Count > 0) flags.Add(ReviewFlagId.ConfirmedReadingsLost);
        if (_negative.Count > 0) flags.Add(ReviewFlagId.KnownNonwordsNowAccepted);
        if (_workFlag) flags.Add(ReviewFlagId.ParserWorkIncrease);
        if (_findings is not null && (_findings.AddedWarnings.Count > 0 ||
            _findings.Measures.Any(measure => measure.Added.Count > 0))) flags.Add(ReviewFlagId.NewWarningsOrParsimony);
        return flags;
    }

    private static void AddSide(ReviewCaseSideFacts side, ref long notMeasured, ref long stopped,
        Dictionary<string, long> reasons)
    {
        if (side.Completion == Completion.NotMeasured) notMeasured++;
        if (side.Completion == Completion.Stopped) stopped++;
        foreach (var reason in side.StopReasons.Distinct(StringComparer.Ordinal))
            reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
    }

    private static IncompleteSide Side(long notMeasured, long stopped, Dictionary<string, long> reasons) =>
        new(notMeasured, stopped, reasons.OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new IncompleteReason(item.Key, item.Value)).ToArray());

    private static bool? Variant(IReadOnlyList<string> ids, ReviewCaseSideFacts side) =>
        ids.Count == 0 ? null : ids.Any(side.MatchedExpectationIds.Contains) ? true :
        side.Completion == Completion.Complete && ids.All(side.MissingExpectationIds.Contains) ? false : null;
    private static bool? Merge(bool? a, bool? b) => a == true || b == true ? true : a == false && b == false ? false : null;

    private sealed class Counter
    {
        private long _yes, _no, _unknown;
        private string? _invalid;
        private bool _partial;
        public void Add(bool? state, long weight, bool partial)
        {
            _partial |= partial;
            if (weight <= 0) { _invalid = "invalid-weight"; return; }
            try
            {
                if (state == true) _yes = checked(_yes + weight);
                else if (state == false) _no = checked(_no + weight);
                else _unknown = checked(_unknown + weight);
            }
            catch (OverflowException) { _invalid = "count-overflow"; }
        }
        public Coverage Value() => _invalid is not null ? new(null, null, null, null, null, CoveragePrecision.Unavailable, [_invalid]) :
            ReviewCoverage.WithPartial(ReviewCoverage.Count(new[] { ((bool?)true, _yes), ((bool?)false, _no), ((bool?)null, _unknown) }
                .Where(item => item.Item2 != 0)), _partial);
    }
}

/// <summary>Rates and event counts accumulated from one frozen case stream.</summary>
public sealed record ReviewAccumulatedFacts(Coverage WordBefore, Coverage WordAfter,
    Coverage TokensBefore, Coverage TokensAfter, Coverage CellsBefore, Coverage CellsAfter,
    long NewWordFailureWords, long NewNoParseWords, long NewApprovedLossWords, long OverlapWords,
    long NewlyAcceptedNegativeWords, long NewlyAcceptedNegativeMemberships, long NewlyAcceptedNegativeSetCount,
    long UniqueReadingLosses, long ApprovedReadingLosses, long ReferenceReadingLosses,
    long OverlapReadingLosses, long IncompleteCases, long IncompleteWords, long IncompleteBefore,
    long IncompleteAfter, IncompleteSide BeforeIncomplete, IncompleteSide AfterIncomplete,
    long? IncompleteTokenWeight, long IncompleteCellCount, long SetCount)
{
    /// <summary>Reference and regression totals, kept separate by role.</summary>
    public IReadOnlyList<ControlSummary> Controls { get; init; } = [];
    /// <summary>Confirmed and unresolved negative-set outcomes.</summary>
    public NegativeSummary Negatives { get; init; } = new(0, null, null, null);
    /// <summary>Comparison limitations discovered during accumulation.</summary>
    public IReadOnlyList<string> Limitations { get; init; } = [];
    /// <summary>Observed flag families in the report's fixed order.</summary>
    public IReadOnlyList<ReviewFlagId> Flags { get; init; } = [];
    /// <summary>Same-scope parser work, or null until it is supplied.</summary>
    public WorkChange? ParserWork { get; init; }
    /// <summary>Paired warning and Parsimony facts, or null until supplied.</summary>
    public ReviewFindingDelta? Findings { get; init; }
    /// <summary>Distinct frozen Approved reading identities.</summary>
    public long? ApprovedExpected { get; init; }
    /// <summary>Approved readings observed before the Proposal.</summary>
    public long? ApprovedBeforeMatched { get; init; }
    /// <summary>Approved readings observed after the Proposal.</summary>
    public long? ApprovedAfterMatched { get; init; }
    /// <summary>Approved readings whose after state remains unknown.</summary>
    public long? ApprovedAfterUnknown { get; init; }
    /// <summary>Approved readings observed before and absent from a complete after search.</summary>
    public long ApprovedNewlyLost { get; init; }
    /// <summary>Named set totals by role, including empty sets.</summary>
    public IReadOnlyDictionary<string, long> SetCountsByRole { get; init; } = new Dictionary<string, long>();
    /// <summary>Known section row counts and explicit unknowns for evidence not supplied here.</summary>
    public IReadOnlyList<SectionCount> Sections { get; init; } = [];
}
