using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Queries;

/// <summary>Pure aggregations over stored Assessment timing facts.</summary>
/// <remarks>
/// Every share divides by the total word time of the same words: a group's object time and the words' parse
/// times are each added up before dividing, never averaged as per-word percentages. Object time recorded in a word
/// that has no parse time is left out, so the numerator never covers words the denominator does not.
/// </remarks>
public static class TimingAggregation
{
    /// <summary>Builds median, nearest-rank 95th percentile, and the requested slowest words.</summary>
    public static OverviewTiming SummarizeWords(IEnumerable<AssessedWord> words, int top = 3)
    {
        ArgumentNullException.ThrowIfNull(words);
        if (top < 0) throw new ArgumentOutOfRangeException(nameof(top));
        var materialized = words.ToArray();
        var timed = materialized.Select(word => (Word: word, TimeMs: WordTimeMs(word)))
            .Where(item => item.TimeMs is not null).ToArray();
        var durations = timed.Select(item => item.TimeMs!.Value).Order().ToArray();
        var median = durations.Length switch
        {
            0 => (double?)null,
            var length when length % 2 == 1 => durations[length / 2],
            var length => (durations[length / 2 - 1] + durations[length / 2]) / 2d,
        };
        double? percentile95 = durations.Length == 0
            ? null
            : durations[Math.Max(0, (int)Math.Ceiling(durations.Length * 0.95) - 1)];
        var slowest = SelectSlowestWords(materialized, top)
            .Select(word => new SlowWordTiming(word.Word, WordTimeMs(word)!.Value)).ToArray();
        var stepLimited = materialized.Count(word => word.Outcome == "capped" || word.Morphology?.Capped == true);
        return new OverviewTiming(median, percentile95, slowest, stepLimited) { MeasuredWordCount = durations.Length };
    }

    /// <summary>Returns measured words ordered by exact parse time, then by spelling for equal times.</summary>
    public static IReadOnlyList<AssessedWord> SelectSlowestWords(IEnumerable<AssessedWord> words, int top)
    {
        ArgumentNullException.ThrowIfNull(words);
        if (top < 0) throw new ArgumentOutOfRangeException(nameof(top));
        return words.Select(word => (Word: word, TimeMs: WordTimeMs(word)))
            .Where(item => item.TimeMs is not null)
            .OrderByDescending(item => item.TimeMs)
            .ThenBy(item => item.Word.Word, StringComparer.Ordinal)
            .Take(top).Select(item => item.Word).ToArray();
    }

    /// <summary>Whether a positive count or duration proves that an object did work in one word.</summary>
    public static bool HasExecutionEvidence(int? attempts, long? elapsedNs) => attempts is > 0 || elapsedNs is > 0;

    /// <summary>Sums the counts present and marks the projection partial when any row has no count.</summary>
    public static (long? Calls, bool IsPartial) ProjectCalls(IEnumerable<int?> attempts)
    {
        ArgumentNullException.ThrowIfNull(attempts);
        var rows = attempts.ToArray();
        var recorded = rows.Where(value => value is not null).Select(value => (long)value!.Value).ToArray();
        return (recorded.Length == 0 ? null : recorded.Sum(), recorded.Length > 0 && recorded.Length < rows.Length);
    }

    /// <summary>Combines per-word counts without losing whether any word's count is missing or partial.</summary>
    public static (long? Calls, bool IsPartial) CombineCalls(IEnumerable<(long? Calls, bool IsPartial)> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        var rows = counts.ToArray();
        var recorded = rows.Where(row => row.Calls is not null).ToArray();
        return (recorded.Length == 0 ? null : recorded.Sum(row => row.Calls!.Value),
            recorded.Length > 0 && (recorded.Length < rows.Length || recorded.Any(row => row.IsPartial)));
    }

    /// <summary>Adds the split of the words' total word time by kind, and its residual, to the word summary.</summary>
    public static OverviewTiming SummarizeWords(IReadOnlyList<AssessedWord> words,
        IReadOnlyList<AssessmentObjectTiming> objectTimings, int top = 3)
    {
        var byKind = Aggregate(words, objectTimings, "kind", rule: null, top: 1);
        return SummarizeWords(words, top) with { Kinds = byKind.Aggregates, Attribution = byKind.Attribution };
    }

    /// <summary>The stored row's typed address, retaining its query-derived local scope.</summary>
    public static TraceTimingKey KeyOf(AssessmentObjectTiming row) =>
        new(row.Kind, ObjectIdentity.CanonicalKey(row.Key, row.IdentityQuality)!)
            { IdentityQuality = row.IdentityQuality, Scope = row.Scope };

