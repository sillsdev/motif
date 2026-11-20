using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Query;

/// <summary>One corpus word type before it is grouped: its spelling, parse state and how often it occurs.</summary>
/// <param name="Form">The spelling as the Text writes it.</param>
/// <param name="ParseState">approved, parsed or unparsed.</param>
/// <param name="Tokens">The occurrences in the Texts.</param>
public sealed record CorpusWord(string Form, string ParseState, int Tokens);

/// <summary>What to group: the kind, the string or pattern that selects words, the state filter and the page.</summary>
/// <param name="Kind">suffix or prefix.</param>
/// <param name="Text">The ending or start, or null when a pattern selects.</param>
/// <param name="Pattern">A pattern with a named stem group, or null when text selects.</param>
/// <param name="ParseState">approved, parsed, unparsed or all.</param>
/// <param name="Limit">The most groups on a page, or null for the default.</param>
/// <param name="Page">The one-based page, or null for the first.</param>
/// <param name="MinStem">The fewest graphemes a stem may have, or null for the default.</param>
public sealed record GroupSpec(string Kind, string? Text, string? Pattern, string? ParseState, int? Limit, int? Page, int? MinStem = null);

/// <summary>
/// Groups corpus word types by the stem that remains when an ending or start is removed. Words are compared by their
/// fold key, and a stem boundary always falls between grapheme clusters, so a combining mark never splits from its base.
/// The result is a pure function of the words and the lexicon: the same input gives the same bytes.
/// </summary>
public static class WordGrouping
{
    /// <summary>The grouping that strips an ending.</summary>
    public const string Suffix = "suffix";

    /// <summary>The grouping that strips a start.</summary>
    public const string Prefix = "prefix";

    /// <summary>A parse a human approved.</summary>
    public const string Approved = "approved";

    /// <summary>A parse exists and none is approved.</summary>
    public const string Parsed = "parsed";

    /// <summary>No parse exists.</summary>
    public const string Unparsed = "unparsed";

    /// <summary>Every state.</summary>
    public const string All = "all";

    /// <summary>The page size when none is given.</summary>
    public const int DefaultLimit = 50;

    /// <summary>The largest page size; a larger request is reduced to it.</summary>
    public const int MaxLimit = 200;

    /// <summary>The default fewest graphemes in a stem.</summary>
    public const int DefaultMinStem = 2;

    /// <summary>The longest pattern accepted.</summary>
    public const int MaxPatternLength = FormQuery.MaximumRegexLength;

    private static readonly TimeSpan PatternTimeout = TimeSpan.FromSeconds(2);

    private sealed class Entry(string key, string form, string state, int tokens)
    {
        public string Key { get; } = key;
        public string Form { get; set; } = form;
        public string State { get; set; } = state;
        public int Tokens { get; set; } = tokens;
        public int FormTokens { get; set; } = tokens;
    }

    /// <summary>Groups the words, or throws <see cref="ArgumentException"/> when the request cannot be answered.</summary>
    /// <param name="corpus">Every word type of the Texts; types that fold to one key are merged.</param>
    /// <param name="lexiconStems">The spellings of the lexicon's stem forms.</param>
    /// <param name="spec">What to group.</param>
    /// <param name="fold">The fold that makes two spellings one key.</param>
    /// <param name="foldName">The fold set's name, echoed in the response.</param>
    /// <param name="readAt">What the words were read from, echoed in the response.</param>
    /// <param name="notes">Facts about the read that the response carries.</param>
    public static GroupResponse Group(IEnumerable<CorpusWord> corpus, IEnumerable<string> lexiconStems, GroupSpec spec,
        Func<string, string> fold, string foldName, string readAt, IReadOnlyList<string> notes)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(lexiconStems);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(fold);
        var forward = spec.Kind == Suffix;
        if (!forward && spec.Kind != Prefix) throw new ArgumentException("Group kind must be suffix or prefix.");
        var state = spec.ParseState ?? All;
        if (state is not (Approved or Parsed or Unparsed or All))
            throw new ArgumentException("The parse state must be parsed, unparsed, approved or all.");
        var requestedLimit = spec.Limit ?? DefaultLimit;
        if (requestedLimit < 1) throw new ArgumentException("The limit must be a positive whole number.");
        var limit = Math.Min(requestedLimit, MaxLimit);
        if (spec.Page is < 1) throw new ArgumentException("The page must be a positive whole number.");
        var minStem = spec.MinStem ?? DefaultMinStem;
        if (minStem < 1) throw new ArgumentException("The minimum stem length must be a positive whole number.");
        var match = BuildMatcher(spec, forward, fold, out var query);

