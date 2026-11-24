using System.Text;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Query.Phon;

/// <summary>The pattern predicates of one query run, resolved over the candidate forms through one <see cref="IPhonMatcher"/>.</summary>
public sealed class PhonSession(IPhonMatcher matcher, int batchSize = PhonSession.DefaultBatchSize, int maxBatchBytes = PhonSession.DefaultBatchBytes)
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
    private bool _complete = true;
    private string? _stopReason;
    private int _requests;

    /// <summary>What this run could not decide.</summary>
    public PhonReport Report => new(_candidates.Count, _unsegmentable.Count, [.. _unsegmentable.Values.Take(SampleSize)],
        _incomplete.Count, [.. _incomplete.Values.Take(SampleSize)], _complete, _stopReason, matcher.ParserVersion, _requests);

    /// <summary>Whether the query reached the pattern predicates, so a <see cref="Report"/> says something even when no form needed PanGloss.</summary>
    public bool Ran { get; private set; }

    /// <summary>Sends each leaf's pattern once over the distinct candidate forms, in batches.</summary>
    internal void Resolve(IReadOnlyList<PhonLeaf> leaves, IEnumerable<string> forms, CancellationToken cancellationToken = default)
    {
        Ran = true;
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
                var result = matcher.Match(leaf.Plan.Text, batch, cancellationToken);
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
                leaf.Plan.TargetOffset is { } offset && leaf.Plan.TargetWidth is { } width ? span.Start + offset : null,
                leaf.Plan.TargetOffset is { } offset2 && leaf.Plan.TargetWidth is { } width2 ? span.Start + offset2 + width2 : null)).ToArray();
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
    /// <summary>Two leaves with the same key are answered by one request; an <c>env</c> and a <c>phon</c> differ in where their target sits.</summary>
    public string Key => Kind + "\0" + Plan.Text + "\0" + Plan.TargetOffset + "\0" + Plan.TargetWidth;
}

/// <summary>True, false, or not decided because PanGloss could not segment the form or stopped on it.</summary>
internal enum Tri : byte { False, Unknown, True }
