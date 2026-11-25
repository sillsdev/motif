using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Query.Readings;

/// <summary>Why a word has more than one reading; the kinds are tried in this order.</summary>
public static class ReadingCause
{
    /// <summary>One reading has a single stem over a span that another covers with a root and derivational affixes.</summary>
    public const string LexicalisedVsDerived = "lexicalised-vs-derived";
    /// <summary>The readings hold the same morphs in a different order.</summary>
    public const string SameMorphsReordered = "same-morphs-reordered";
    /// <summary>The readings differ only in inflectional affixes that spell the same but have different analyses (SM versus OM); harmless.</summary>
    public const string HomophonousAffix = "homophonous-affix";
    /// <summary>The readings segment the word identically (same morph texts at the same spans) and differ only in which entry or analysis carries a morph.</summary>
    public const string SameSegmentation = "same-segmentation";
    /// <summary>One reading has a single stem over a span that another splits into several morphs, none of them derivational (a clitic, an inflectional affix or a short stem).</summary>
    public const string CliticOrStemSplit = "clitic-or-stem-split";
    /// <summary>The readings have the same number of morphs but cut the word at different places.</summary>
    public const string BoundaryShift = "boundary-shift";
    /// <summary>None of the above.</summary>
    public const string Other = "other";

    /// <summary>Every kind in priority order.</summary>
    public static IReadOnlyList<string> All { get; } =
        [LexicalisedVsDerived, SameMorphsReordered, HomophonousAffix, SameSegmentation, CliticOrStemSplit, BoundaryShift, Other];

    /// <summary>The sub-label of <see cref="SameSegmentation"/> when every differing morph is a stem (homograph roots).</summary>
    public const string Stems = "stems";
    /// <summary>The sub-label when every differing morph is an affix.</summary>
    public const string Affixes = "affixes";
    /// <summary>The sub-label when stems and affixes both differ, or a class is unknown.</summary>
    public const string Mixed = "mixed";

    /// <summary>Whether the cause only reflects spelling and does not change what the word means.</summary>
    public static bool Benign(string cause) => cause == HomophonousAffix;

    /// <summary>The most readings of one word compared pairwise; the rest are compared to the first.</summary>
    public const int PairwiseLimit = 40;

    /// <summary>The cause of a word's extra readings.</summary>
    public static string Of(IReadOnlyList<WordReading> readings) => Classify(readings).Cause;

    /// <summary>
    /// The cause and its sub-label. Each word gets its most specific cause: the structural kinds that any pair shows come first
    /// (lexicalised, reordered); then the kinds that must hold for every differing pair, narrowest first (homophonous inflectional
    /// affixes are a case of same segmentation, so they rank before it); then the segmentation differences any pair shows.
    /// </summary>
    public static (string Cause, string? Detail) Classify(IReadOnlyList<WordReading> readings)
    {
        var pairs = Pairs(readings).Where(pair => !SameMorphs(pair.A, pair.B)).ToList();
        if (pairs.Count == 0) return (Other, null);
        if (pairs.Any(pair => IsLexicalisedVsDerived(pair.A, pair.B) || IsLexicalisedVsDerived(pair.B, pair.A))) return (LexicalisedVsDerived, null);
        if (pairs.Any(pair => IsReordered(pair.A, pair.B))) return (SameMorphsReordered, null);
        if (pairs.All(pair => IsHomophonous(pair.A, pair.B))) return (HomophonousAffix, null);
        if (pairs.All(pair => SameSegmentationOf(pair.A, pair.B) is not null))
        {
            var labels = pairs.Select(pair => SameSegmentationOf(pair.A, pair.B)!).Distinct().ToList();
            return (SameSegmentation, labels.Count == 1 ? labels[0] : Mixed);
        }
        if (pairs.Any(pair => IsSplit(pair.A, pair.B) || IsSplit(pair.B, pair.A))) return (CliticOrStemSplit, null);
        if (pairs.Any(pair => IsBoundaryShift(pair.A, pair.B))) return (BoundaryShift, null);
        return (Other, null);
    }

