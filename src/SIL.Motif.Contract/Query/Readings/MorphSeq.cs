namespace SIL.Motif.Contract.Query.Readings;

/// <summary>
/// One item of <c>morph(...)</c> or <c>seq(...)</c>, read from its text. The kinds are <c>entry</c>, <c>msa</c>, <c>form</c> (a guid),
/// <c>id</c> (a bare guid, an analysis or a form id), <c>root</c>, <c>pfx</c>, <c>sfx</c> (a form of the project's lexicon, resolved to its entry),
/// <c>text</c> (the morph's own surface text) and <c>class</c> (any other token: a gloss, class or inflection type).
/// </summary>
/// <param name="Kind">The kind.</param>
/// <param name="Value">What follows the kind prefix.</param>
/// <param name="Text">The item as written.</param>
public sealed record MorphItem(string Kind, string Value, string Text)
{
    /// <summary>The prefixes, in the order help lists them.</summary>
    public static IReadOnlyList<string> Prefixes { get; } = ["entry", "msa", "form", "root", "pfx", "sfx", "text"];

    /// <summary>Reads an item; a bare guid is an <c>id</c> and any other bare token a <c>class</c>.</summary>
    public static MorphItem Parse(string text)
    {
        var trimmed = text.Trim();
        var at = trimmed.IndexOf(':');
        if (at > 0)
        {
            var prefix = trimmed[..at].ToLowerInvariant();
            var value = trimmed[(at + 1)..].Trim();
            if (prefix is "stem" or "afx") prefix = prefix == "stem" ? "root" : "pfx";
            if (Prefixes.Contains(prefix) && value.Length > 0)
            {
                if (prefix is "entry" or "msa" or "form" && Guid.TryParse(value, out var guid)) value = guid.ToString("D");
                return new MorphItem(prefix, value, trimmed);
            }
        }
        return Guid.TryParse(trimmed, out var bare) ? new MorphItem("id", bare.ToString("D"), trimmed) : new MorphItem("class", trimmed, trimmed);
    }

    /// <summary>Whether the item names a lexicon form that only a project can resolve to an entry.</summary>
    public bool NeedsLexiconForm => Kind is "root" or "pfx" or "sfx";

    /// <summary>Whether matching the item needs the project: its morphs' entries, glosses or classes.</summary>
    public bool NeedsProject => Kind is "entry" or "root" or "pfx" or "sfx" or "class";