        var entries = Merge(corpus, fold);
        var keys = entries.Keys.Order(StringComparer.Ordinal).ToArray();
        var lexicon = lexiconStems.Select(fold).Where(stem => stem.Length > 0).ToHashSet(StringComparer.Ordinal);
        var index = new OrientedIndex(keys, forward);

        var matched = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
        foreach (var key in keys)
            if (match(key) is { Length: > 0 } stem && StringInfo.ParseCombiningCharacters(stem).Length >= minStem)
            {
                if (!matched.TryGetValue(stem, out var list)) matched[stem] = list = [];
                list.Add(entries[key]);
            }

        var groups = new List<(string Stem, StemGroup Group)>();
        foreach (var (stem, all) in matched)
        {
            var listed = all.Where(entry => state == All || entry.State == state).ToList();
            if (listed.Count == 0) continue;
            listed.Sort(static (a, b) => b.Tokens != a.Tokens ? b.Tokens.CompareTo(a.Tokens) : string.CompareOrdinal(a.Key, b.Key));
            var own = all.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
            var siblings = index.Siblings(stem, own, StemGroup.SiblingsShown, out var siblingTotal);
            var attested = entries.ContainsKey(stem);
            var status = lexicon.Contains(stem) ? StemGroup.Known
                : attested || siblingTotal > 0 ? StemGroup.Fitting : StemGroup.NotFitting;
            groups.Add((stem, new StemGroup(stem, status, listed.Sum(entry => entry.Tokens), attested,
                [.. listed.Take(StemGroup.WordsShown).Select(entry => new GroupedWord(entry.Form, entry.State, entry.Tokens))],
                listed.Count, siblings, siblingTotal)));
        }
        groups.Sort(static (a, b) => a.Group.Tokens != b.Group.Tokens ? b.Group.Tokens.CompareTo(a.Group.Tokens)
            : string.CompareOrdinal(a.Stem, b.Stem));