    /// <summary>
    /// The sub-label when the readings have the same morph texts at the same spans and differ only in the entry or analysis of some
    /// morphs: <see cref="Stems"/>, <see cref="Affixes"/> or <see cref="Mixed"/>; null when they segment differently or a span is unplaced.
    /// </summary>
    internal static string? SameSegmentationOf(WordReading a, WordReading b)
    {
        if (a.Morphs.Count != b.Morphs.Count || SameMorphs(a, b)) return null;
        var stems = 0;
        var affixes = 0;
        var unknown = false;
        foreach (var (x, y) in a.Morphs.Zip(b.Morphs))
        {
            if (x.Start < 0 || x.Start != y.Start || x.End != y.End || x.Text.Length == 0 || !string.Equals(x.Text, y.Text, StringComparison.Ordinal)) return null;
            if (x.Key == y.Key) continue;
            if (x.Kind is null || y.Kind is null) unknown = true;
            else if (x.Kind == MorphKinds.Stem && y.Kind == MorphKinds.Stem) stems++;
            else if (x.Kind != MorphKinds.Stem && y.Kind != MorphKinds.Stem) affixes++;
            else unknown = true;
        }
        return unknown || stems > 0 && affixes > 0 ? Mixed : stems > 0 ? Stems : Affixes;
    }

    /// <summary>After the shared head and tail, <paramref name="single"/> keeps one stem over the span that <paramref name="split"/> covers with several morphs.</summary>
    internal static bool IsSplit(WordReading single, WordReading split)
    {
        var (left, right) = Middle(single, split);
        if (left.Count != 1 || right.Count < 2) return false;
        var one = left[0];
        if (one.Start < 0 || right.Any(morph => morph.Start < 0)) return false;
        if (one.Start != right.Min(morph => morph.Start) || one.End != right.Max(morph => morph.End)) return false;
        return one.Kind == MorphKinds.Stem;
    }

    /// <summary>The same number of morphs, cut at different places.</summary>
    internal static bool IsBoundaryShift(WordReading a, WordReading b)
    {
        if (a.Morphs.Count != b.Morphs.Count || a.Morphs.Any(morph => morph.Start < 0) || b.Morphs.Any(morph => morph.Start < 0)) return false;
        return a.Morphs.Zip(b.Morphs).Any(pair => pair.First.Start != pair.Second.Start || pair.First.End != pair.Second.End);
    }

    private static IEnumerable<(WordReading A, WordReading B)> Pairs(IReadOnlyList<WordReading> readings)
    {
        var count = Math.Min(readings.Count, PairwiseLimit);
        for (var i = 0; i < count; i++)
            for (var j = i + 1; j < count; j++) yield return (readings[i], readings[j]);
        for (var j = count; j < readings.Count; j++) yield return (readings[0], readings[j]);
    }

    private static bool SameMorphs(WordReading a, WordReading b) =>
        a.Morphs.Count == b.Morphs.Count && a.Morphs.Zip(b.Morphs).All(pair => pair.First.Key == pair.Second.Key);

    /// <summary>The same morphs (form and analysis) in a different order.</summary>
    internal static bool IsReordered(WordReading a, WordReading b)
    {
        if (a.Morphs.Count != b.Morphs.Count || SameMorphs(a, b)) return false;
        var left = a.Morphs.Select(morph => morph.Key).Order().ToList();
        var right = b.Morphs.Select(morph => morph.Key).Order().ToList();
        return left.SequenceEqual(right);
    }

    /// <summary>
/// Same length, and every position is the same morph or one with the same text and a different analysis, both inflectional affixes
/// (agreement markers that spell alike). A stem, a derivational affix or a morph of unknown class never counts as benign.
/// </summary>
    internal static bool IsHomophonous(WordReading a, WordReading b)
    {
        if (a.Morphs.Count != b.Morphs.Count || SameMorphs(a, b)) return false;
        foreach (var (x, y) in a.Morphs.Zip(b.Morphs))
        {
            if (x.Key == y.Key) continue;
            if (x.Msa == y.Msa || x.Text.Length == 0 || x.Start < 0 || x.Start != y.Start || !string.Equals(x.Text, y.Text, StringComparison.Ordinal)) return false;
            if (x.Kind != MorphKinds.Inflectional || y.Kind != MorphKinds.Inflectional) return false;
        }
        return true;
    }

    /// <summary>
    /// After the morphs the readings share at the start and end are removed, <paramref name="lexicalised"/> keeps one morph and
    /// <paramref name="derived"/> several, over the same stretch of the word. With the project's morph classes the one is a stem and
    /// the several are one or more stems and derivational affixes only; without them the shape of the spans decides.
    /// </summary>
    internal static bool IsLexicalisedVsDerived(WordReading lexicalised, WordReading derived)
    {
        var (left, right) = Middle(lexicalised, derived);
        if (left.Count != 1 || right.Count < 2) return false;
        var single = left[0];
        if (single.Start < 0 || right.Any(morph => morph.Start < 0)) return false;
        if (single.Start != right.Min(morph => morph.Start) || single.End != right.Max(morph => morph.End)) return false;
        var classified = single.Kind is not null && right.All(morph => morph.Kind is not null);
        if (!classified) return true;
        return single.Kind == MorphKinds.Stem && right.Count(morph => morph.Kind == MorphKinds.Stem) >= 1 &&
            right.All(morph => morph.Kind is MorphKinds.Stem or MorphKinds.Derivational) && right.Any(morph => morph.Kind == MorphKinds.Derivational);
    }

