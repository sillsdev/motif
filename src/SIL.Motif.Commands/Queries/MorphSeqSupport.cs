using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Query.Corpus;
using SIL.Motif.Contract.Query.Readings;

namespace SIL.Motif.Commands.Queries;

/// <summary>What a <c>morph</c> or <c>seq</c> query needs and what it compiled to.</summary>
/// <param name="Lowered">The expression with each predicate replaced by a test of its synthetic column.</param>
/// <param name="Tests">The per-reading tests behind those columns.</param>
/// <param name="Items">Each item the expression names with the test of a morph.</param>
internal sealed record MorphSeqPlan(QueryExpr? Lowered, ReadingTests Tests, IReadOnlyList<(MorphItem Item, Func<ReadingMorph, bool> Matcher)> Items);

/// <summary>Compiles <c>morph(...)</c> and <c>seq(...)</c> over the shared corpus parse, and counts the sequences of the matching readings.</summary>
internal static class MorphSeqSupport
{
    /// <summary>Every distinct item the expression names.</summary>
    public static List<MorphItem> ItemsOf(QueryExpr? where) =>
        [.. MorphSeqLowering.Find(where).SelectMany(found => MorphSeqLowering.ItemsOf(found.Node)).Select(MorphItem.Parse).DistinctBy(item => item.Kind + ":" + item.Value, StringComparer.Ordinal)];

    /// <summary>Compiles the expression; <paramref name="entryOf"/> names the entry of a lexicon form item.</summary>
    public static MorphSeqPlan Compile(QueryExpr where, bool adjacentAll, Func<MorphItem, string?> entryOf)
    {
        var tests = new ReadingTests();
        var items = new List<(MorphItem, Func<ReadingMorph, bool>)>();
        Func<ReadingMorph, bool> Matcher(string text)
        {
            var item = MorphItem.Parse(text);
            var matcher = item.Matcher(item.NeedsLexiconForm ? entryOf(item) : null);
            items.Add((item, matcher));
            return matcher;
        }
        var lowered = MorphSeqLowering.Lower(where, node => node switch
        {
            QueryMorphIs morph => tests.Add(CompileSeq([Matcher(morph.Item)], false)),
            QueryMorphSeq seq => tests.Add(CompileSeq([.. seq.Items.Select(Matcher)], seq.Adjacent || adjacentAll)),
            _ => throw new ArgumentException("Not a morph or seq."),
        });
        return new MorphSeqPlan(lowered, tests, items);
    }

    private static Func<WordReading, bool> CompileSeq(IReadOnlyList<Func<ReadingMorph, bool>> matchers, bool adjacent) =>
        reading => MorphItem.Matches(reading.Morphs, matchers, adjacent);

    /// <summary>The first item that no morph of any word of the parse matches, or null when each matches something.</summary>
    public static MorphItem? FirstUnmatched(MorphSeqPlan plan, IReadingSource readings, IEnumerable<string> words)
    {
        var open = plan.Items.ToList();
        foreach (var word in words)
        {
            if (open.Count == 0) return null;
            if (readings.Get(word) is not { } entry) continue;
            foreach (var morph in entry.Readings.SelectMany(reading => reading.Morphs))
            {
                open.RemoveAll(pair => pair.Matcher(morph));
                if (open.Count == 0) return null;
            }
        }
        return open.Count == 0 ? null : open[0].Item;
    }

    /// <summary>One morph of a sequence: its text, then <c>/</c> and its gloss when the project gives one; the head of its analysis id when no text was placed.</summary>
    public static string Label(ReadingMorph morph) =>
        (morph.Text.Length > 0 ? morph.Text : morph.Msa.Length >= 8 ? morph.Msa[..8] : morph.Msa) + (morph.Gloss is { Length: > 0 } ? "/" + morph.Gloss : "");

    /// <summary>
    /// Counts the distinct morph sequences among the readings of the rows that pass <paramref name="keep"/>; a word counts once per distinct sequence.
    /// </summary>
    public static MorphSeqReport Count(IReadOnlyList<CorpusFormRow> rows, IReadingSource readings, Func<WordReading, bool> keep, bool complete, int limit)
    {
        var groups = new Dictionary<string, (IReadOnlyList<string> Seq, int Types, long Tokens, List<(string Word, long Tokens)> Examples)>(StringComparer.Ordinal);
        var words = 0;
        long tokens = 0;
        foreach (var row in rows)
        {
            if (readings.Get(row.Form) is not { } entry) continue;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var reading in entry.Readings)
            {
                if (!keep(reading)) continue;
                var labels = reading.Morphs.Select(Label).ToArray();
                var key = string.Join("\u001f", labels);
                if (!seen.Add(key)) continue;
                if (!groups.TryGetValue(key, out var group)) group = (labels, 0, 0, []);
                group.Examples.Add((row.Form, row.Tokens));
                groups[key] = (group.Seq, group.Types + 1, group.Tokens + row.Tokens, group.Examples);
            }
            if (seen.Count > 0) { words++; tokens += row.Tokens; }
        }
        var ordered = groups.Values.OrderByDescending(group => group.Tokens).ThenByDescending(group => group.Types)
            .ThenBy(group => string.Join(" ", group.Seq), StringComparer.Ordinal).ToList();
        return new MorphSeqReport(complete, words, tokens, ordered.Count, [.. ordered.Take(limit).Select(group => new MorphSeqEntry(group.Seq, group.Types, group.Tokens,
            [.. group.Examples.OrderByDescending(example => example.Tokens).ThenBy(example => example.Word, StringComparer.Ordinal).Take(5).Select(example => example.Word)]))]);
    }
}

