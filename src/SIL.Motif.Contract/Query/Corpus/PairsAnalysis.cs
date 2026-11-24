using System.Text.RegularExpressions;

namespace SIL.Motif.Contract.Query.Corpus;

/// <summary>Where the differing part sits: after the base (<c>suffix</c>) or before it (<c>prefix</c>).</summary>
public enum PairsDiffer { Suffix, Prefix }

/// <summary>What <see cref="PairsAnalysis.Run"/> looks for.</summary>
/// <param name="Differ">Whether the differing part X follows or precedes the base.</param>
/// <param name="XPattern">An optional regular expression the whole of X must match (non-backtracking).</param>
/// <param name="Having">Literal Xs a base must all have (and, when given, only such bases are reported). A having X is searched even when it does not match <paramref name="XPattern"/>.</param>
/// <param name="MaxXLength">The longest X, in segments. Longer differences are not searched.</param>
/// <param name="MaxResults">The most X rows, and the most base rows, returned.</param>
/// <param name="ExamplesPerX">The most example pairs listed for one X.</param>
public sealed record PairsOptions(PairsDiffer Differ, string? XPattern = null, IReadOnlyList<string>? Having = null,
    int MaxXLength = 4, int MaxResults = 100, int ExamplesPerX = 5)
{
    /// <summary>The most segments an X may be asked to span.</summary>
    public const int XLengthLimit = 12;

    /// <summary>The most rows a caller may ask for.</summary>
    public const int ResultLimit = 10_000;
}

/// <summary>One attested pair: <paramref name="Base"/> and the longer form it makes with the X.</summary>
public sealed record PairExample(string Base, string Derived, long BaseTokens, long DerivedTokens);

/// <summary>An X with the bases it attaches to.</summary>
/// <param name="X">The differing part.</param>
/// <param name="Bases">How many bases have base+X (or X+base) in the corpus.</param>
/// <param name="Tokens">The tokens of those derived forms.</param>
/// <param name="BaseTokens">The tokens of those bases.</param>
/// <param name="Examples">The pairs with the most derived tokens, then by base.</param>
public sealed record PairsX(string X, int Bases, long Tokens, long BaseTokens, IReadOnlyList<PairExample> Examples);

/// <summary>A base with every X it takes.</summary>
public sealed record PairsBase(string Base, long Tokens, IReadOnlyList<string> Xs);

/// <summary>What was cut because of a cap. A total larger than its shown count means rows were left out.</summary>
public sealed record PairsTruncation(int XsTotal, int XsShown, int BasesTotal, int BasesShown)
{
    /// <summary>True when any row was left out.</summary>
    public bool Truncated => XsShown < XsTotal || BasesShown < BasesTotal;
}

/// <summary>The result of <see cref="PairsAnalysis.Run"/>.</summary>
public sealed record PairsResult(PairsDiffer Differ, string? XPattern, IReadOnlyList<string> Having, int MaxXLength, int MaxResults,
    int PairCount, IReadOnlyList<PairsX> Xs, IReadOnlyList<PairsBase> Bases, PairsTruncation Truncation);

/// <summary>
/// Finds the pairs of corpus words that differ only by an affix: base and base+X (suffix) or X+base (prefix), the
/// first step of finding affixes and allomorphs in unannotated text.
/// </summary>
/// <remarks>
/// Every word is cut at each of its last (suffix) or first (prefix) 1 to <c>MaxXLength</c> segment boundaries, and the
/// remainder is looked up in a hash set of the corpus, so the cost is the number of types times <c>MaxXLength</c>, not
/// the square of the types. Segments are the corpus's. A base has at least one segment. Rows are in ordinal order, X
/// rows by descending base count, so a run is reproducible.
/// </remarks>
public static class PairsAnalysis
{
    /// <summary>Runs the analysis; throws <see cref="ArgumentException"/> for an option out of range or an unparsable pattern.</summary>
    public static PairsResult Run(CorpusWordList corpus, PairsOptions options)
    {
        var fold = corpus.Options.FoldCase;
        var having = (options.Having ?? []).Select(x => x.Normalize(System.Text.NormalizationForm.FormC)).Select(x => fold ? x.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormC) : x).Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (having.Any(x => x.Length == 0)) throw new ArgumentException("A having X may not be empty.");
        if (options.MaxXLength is < 1 or > PairsOptions.XLengthLimit)
            throw new ArgumentException($"MaxXLength must be 1 to {PairsOptions.XLengthLimit}.");
        if (options.MaxResults is < 1 or > PairsOptions.ResultLimit)
            throw new ArgumentException($"MaxResults must be 1 to {PairsOptions.ResultLimit}.");
        if (options.ExamplesPerX is < 0 or > 100) throw new ArgumentException("ExamplesPerX must be 0 to 100.");
        Regex? pattern = null;
        try
        {
            if (options.XPattern is { Length: > 0 } source)
                pattern = new Regex("^(?:" + source + ")$", RegexOptions.NonBacktracking | RegexOptions.CultureInvariant | (fold ? RegexOptions.IgnoreCase : RegexOptions.None));
        }
        catch (ArgumentException exception) { throw new ArgumentException("The X pattern is not a valid regular expression: " + exception.Message); }