    private static (List<ReadingMorph> A, List<ReadingMorph> B) Middle(WordReading a, WordReading b)
    {
        var x = a.Morphs;
        var y = b.Morphs;
        var head = 0;
        while (head < x.Count && head < y.Count && x[head].Key == y[head].Key) head++;
        var tail = 0;
        while (tail < x.Count - head && tail < y.Count - head && x[^(tail + 1)].Key == y[^(tail + 1)].Key) tail++;
        return ([.. x.Skip(head).Take(x.Count - head - tail)], [.. y.Skip(head).Take(y.Count - head - tail)]);
    }
}

/// <summary>One morph of an example reading, as it is shown.</summary>
/// <param name="Form">The form id.</param>
/// <param name="Msa">The analysis id.</param>
/// <param name="Text">The surface text.</param>
/// <param name="Entry">The entry id when known.</param>
/// <param name="Kind">The morph class when known.</param>
/// <param name="Gloss">The entry's gloss when the project has one.</param>
public sealed record CauseMorph(string Form, string Msa, string Text, string? Entry = null, string? Kind = null, string? Gloss = null);

/// <summary>One example reading.</summary>
/// <param name="Morphs">Its ordered morphs.</param>
public sealed record CauseReading(IReadOnlyList<CauseMorph> Morphs);

/// <summary>A word that shows a cause and its readings.</summary>
/// <param name="Word">The word.</param>
/// <param name="Readings">Its readings, at most <see cref="CauseGroupBuilder.ExampleReadings"/>.</param>
public sealed record CauseExample(string Word, IReadOnlyList<CauseReading> Readings);

/// <summary>The words that share a cause.</summary>
/// <param name="Cause">One of <see cref="ReadingCause.All"/>.</param>
/// <param name="Benign">True when the extra readings are only a spelling coincidence.</param>
/// <param name="Words">How many word types.</param>
/// <param name="Tokens">Their token total.</param>
/// <param name="Example">The most frequent word of the group.</param>
/// <param name="Detail">The sub-label (<c>stems</c>, <c>affixes</c> or <c>mixed</c> for <see cref="ReadingCause.SameSegmentation"/>); null otherwise.</param>
public sealed record CauseGroup(string Cause, bool Benign, int Words, long Tokens, CauseExample Example,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Detail = null);

/// <summary>Collects the words of a query into cause groups.</summary>
public sealed class CauseGroupBuilder
{
    /// <summary>The most readings an example shows.</summary>
    public const int ExampleReadings = 6;

    private readonly Dictionary<(string Cause, string Detail), (int Words, long Tokens, string Word, long Best, WordReadings Readings)> _groups = [];

    /// <summary>Adds a word with more than one reading.</summary>
    public void Add(string word, long tokens, WordReadings readings)
    {
        var (cause, detail) = ReadingCause.Classify(readings.Readings);
        var key = (cause, detail ?? "");
        _groups[key] = _groups.TryGetValue(key, out var group)
            ? (group.Words + 1, group.Tokens + tokens, tokens > group.Best ? word : group.Word, Math.Max(group.Best, tokens), tokens > group.Best ? readings : group.Readings)
            : (1, tokens, word, tokens, readings);
    }

    /// <summary>The groups in cause priority order, a cause's sub-labels together.</summary>
    public IReadOnlyList<CauseGroup> Build() =>
    [
        .. _groups.Keys.OrderBy(key => ReadingCause.All.ToList().IndexOf(key.Cause)).ThenBy(key => key.Detail, StringComparer.Ordinal).Select(key =>
        {
            var group = _groups[key];
            return new CauseGroup(key.Cause, ReadingCause.Benign(key.Cause), group.Words, group.Tokens, new CauseExample(group.Word,
                [.. group.Readings.Readings.Take(ExampleReadings).Select(reading => new CauseReading(
                    [.. reading.Morphs.Select(morph => new CauseMorph(morph.Form, morph.Msa, morph.Text, morph.Entry, morph.Kind, morph.Gloss))]))]),
                key.Detail.Length == 0 ? null : key.Detail);
        }),
    ];
}