    /// <summary>The test a morph must pass; <paramref name="entry"/> is the entry id a <c>root</c>, <c>pfx</c> or <c>sfx</c> item resolved to.</summary>
    public Func<ReadingMorph, bool> Matcher(string? entry = null)
    {
        switch (Kind)
        {
            case "entry": return morph => string.Equals(morph.Entry, Value, StringComparison.OrdinalIgnoreCase);
            case "msa": case "form": case "id":
                var target = new ReadingTarget(Kind, Value);
                return target.Matches;
            case "root": case "pfx": case "sfx":
                return morph => entry is not null && string.Equals(morph.Entry, entry, StringComparison.OrdinalIgnoreCase);
            case "text": return morph => string.Equals(morph.Text, Value, StringComparison.OrdinalIgnoreCase);
            default:
                return morph => string.Equals(morph.Gloss, Value, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(morph.Kind, Value, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(morph.InflType, Value, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Whether the reading's morphs hold <paramref name="items"/> in order, each morph used once. Without <paramref name="adjacent"/> gaps are allowed, and
    /// taking the earliest morph for each item is exact; with it the matched morphs are consecutive, so every start is tried.
    /// </summary>
    public static bool Matches(IReadOnlyList<ReadingMorph> morphs, IReadOnlyList<Func<ReadingMorph, bool>> items, bool adjacent)
    {
        if (items.Count == 0) return false;
        if (adjacent)
        {
            for (var start = 0; start + items.Count <= morphs.Count; start++)
            {
                var all = true;
                for (var k = 0; k < items.Count && all; k++) all = items[k](morphs[start + k]);
                if (all) return true;
            }
            return false;
        }
        var next = 0;
        foreach (var morph in morphs)
            if (items[next](morph) && ++next == items.Count) return true;
        return false;
    }
}

/// <summary>The compiled <c>morph</c> and <c>seq</c> predicates of a query, each a test of one reading, addressed by the column a lowered predicate tests.</summary>
public sealed class ReadingTests
{
    private readonly List<Func<WordReading, bool>> _tests = [];

    /// <summary>Adds a test and returns the column that stands for it.</summary>
    public string Add(Func<WordReading, bool> test)
    {
        _tests.Add(test);
        return MorphSeqLowering.ColumnPrefix + (_tests.Count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The test a lowered column stands for.</summary>
    public Func<WordReading, bool>? Of(string column) =>
        MorphSeqLowering.TryIndex(column, out var index) && index < _tests.Count ? _tests[index] : null;

    /// <summary>The word has a reading that passes the test.</summary>
    public bool Word(string column, WordReadings word) => Of(column) is { } test && word.Readings.Any(test);

    /// <summary>
    /// The test of one reading against the lowered expression: a <c>morph</c> or <c>seq</c> leaf answers for the reading, and and and or combine them.
    /// A <c>not</c> and every other predicate say nothing about a single reading and pass, so the readings kept are the ones that explain a match.
    /// </summary>
    public Func<WordReading, bool> ReadingFilter(QueryExpr? lowered)
    {
        return reading => Eval(lowered, reading);

        bool Eval(QueryExpr? node, WordReading reading) => node switch
        {
            null => true,
            QueryField field when MorphSeqLowering.IsColumn(field.Field) => Of(field.Field)?.Invoke(reading) ?? true,
            QueryAnd and => and.Items.All(item => Eval(item, reading)),
            QueryOr or => or.Items.Any(item => Eval(item, reading)),
            _ => true,
        };
    }
}

/// <summary>Replaces each <c>morph</c> and <c>seq</c> by a test of the synthetic column <c>seq:N</c>, which a corpus row answers from the word's readings.</summary>
public static class MorphSeqLowering
{
    /// <summary>The column prefix.</summary>
    public const string ColumnPrefix = "seq:";

    /// <summary>Whether the column is one a <c>morph</c> or <c>seq</c> lowers to.</summary>
    public static bool IsColumn(string column) => column.StartsWith(ColumnPrefix, StringComparison.Ordinal);

    /// <summary>The index a lowered column carries.</summary>
    public static bool TryIndex(string column, out int index)
    {
        index = -1;
        return IsColumn(column) && int.TryParse(column[ColumnPrefix.Length..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out index);
    }

    /// <summary>Every <c>morph</c> and <c>seq</c>, with whether a <c>not</c> encloses it an odd number of times.</summary>
    public static List<(QueryExpr Node, bool Negated)> Find(QueryExpr? expr)
    {
        var found = new List<(QueryExpr, bool)>();
        Walk(expr, false);
        return found;

        void Walk(QueryExpr? node, bool negated)
        {
            switch (node)
            {
                case QueryMorphIs or QueryMorphSeq: found.Add((node, negated)); break;
                case QueryAnd and: foreach (var item in and.Items) Walk(item, negated); break;
                case QueryOr or: foreach (var item in or.Items) Walk(item, negated); break;
                case QueryNot not: Walk(not.Item, !negated); break;
            }
        }
    }

    /// <summary>Whether the expression holds a <c>morph</c> or <c>seq</c>.</summary>
    public static bool Has(QueryExpr? expr) => Find(expr).Count > 0;

    /// <summary>The items of a <c>morph</c> or <c>seq</c> node.</summary>
    public static IReadOnlyList<string> ItemsOf(QueryExpr node) => node switch
    {
        QueryMorphIs morph => [morph.Item],
        QueryMorphSeq seq => seq.Items,
        _ => [],
    };

    /// <summary>Lowers every node; <paramref name="column"/> returns the column that stands for a node.</summary>
    public static QueryExpr? Lower(QueryExpr? expr, Func<QueryExpr, string> column) => expr is null ? null : Visit(expr, column);

    private static QueryExpr Visit(QueryExpr node, Func<QueryExpr, string> column) => node switch
    {
        QueryMorphIs or QueryMorphSeq => new QueryField(column(node), QueryOps.Eq, "yes"),
        QueryAnd and => new QueryAnd([.. and.Items.Select(item => Visit(item, column))]),
        QueryOr or => new QueryOr([.. or.Items.Select(item => Visit(item, column))]),
        QueryNot not => new QueryNot(Visit(not.Item, column)),
        _ => node,
    };
}