        var maxX = Math.Max(options.MaxXLength, having.Select(x => corpus.Segments.Segment(x).Count).DefaultIfEmpty(0).Max());
        maxX = Math.Min(maxX, PairsOptions.XLengthLimit);
        var havingSet = having.ToHashSet(StringComparer.Ordinal);
        var accepted = new Dictionary<string, bool>(StringComparer.Ordinal);
        bool Accepts(string x)
        {
            if (!accepted.TryGetValue(x, out var ok))
                accepted[x] = ok = pattern is null || havingSet.Contains(x) || pattern.IsMatch(x);
            return ok;
        }

        // One entry per attested pair, keyed by base: base -> (x, derived).
        var byBase = new Dictionary<string, List<(string X, CorpusEntry Derived)>>(StringComparer.Ordinal);
        foreach (var word in corpus.Words)
        {
            var segments = corpus.Segments.Segment(word.Form);
            for (var length = 1; length <= maxX && length < segments.Count; length++)
            {
                var suffix = options.Differ == PairsDiffer.Suffix;
                var baseText = string.Concat(suffix ? segments.Take(segments.Count - length) : segments.Skip(length));
                var x = string.Concat(suffix ? segments.Skip(segments.Count - length) : segments.Take(length));
                if (!Accepts(x) || !corpus.TryGet(baseText, out _)) continue;
                if (!byBase.TryGetValue(baseText, out var list)) byBase[baseText] = list = [];
                list.Add((x, word));
            }
        }

        if (having.Length > 0)
            foreach (var key in byBase.Keys.ToArray())
                if (!havingSet.All(x => byBase[key].Any(pair => pair.X == x))) byBase.Remove(key);

        var byX = new Dictionary<string, List<(string Base, CorpusEntry Derived)>>(StringComparer.Ordinal);
        var pairCount = 0;
        foreach (var (baseText, list) in byBase)
            foreach (var (x, derived) in list)
            {
                if (!byX.TryGetValue(x, out var xs)) byX[x] = xs = [];
                xs.Add((baseText, derived));
                pairCount++;
            }

        var xRows = byX.Select(pair => ToX(corpus, pair.Key, pair.Value, options.ExamplesPerX))
            .OrderByDescending(row => row.Bases).ThenBy(row => row.X, StringComparer.Ordinal).ToList();
        var baseRows = byBase.Select(pair => new PairsBase(pair.Key, CorpusTokens(corpus, pair.Key),
                [.. pair.Value.Select(item => item.X).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal)]))
            .OrderByDescending(row => row.Xs.Count).ThenByDescending(row => row.Tokens).ThenBy(row => row.Base, StringComparer.Ordinal).ToList();

        var shownX = xRows.Take(options.MaxResults).ToList();
        var shownBases = baseRows.Take(options.MaxResults).ToList();
        return new PairsResult(options.Differ, options.XPattern, having, maxX, options.MaxResults, pairCount, shownX, shownBases,
            new PairsTruncation(xRows.Count, shownX.Count, baseRows.Count, shownBases.Count));
    }

    private static long CorpusTokens(CorpusWordList corpus, string form) => corpus.TryGet(form, out var word) ? word.Tokens : 0;

    private static PairsX ToX(CorpusWordList corpus, string x, List<(string Base, CorpusEntry Derived)> pairs, int examples) =>
        new(x, pairs.Count, pairs.Sum(pair => pair.Derived.Tokens), pairs.Sum(pair => CorpusTokens(corpus, pair.Base)),
            [.. pairs.OrderByDescending(pair => pair.Derived.Tokens).ThenBy(pair => pair.Base, StringComparer.Ordinal).Take(examples)
                .Select(pair => new PairExample(pair.Base, pair.Derived.Form, CorpusTokens(corpus, pair.Base), pair.Derived.Tokens))]);
}