        var pages = Math.Max(1, (groups.Count + limit - 1) / limit);
        var page = spec.Page ?? 1;
        if (page > pages) throw new ArgumentException($"Page {page} is past the last page, {pages}.");
        var shown = groups.Skip((page - 1) * limit).Take(limit).Select(item => item.Group).ToArray();
        var capped = requestedLimit > MaxLimit || page * limit < groups.Count;
        var allNotes = new List<string>(notes);
        if (requestedLimit > MaxLimit)
            allNotes.Add($"The limit {requestedLimit.ToString(CultureInfo.InvariantCulture)} was reduced to the cap of {MaxLimit}.");
        if (page * limit < groups.Count)
            allNotes.Add($"Showing stems {((page - 1) * limit + 1).ToString(CultureInfo.InvariantCulture)} to " +
                $"{(page * limit).ToString(CultureInfo.InvariantCulture)} of {groups.Count.ToString(CultureInfo.InvariantCulture)}; " +
                $"--page {(page + 1).ToString(CultureInfo.InvariantCulture)} shows the next.");
        if (shown.Any(group => group.WordsTotal > group.Words.Count))
            allNotes.Add($"A stem lists at most {StemGroup.WordsShown} words; its total says how many matched.");
        return new GroupResponse(GroupResponse.CurrentSchemaVersion, spec.Kind, query, state, foldName, readAt, minStem, keys.Length,
            groups.Sum(item => item.Group.WordsTotal), groups.Count, groups.Count(item => item.Group.Status == StemGroup.Known),
            groups.Count(item => item.Group.Status == StemGroup.Fitting),
            groups.Count(item => item.Group.Status == StemGroup.NotFitting), page, pages, limit, capped, allNotes, shown);
    }

    private static Dictionary<string, Entry> Merge(IEnumerable<CorpusWord> corpus, Func<string, string> fold)
    {
        var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var word in corpus)
        {
            var key = fold(word.Form);
            if (key.Length == 0) continue;
            if (!entries.TryGetValue(key, out var entry))
            {
                entries[key] = new Entry(key, word.Form, word.ParseState, word.Tokens);
                continue;
            }
            entry.Tokens += word.Tokens;
            if (Rank(word.ParseState) > Rank(entry.State)) entry.State = word.ParseState;
            if (word.Tokens > entry.FormTokens || word.Tokens == entry.FormTokens && string.CompareOrdinal(word.Form, entry.Form) < 0)
            {
                entry.Form = word.Form;
                entry.FormTokens = word.Tokens;
            }
        }
        return entries;
    }

    private static int Rank(string state) => state switch { Approved => 2, Parsed => 1, _ => 0 };

    private static Func<string, string?> BuildMatcher(GroupSpec spec, bool forward, Func<string, string> fold, out string query)
    {
        var hasText = !string.IsNullOrEmpty(spec.Text);
        var hasPattern = !string.IsNullOrEmpty(spec.Pattern);
        if (hasText == hasPattern)
            throw new ArgumentException(forward ? "Give exactly one of --ending and --pattern." : "Give exactly one of --start and --pattern.");
        if (hasPattern)
        {
            if (spec.Pattern!.Length > MaxPatternLength)
                throw new ArgumentException($"The pattern is longer than {MaxPatternLength} characters.");
            Regex regex;
            try
            {
                regex = new Regex("^(?:" + spec.Pattern + ")$", RegexOptions.NonBacktracking | RegexOptions.CultureInvariant,
                    PatternTimeout);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                throw new ArgumentException("The pattern is not valid: " + exception.Message);
            }
            if (!regex.GetGroupNames().Contains("stem", StringComparer.Ordinal))
                throw new ArgumentException("The pattern needs one named group for the stem, for example (?<stem>.+).");
            query = "pattern " + spec.Pattern;
            return key =>
            {
                Match found;
                try { found = regex.Match(key); }
                catch (RegexMatchTimeoutException) { return null; }
                var stem = found.Groups["stem"];
                return found.Success && stem.Success && stem.Length > 0 && IsBoundary(key, stem.Index) &&
                    IsBoundary(key, stem.Index + stem.Length) ? stem.Value : null;
            };
        }
        var text = spec.Text!.Trim(' ', '-');
        var folded = fold(text);
        if (folded.Length == 0) throw new ArgumentException("The ending or start is empty.");
        query = (forward ? "ending " : "start ") + folded;
        return forward
            ? key => key.Length > folded.Length && key.EndsWith(folded, StringComparison.Ordinal) &&
                IsBoundary(key, key.Length - folded.Length) ? key[..^folded.Length] : null
            : key => key.Length > folded.Length && key.StartsWith(folded, StringComparison.Ordinal) &&
                IsBoundary(key, folded.Length) ? key[folded.Length..] : null;
    }

    private static bool IsBoundary(string text, int index)
    {
        if (index <= 0 || index >= text.Length) return true;
        return Array.BinarySearch(StringInfo.ParseCombiningCharacters(text), index) >= 0;
    }

    private static string Reverse(string text)
    {
        var elements = StringInfo.ParseCombiningCharacters(text);
        var builder = new StringBuilder(text.Length);
        for (var i = elements.Length - 1; i >= 0; i--)
            builder.Append(text, elements[i], (i + 1 < elements.Length ? elements[i + 1] : text.Length) - elements[i]);
        return builder.ToString();
    }

    // Sorted by start for a suffix and by reversed end for a prefix, so words sharing a stem are adjacent.
    private sealed class OrientedIndex
    {
        private readonly bool _forward;
        private readonly (string Probe, string Key)[] _items;

        public OrientedIndex(string[] keys, bool forward)
        {
            _forward = forward;
            _items = forward ? [.. keys.Select(key => (key, key))] : [.. keys.Select(key => (Reverse(key), key))
                .OrderBy(item => item.Item1, StringComparer.Ordinal)];
        }

        public IReadOnlyList<string> Siblings(string stem, HashSet<string> own, int sample, out int total)
        {
            var probe = _forward ? stem : Reverse(stem);
            var low = 0;
            var high = _items.Length;
            while (low < high)
            {
                var mid = (low + high) >>> 1;
                if (string.CompareOrdinal(_items[mid].Probe, probe) < 0) low = mid + 1; else high = mid;
            }
            var found = new List<string>();
            total = 0;
            for (var i = low; i < _items.Length && _items[i].Probe.StartsWith(probe, StringComparison.Ordinal); i++)
            {
                var key = _items[i].Key;
                if (key == stem || own.Contains(key)) continue;
                if (!IsBoundary(key, _forward ? stem.Length : key.Length - stem.Length)) continue;
                total++;
                if (found.Count < sample) found.Add(key);
            }
            return found;
        }
    }
}
