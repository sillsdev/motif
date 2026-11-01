using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>
/// Counts every compared word and keeps only the words a Difference lists: those that changed or fall in a category,
/// at most <see cref="ProposalDifferenceListing.MostWordsPerGroup"/> for each matrix cell, category and limitation.
/// </summary>
/// <remarks>
/// Within a group the words with the most places in the Text come first and the spelling, ordinal, breaks a tie, so
/// the same input always lists the same words. A word is held only while some group keeps it, so building a Difference
/// never holds more than the bound however many words the Text has.
/// </remarks>
internal sealed class ProposalDifferenceTally
{
    private enum Kind { Cell, Bucket, Limitation }

    private readonly record struct Group(Kind Kind, string Id);

    private sealed class Counts
    {
        public int Words;
        public int Occurrences;
        public int Listed;
    }

    private sealed class Candidate(string form, ProposalDifferenceCaseFacts facts, int occurrences)
    {
        public string Form { get; } = form;
        public ProposalDifferenceCaseFacts Facts { get; } = facts;
        public int Occurrences { get; } = occurrences;
    }

    // The root of each group's queue is its worst word, which is the first to give way.
    private sealed class WorstFirst : IComparer<Candidate>
    {
        public int Compare(Candidate? left, Candidate? right) => left!.Occurrences != right!.Occurrences
            ? left.Occurrences.CompareTo(right.Occurrences) : string.CompareOrdinal(right.Form, left.Form);
    }

    private static readonly WorstFirst Order = new();

    private readonly int _most;
    private readonly SortedDictionary<(string Before, string After), Counts> _cells = [];
    private readonly SortedDictionary<ProposalDifferenceBucket, Counts> _buckets = [];
    private readonly SortedDictionary<HumanAgreementLimitation, Counts> _limitations = [];
    private readonly Dictionary<Group, PriorityQueue<Candidate, Candidate>> _kept = [];
    private IReadOnlyList<(string Form, ProposalDifferenceCaseFacts Facts, int Occurrences)>? _listed;

    /// <summary>Starts a tally that keeps at most <paramref name="most"/> words for each group.</summary>
    public ProposalDifferenceTally(int most = ProposalDifferenceListing.MostWordsPerGroup)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(most, 1);
        _most = most;
    }

    /// <summary>How many words were compared.</summary>
    public int Words { get; private set; }

    /// <summary>How many places in the Text those words have.</summary>
    public int Occurrences { get; private set; }

    /// <summary>How many words have an approved decision after the change.</summary>
    public int WithApprovedDecisions { get; private set; }

    /// <summary>How many words fall in a could-be-bad category.</summary>
    public int CouldBeBad { get; private set; }

    /// <summary>How many words improved.</summary>
    public int Improved { get; private set; }

    /// <summary>Counts one compared word and keeps it when some group lists it.</summary>
    public void Add(string form, ProposalDifferenceCaseFacts facts, int occurrences)
    {
        if (_listed is not null) throw new InvalidOperationException("The tally is sealed.");
        Words++;
        Occurrences += occurrences;
        if (facts.After.ApprovedExpectedMorphologyCount > 0) WithApprovedDecisions++;
        if (facts.CouldBeBad) CouldBeBad++;
        if (facts.Improved) Improved++;

        var cell = (Before: ProposalDifferenceRepository.StateName(facts.Before),
            After: ProposalDifferenceRepository.StateName(facts.After));
        var buckets = facts.Buckets.Select(item => item.Bucket).Distinct().ToArray();
        var limitations = facts.Before.Limitations.Concat(facts.After.Limitations).Distinct().ToArray();
        Count(_cells, cell, occurrences);
        foreach (var bucket in buckets) Count(_buckets, bucket, occurrences);
        foreach (var limitation in limitations) Count(_limitations, limitation, occurrences);

        // An unchanged word in no category has nothing to look at, so it is counted and not listed.
        var changed = facts.Changes.Count > 0 || buckets.Length > 0 || limitations.Length > 0 || cell.Before != cell.After;
        if (!changed) return;
        var candidate = new Candidate(form, facts, occurrences);
        Offer(new Group(Kind.Cell, CellId(cell.Before, cell.After)), candidate);
        foreach (var bucket in buckets) Offer(new Group(Kind.Bucket, bucket.ToString()), candidate);
        foreach (var limitation in limitations) Offer(new Group(Kind.Limitation, limitation.ToString()), candidate);
    }

    /// <summary>
    /// The words to store, ordered by spelling. Calling it ends the tally, and it fixes how many words each count lists.
    /// </summary>
    public IReadOnlyList<(string Form, ProposalDifferenceCaseFacts Facts, int Occurrences)> Listed()
    {
        if (_listed is not null) return _listed;
        var unique = new HashSet<Candidate>();
        foreach (var queue in _kept.Values)
            foreach (var (candidate, _) in queue.UnorderedItems) unique.Add(candidate);
        var ordered = unique.OrderBy(candidate => candidate.Form, StringComparer.Ordinal).ToArray();
        foreach (var candidate in ordered)
        {
            var facts = candidate.Facts;
            _cells[(ProposalDifferenceRepository.StateName(facts.Before), ProposalDifferenceRepository.StateName(facts.After))]
                .Listed++;
            foreach (var bucket in facts.Buckets.Select(item => item.Bucket).Distinct()) _buckets[bucket].Listed++;
            foreach (var limitation in facts.Before.Limitations.Concat(facts.After.Limitations).Distinct())
                _limitations[limitation].Listed++;
        }
        _kept.Clear();
        return _listed = ordered.Select(candidate => (candidate.Form, candidate.Facts, candidate.Occurrences)).ToArray();
    }

    /// <summary>The words and places of each category, with how many of its words are listed.</summary>
    public ProposalDifferenceBucketCount[] BucketCounts()
    {
        Listed();
        return [.. _buckets.Select(pair => new ProposalDifferenceBucketCount(pair.Key, pair.Value.Words,
            pair.Value.Occurrences, pair.Value.Listed))];
    }

    /// <summary>The words and places of each before and after pair, with how many of its words are listed.</summary>
    public ProposalDifferenceMatrixCell[] MatrixCells()
    {
        Listed();
        return [.. _cells.OrderBy(pair => pair.Key.Before, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.After, StringComparer.Ordinal)
            .Select(pair => new ProposalDifferenceMatrixCell(pair.Key.Before, pair.Key.After, pair.Value.Words,
                pair.Value.Occurrences, pair.Value.Listed))];
    }

    /// <summary>The words and places each search limit touched, with how many of its words are listed.</summary>
    public ProposalDifferenceLimitationCount[] LimitationCounts()
    {
        Listed();
        return [.. _limitations.Select(pair => new ProposalDifferenceLimitationCount(pair.Key, pair.Value.Words,
            pair.Value.Occurrences, pair.Value.Listed))];
    }

    private static string CellId(string before, string after) => before + "\n" + after;

    private static void Count<TKey>(SortedDictionary<TKey, Counts> counts, TKey key, int occurrences) where TKey : notnull
    {
        if (!counts.TryGetValue(key, out var count)) counts[key] = count = new Counts();
        count.Words++;
        count.Occurrences += occurrences;
    }

    private void Offer(Group group, Candidate candidate)
    {
        if (!_kept.TryGetValue(group, out var queue)) _kept[group] = queue = new PriorityQueue<Candidate, Candidate>(Order);
        if (queue.Count < _most) { queue.Enqueue(candidate, candidate); return; }
        if (Order.Compare(candidate, queue.Peek()) <= 0) return;
        queue.Dequeue();
        queue.Enqueue(candidate, candidate);
    }
}