    /// <summary>
    /// Groups object timing rows by kind or by object identity, shares each group's time of the words' total word
    /// time, and finds the words where the object keyed <paramref name="rule"/> took longest.
    /// </summary>
    public static (IReadOnlyList<TimingAggregateRow> Aggregates, IReadOnlyList<WordRuleTiming> CostliestWords,
        WordTimeAttribution Attribution) Aggregate(IReadOnlyList<AssessedWord> words,
        IReadOnlyList<AssessmentObjectTiming> rows, string by, TraceTimingKey? rule, int top)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(rows);
        if (by is not ("kind" or "rule")) throw new ArgumentException("Grouping must be 'kind' or 'rule'.", nameof(by));
        if (top <= 0) throw new ArgumentOutOfRangeException(nameof(top));
        var wordTimes = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var word in words)
            if (WordTimeNs(word) is { } time) wordTimes.TryAdd(word.Word, time);
        var measured = rows.Where(row => wordTimes.ContainsKey(row.Word) &&
            HasExecutionEvidence(row.Attempts, row.ElapsedNs)).ToArray();
        var attribution = Attribute(wordTimes, measured);
        double? ShareOf(double selfMs) => attribution.WordTimeMs > 0 ? selfMs / attribution.WordTimeMs : null;

        var aggregates = measured.Select((row, index) => (Row: row, Identity: by == "kind"
                ? ObjectIdentity.Create(row.Kind, row.Kind, "structural") : KeyOf(row).Identity, Index: index))
            .GroupBy(item => (item.Identity, Unresolved: item.Identity is null ? item.Index : -1))
            .Select(group => (Rows: group.Select(item => item.Row).ToArray(), Identity: group.Key.Identity))
            .Where(group => group.Rows.Any(row => row.ElapsedNs is not null))
            .Select(group =>
            {
                var self = group.Rows.Sum(row => row.ElapsedNs ?? 0) / 1_000_000d;
                var calls = ProjectCalls(group.Rows.Select(row => row.Attempts));
                var first = group.Rows[0];
                return new TimingAggregateRow(by == "kind" ? first.Kind : KeyOf(first).Key, by == "kind" ? first.Kind : first.Object, self,
                    ShareOf(self), group.Rows.Select(row => row.Word).Distinct(StringComparer.Ordinal).Count())
                {
                    Kind = first.Kind,
                    IdentityQuality = by == "kind" ? string.Empty : first.IdentityQuality,
                    Scope = by == "kind" ? null : first.Scope,
                    Calls = calls.Calls,
                    CallsArePartial = calls.IsPartial,
                };
            })
            .OrderByDescending(row => row.SelfMs).ThenBy(row => row.Name, StringComparer.Ordinal)
            .ThenBy(row => row.Key, StringComparer.Ordinal).ToArray();
        var costliest = rule is null
            ? Array.Empty<WordRuleTiming>()
            : measured.Where(row => ObjectIdentity.Same(KeyOf(row).Identity, rule.Identity))
                .GroupBy(row => row.Word, StringComparer.Ordinal)
                .Select(group => (Word: group.Key, Rows: group.ToArray()))
                .Where(group => group.Rows.Any(row => row.ElapsedNs is not null))
                .Select(group =>
                {
                    var self = group.Rows.Sum(row => row.ElapsedNs ?? 0) / 1_000_000d;
                    var whole = wordTimes[group.Word] / 1_000_000d;
                    var calls = ProjectCalls(group.Rows.Select(row => row.Attempts));
                    return new WordRuleTiming(group.Word, self, calls.Calls)
                    {
                        WordTimeMs = whole,
                        ShareOfWordTime = whole > 0 ? self / whole : null,
                        CallsArePartial = calls.IsPartial,
                    };
                })
                .OrderByDescending(row => row.SelfMs).ThenBy(row => row.Word, StringComparer.Ordinal)
                .Take(top).ToArray();
        return (aggregates, costliest, attribution);
    }

    /// <summary>
    /// A word's parse time in milliseconds: the statistics cache's nanoseconds when it recorded them, since the
    /// per-word millisecond is rounded and a fast word's object time could exceed it; otherwise the millisecond.
    /// </summary>
    public static double? WordTimeMs(AssessedWord word)
    {
        ArgumentNullException.ThrowIfNull(word);
        return WordTimeMs(word.ElapsedNs, word.ElapsedMs);
    }

    /// <summary>Returns a stored Timing row's exact parse time, or null when neither duration was recorded.</summary>
    public static double? WordTimeMs(TimingWordRow word)
    {
        ArgumentNullException.ThrowIfNull(word);
        return WordTimeMs(word.ElapsedNs, word.ElapsedMs);
    }

    private static double? WordTimeMs(long? elapsedNs, int? elapsedMs) => elapsedNs is { } ns
        ? ns / 1_000_000d
        : elapsedMs is { } ms ? ms : null;

    private static long? WordTimeNs(AssessedWord word) => word.ElapsedNs is { } ns
        ? ns
        : word.ElapsedMs is { } ms ? ms * 1_000_000L : null;

    private static WordTimeAttribution Attribute(IReadOnlyDictionary<string, long> wordTimes,
        IReadOnlyList<AssessmentObjectTiming> rows)
    {
        var objectTimes = rows.Where(row => row.ElapsedNs is not null)
            .GroupBy(row => row.Word, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.ElapsedNs!.Value), StringComparer.Ordinal);
        var wordTime = wordTimes.Values.Sum();
        var attributed = objectTimes.Values.Sum();
        long notAttributed = 0, overrun = 0;
        foreach (var (word, time) in wordTimes)
        {
            var left = time - objectTimes.GetValueOrDefault(word);
            if (left >= 0) notAttributed += left;
            else overrun -= left;
        }
        var recorded = objectTimes.Count > 0;
        return new WordTimeAttribution(wordTimes.Count, wordTime / 1_000_000d, attributed / 1_000_000d,
            recorded ? notAttributed / 1_000_000d : null,
            recorded && wordTime > 0 ? notAttributed / (double)wordTime : null,
            overrun / 1_000_000d, overrun > 0);
    }

}
