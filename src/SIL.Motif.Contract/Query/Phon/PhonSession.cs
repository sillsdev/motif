using System.Text;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Query.Phon;

/// <summary>The pattern predicates of one query run, resolved over the candidate forms through one <see cref="IPhonMatcher"/>.</summary>
public sealed class PhonSession(IPhonMatcher matcher, int batchSize = PhonSession.DefaultBatchSize, int maxBatchBytes = PhonSession.DefaultBatchBytes,
    string segmentTest = PhonSegmentTest.Default)
{
    /// <summary>The most words in one <c>match</c> request.</summary>
    public const int DefaultBatchSize = 20000;

    /// <summary>The most encoded bytes of words in one request, well under the 1 MiB line limit of <c>pangloss serve</c>.</summary>
    public const int DefaultBatchBytes = 512 * 1024;

    // A word costs its UTF-8 bytes plus quotes, a comma and a little escaping.
    private const int WordOverhead = 8;

    private const int SampleSize = 10;

    // plan key -> form -> result; a form the candidates did not include has no entry.
    private readonly Dictionary<string, Dictionary<string, PhonWordResult>> _results = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PhonLeaf> _leaves = new(StringComparer.Ordinal);
    private readonly List<PhonLeaf> _reported = [];
    private readonly Dictionary<string, QuerySkippedWord> _unsegmentable = new(StringComparer.Ordinal);
    private readonly Dictionary<string, QuerySkippedWord> _incomplete = new(StringComparer.Ordinal);
    private readonly HashSet<string> _candidates = new(StringComparer.Ordinal);
    private readonly List<PhonLeaf> _negated = [];
    private PhonInventory? _inventory;
    private bool _inventoryRead;
    private bool _complete = true;
    private string? _stopReason;
    private int _requests;

    /// <summary>What this run could not decide.</summary>
    public PhonReport Report
    {
        get
        {
            var identity = _leaves.Values.Any(IsIdentityTested);
            var warnings = _negated.Where(IsIdentityTested).Select(leaf => leaf.Source).Distinct(StringComparer.Ordinal)
                .Select(source => $"not over {source}: absence is not proven, because --segment-test identity tests its literals and segment-list classes by exact members and a form that has no span may still hold a segment a rule treats as the same.").ToArray();
            return new PhonReport(_candidates.Count, _unsegmentable.Count, [.. _unsegmentable.Values.Take(SampleSize)],
                _incomplete.Count, [.. _incomplete.Values.Take(SampleSize)], _complete, _stopReason, matcher.ParserVersion, _requests,
                segmentTest, identity ? PhonSegmentTest.IdentityLabel : null, identity, warnings.Length == 0 ? null : warnings);
        }
    }

    /// <summary>The segment test the patterns of this run use.</summary>
    public string SegmentTest => segmentTest;

    // Only identity labels; engine tests the target as a rule does and env sides are exact as in a rule.
    private bool IsIdentityTested(PhonLeaf leaf)
    {
        if (segmentTest != PhonSegmentTest.Identity) return false;
        if (leaf.Plan.HasLiteral) return true;
        if (leaf.Plan.ClassTokens.Count == 0) return false;
        foreach (var token in leaf.Plan.ClassTokens)
        {
            var kind = _inventory?.Classes.FirstOrDefault(c => c.Token == token)?.Kind
                ?? _inventory?.Classes.FirstOrDefault(c => c.Name == token)?.Kind
                ?? _inventory?.Classes.FirstOrDefault(c => c.Id == token)?.Kind;
            if (kind != "feature") return true;
        }
        return false;
    }

    /// <summary>Whether the query reached the pattern predicates, so a <see cref="Report"/> says something even when no form needed PanGloss.</summary>
    public bool Ran { get; private set; }

    /// <summary>Sends each leaf's pattern once over the distinct candidate forms, in batches.</summary>
    internal void Resolve(IReadOnlyList<PhonLeaf> leaves, IEnumerable<string> forms, CancellationToken cancellationToken = default)
    {
        Ran = true;
        if (!_inventoryRead && segmentTest == PhonSegmentTest.Identity && leaves.Any(leaf => leaf.Plan.ClassTokens.Count > 0))
        {
            try { _inventory = matcher.Inventory(cancellationToken); }
            catch (QueryRefusalException) { _inventory = null; }
            _inventoryRead = true;
        }
        var words = forms.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        foreach (var form in words) _candidates.Add(form);
        foreach (var leaf in leaves)
        {
            if (!_leaves.TryAdd(leaf.Plan.Text, leaf)) continue;
            var table = _results[leaf.Plan.Text] = new Dictionary<string, PhonWordResult>(StringComparer.Ordinal);
            for (var offset = 0; offset < words.Length;)
            {
                var count = 0;
                var bytes = Encoding.UTF8.GetByteCount(leaf.Plan.Text);
                while (offset + count < words.Length && count < batchSize &&
                    (count == 0 || bytes + Encoding.UTF8.GetByteCount(words[offset + count]) + WordOverhead <= maxBatchBytes))
                    bytes += Encoding.UTF8.GetByteCount(words[offset + count++]) + WordOverhead;
                var batch = words.AsMemory(offset, count).ToArray();
                offset += count;
                var result = matcher.Match(leaf.Plan.Text, batch, segmentTest, cancellationToken);
                _requests++;
                if (result.Words.Count != batch.Length)
                    throw QueryRefusalException.PhonFailed("PanGloss answered a match request with the wrong number of words.");
                if (!result.Complete) { _complete = false; _stopReason ??= result.StopReason; }
                foreach (var word in result.Words)
                {
                    table[word.Word] = word;
                    if (word.Status == "unsegmentable")
                        _unsegmentable.TryAdd(word.Word, new QuerySkippedWord(word.Word, word.Reason ?? "unsegmentable", word.CharIndex));
                    else if (word.Status == "incomplete")
                        _incomplete.TryAdd(word.Word, new QuerySkippedWord(word.Word, word.Reason ?? "incomplete"));
                }
            }
        }
    }

    /// <summary>The leaves whose matches rows report, so a pattern under <c>not</c> adds none.</summary>
    internal void Reported(IEnumerable<PhonLeaf> reported)
    {
        foreach (var leaf in reported)
            if (_reported.All(known => known.Key != leaf.Key)) _reported.Add(leaf);
    }

    /// <summary>The leaves under <c>not</c>, whose absence of a match is what the query reports.</summary>
    internal void Negated(IEnumerable<PhonLeaf> negated)
    {
        foreach (var leaf in negated)
            if (_negated.All(known => known.Key != leaf.Key)) _negated.Add(leaf);
    }

    /// <summary>Whether the form matches the leaf: true, false, or unknown when PanGloss could not decide the form.</summary>
    internal Tri Lookup(PhonLeaf leaf, string form)
    {
        if (!_results.TryGetValue(leaf.Plan.Text, out var table) || !table.TryGetValue(form, out var word)) return Tri.Unknown;
        return word.Status switch
        {
            "ok" => word.Spans.Count > 0 ? Tri.True : Tri.False,
            "incomplete" when word.Spans.Count > 0 => Tri.True,
            _ => Tri.Unknown,
        };
    }

    /// <summary>The matches of every reported predicate in the form.</summary>
    public IReadOnlyList<QueryMatch> MatchesFor(string form)
    {
        var matches = new List<QueryMatch>();
        foreach (var leaf in _reported)
        {
            if (!_results.TryGetValue(leaf.Plan.Text, out var table) || !table.TryGetValue(form, out var word) || word.Spans.Count == 0) continue;
            var nfd = word.Normalized ?? form.Normalize(NormalizationForm.FormD);
            var bytes = Encoding.UTF8.GetBytes(nfd);
            var spans = word.Spans.Select(span => new QueryMatchSpan(span.Start, span.End, span.ByteStart, span.ByteEnd,
                Slice(bytes, span.ByteStart, span.ByteEnd),
                leaf.Plan.IsEnv ? span.Start : null, leaf.Plan.IsEnv ? span.End : null)).ToArray();
            matches.Add(new QueryMatch(leaf.Kind, leaf.Source, spans, word.Status == "incomplete"));
        }
        return matches;
    }

    private static string Slice(byte[] bytes, int start, int end) =>
        start >= 0 && end >= start && end <= bytes.Length ? Encoding.UTF8.GetString(bytes, start, end - start) : "";
}

/// <summary>One <c>phon</c> or <c>env</c> predicate, compiled.</summary>
/// <param name="Kind"><c>phon</c> or <c>env</c>.</param>
/// <param name="Source">The text the person or agent wrote.</param>
/// <param name="Plan">The pattern text to send.</param>
internal sealed record PhonLeaf(string Kind, string Source, PhonPlan Plan)
{
    /// <summary>Two leaves with the same key are answered by one request.</summary>
    public string Key => Kind + "\0" + Plan.Text;
}

/// <summary>True, false, or not decided because PanGloss could not segment the form or stopped on it.</summary>
internal enum Tri : byte { False, Unknown, True }