/// <summary>Refusals of <c>morph</c> and <c>seq</c>.</summary>
internal static class MorphSeqRefusals
{
    public static QueryRefusalException NeedsProject(MorphItem item, string? failure) => new(new Refusal(RefusalCodes.QueryReadingsNeedsProject, FailureReason.InvalidArgument,
        $"'{item.Text}' needs the project: PanGloss's batch gives each morph its form and analysis ids, and only the project's lexicon names its entry, gloss and class. " +
        "Pass --project <fwdata> (or a .fwdata as --grammar), or name the morph as msa:<id>, form:<id> or text:<surface>." + (failure is null ? "" : " " + failure),
        new Dictionary<string, string>(StringComparer.Ordinal) { ["flag"] = "--project", ["item"] = item.Text }));

    public static QueryRefusalException ItemUnknown(MorphItem item, bool hasProject) => new(new Refusal(RefusalCodes.QuerySeqItemUnknown, FailureReason.InvalidArgument,
        $"'{item.Text}' matches no morph of any reading in the corpus parse, so the query would silently answer nothing." + item.Kind switch
        {
            "class" => hasProject
                ? " A class token matches an entry gloss, a class (stem, derivational, inflectional, affix) or an inflection type, ignoring case; check the spelling against the project's glosses."
                : " A class token matches the project's entry glosses and classes; pass --project <fwdata>.",
            "text" => " text: matches a morph's own surface text exactly (ignoring case).",
            _ => "",
        },
        new Dictionary<string, string>(StringComparer.Ordinal) { ["item"] = item.Text }));

    public static QueryRefusalException NegationIncomplete(ReadingsReport report) => new(new Refusal(RefusalCodes.QuerySeqNegationIncomplete, FailureReason.Refused,
        $"not morph(...) and not seq(...) are refused: the corpus parse is not complete ({report.Capped} capped, {report.TimedOut} timed out, {report.Failed} unanswered words" +
        (report.UnknownMsa > 0 ? $", {report.UnknownMsa} analyses unknown to the project" : "") + "), so a word having no such reading would be a claim the parse cannot back.",
        new Dictionary<string, string>(StringComparer.Ordinal) { ["capped"] = report.Capped.ToString(System.Globalization.CultureInfo.InvariantCulture), ["failed"] = report.Failed.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
}

/// <summary>Expands <c>realizes(@file, tier)</c>: the file holds one morpheme spec per line, and the predicate means any of them.</summary>
internal static class RealizesFiles
{
    /// <summary>The expression with each <c>@file</c> morpheme replaced by the realizes predicates of its lines; the new nodes are returned in <paramref name="listed"/>.</summary>
    public static QueryExpr? Expand(QueryExpr? expr, HashSet<QueryRealizes> listed) => expr switch
    {
        null => null,
        QueryRealizes realizes when realizes.Morpheme.StartsWith('@') => ExpandOne(realizes, listed),
        QueryAnd and => new QueryAnd([.. and.Items.Select(item => Expand(item, listed)!)]),
        QueryOr or => new QueryOr([.. or.Items.Select(item => Expand(item, listed)!)]),
        QueryNot not => new QueryNot(Expand(not.Item, listed)!),
        _ => expr,
    };

    /// <summary>Whether the expression has a stems file.</summary>
    public static bool Has(QueryExpr? expr) => RealizesLowering.Find(expr).Any(found => found.Node.Morpheme.StartsWith('@'));

    private static QueryExpr ExpandOne(QueryRealizes node, HashSet<QueryRealizes> listed)
    {
        var path = node.Morpheme[1..].Trim();
        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new QueryRefusalException(new Refusal(RefusalCodes.QueryCorpusUnreadable, FailureReason.InvalidArgument,
                $"The stems file '{path}' cannot be read: {exception.Message}"));
        }
        var specs = lines.Select(line => line.Trim().TrimStart('﻿')).Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Distinct(StringComparer.Ordinal).ToList();
        if (specs.Count == 0)
            throw new QueryRefusalException(new Refusal(RefusalCodes.QueryCorpusInvalid, FailureReason.InvalidArgument,
                $"The stems file '{path}' has no morpheme spec; give one per line (blank lines and lines starting # are skipped)."));
        var nodes = specs.Select(spec => new QueryRealizes(spec, node.Tier)).ToList();
        foreach (var item in nodes) listed.Add(item);
        return nodes.Count == 1 ? nodes[0] : new QueryOr([.. nodes]);
    }
}
