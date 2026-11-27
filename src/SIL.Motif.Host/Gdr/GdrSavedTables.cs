using System.Globalization;
using System.Text.RegularExpressions;
using SIL.Motif.Contract.Gdr;
using SIL.Motif.Host.RichText;
using SIL.TextEditing;

namespace SIL.Motif.Host.Gdr;

/// <summary>What a saved table's caption line says.</summary>
/// <param name="Date">The day it was saved, <c>yyyy-MM-dd</c>.</param>
/// <param name="BaselineId">The short id of the Baseline the rows were computed from.</param>
/// <param name="AtLeast">Whether the count is a lower bound because the parse behind it was incomplete.</param>
/// <param name="Rows">The rows the query had in all, which may be more than the table holds.</param>
public sealed record GdrSavedTableCaption(string Date, string BaselineId, bool AtLeast, long Rows);

/// <summary>A <c>motif-query</c> block in a run of paragraphs, and the saved table right after it, when it has one.</summary>
/// <param name="QueryId">The block's <c>id</c>.</param>
/// <param name="BlockFirst">The paragraph holding the block's opening fence.</param>
/// <param name="BlockLast">The paragraph holding its closing fence.</param>
/// <param name="TableFirst">The caption paragraph of the saved table; -1 when the block has none.</param>
/// <param name="TableCount">The paragraphs of the saved table, the caption and the header row and the body rows; 0 when none.</param>
/// <param name="Caption">The caption's parts; null when the block has no saved table.</param>
public sealed record GdrSavedTableSite(string QueryId, int BlockFirst, int BlockLast, int TableFirst, int TableCount, GdrSavedTableCaption? Caption)
{
    /// <summary>Whether the block carries a saved table.</summary>
    public bool HasTable => Caption is not null;
}

/// <summary>
/// The saved table of a Guide <c>motif-query</c> block (ADR 0005). A table is stored right after its block as plain FieldWorks
/// paragraphs: one caption paragraph, then a pipe table (ADR 0006), a header row in the <c>Table Header</c> style and body rows in
/// <c>Table Row</c>. What marks it as the block's own is its place and its caption: a paragraph that reads exactly
/// "Saved &lt;date&gt; from Baseline &lt;id&gt;; &lt;N&gt; rows in all; Motif recomputes this when shown." straight after the block's closing fence,
/// followed by a table-header paragraph. FieldWorks keeps both, so the mark survives any round trip through it; text a person types
/// that does not read like the caption is ordinary authored text. Nothing here reads a project or runs a query.
/// </summary>
public static class GdrSavedTables
{
    /// <summary>The most body rows a saved table holds.</summary>
    public const int MaxRows = 10;

    private const string Tail = "; Motif recomputes this when shown.";

    private static readonly Regex CaptionPattern = new(
        @"^Saved (\d{4}-\d{2}-\d{2}) from Baseline ([0-9A-Za-z]+); (at least )?(\d[\d,]*) rows in all; Motif recomputes this when shown\.$",
        RegexOptions.CultureInvariant);

    /// <summary>The caption line of a table whose query has <paramref name="rows"/> rows in all.</summary>
    public static string Caption(DateOnly date, string baselineId, bool atLeast, long rows) =>
        $"Saved {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} from Baseline {baselineId}; " +
        $"{(atLeast ? "at least " : "")}{rows.ToString("N0", CultureInfo.InvariantCulture)} rows in all{Tail[..^1]}.";

    /// <summary>Reads a caption line; false when the text is anything else.</summary>
    public static bool TryParseCaption(string? text, out GdrSavedTableCaption caption)
    {
        caption = null!;
        if (text is null || CaptionPattern.Match(text) is not { Success: true } match) return false;
        var rows = long.Parse(match.Groups[4].Value.Replace(",", "", StringComparison.Ordinal), CultureInfo.InvariantCulture);
        caption = new GdrSavedTableCaption(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Success, rows);
        return true;
    }

    /// <summary>The short id a caption gives a Baseline: the first twelve hex digits of its bundle digest; <c>none</c> without one.</summary>
    public static string ShortBaselineId(string? bundleDigest)
    {
        if (string.IsNullOrEmpty(bundleDigest)) return "none";
        var hex = bundleDigest.StartsWith("sha256:", StringComparison.Ordinal) ? bundleDigest["sha256:".Length..] : bundleDigest;
        return hex[..Math.Min(12, hex.Length)];
    }

    /// <summary>Whether a caption's Baseline id names the Baseline whose digest is <paramref name="bundleDigest"/>.</summary>
    public static bool IsBaseline(GdrSavedTableCaption caption, string? bundleDigest) =>
        string.Equals(caption.BaselineId, ShortBaselineId(bundleDigest), StringComparison.OrdinalIgnoreCase);

    /// <summary>The query blocks among the paragraphs, each with its saved table.</summary>
    public static IReadOnlyList<GdrSavedTableSite> Sites(IReadOnlyList<Paragraph> paragraphs) =>
        Sites(paragraphs.Select(p => string.Concat(p.Runs.Select(run => run.Text))).ToArray(), paragraphs.Select(p => p.Style).ToArray());

    /// <summary>The query blocks among the paragraph texts and styles, in order, each with its saved table.</summary>
    public static IReadOnlyList<GdrSavedTableSite> Sites(IReadOnlyList<string> texts, IReadOnlyList<string?> styles)
    {
        var starts = new int[texts.Count];
        var offset = 0;
        for (var i = 0; i < texts.Count; i++) { starts[i] = offset; offset += texts[i].Length + 1; }
        var sites = new List<GdrSavedTableSite>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var query in GuideQueryBlocks.Parse(string.Join("\n", texts)).Queries.OrderBy(q => q.Span.Start))
        {
            if (!seen.Add(query.Id)) continue;
            var first = Array.IndexOf(starts, query.Span.Start);
            var last = Array.FindIndex(starts, Math.Max(first, 0), start => start + texts[Array.IndexOf(starts, start)].Length == query.Span.End);
            if (first < 0 || last < first) continue;
            var caption = last + 2 < texts.Count && IsHeader(styles[last + 2]) && TryParseCaption(texts[last + 1], out var found) ? found : null;
            var count = 0;
            if (caption is not null)
            {
                count = 2;
                while (last + 1 + count < texts.Count && styles[last + 1 + count] == MotifMarkdownStyles.TableRow) count++;
            }
            sites.Add(new GdrSavedTableSite(query.Id, first, last, caption is null ? -1 : last + 1, count, caption));
        }
        return sites;
    }

    private static bool IsHeader(string? style) => style == MotifMarkdownStyles.TableHeader;

    /// <summary>The paragraphs of a saved table: the caption, the header row of <paramref name="columns"/>, and each of <paramref name="rows"/>.</summary>
    /// <param name="caption">The caption line.</param>
    /// <param name="columns">The column names.</param>
    /// <param name="rows">The rows, each as many cells as columns.</param>
    /// <param name="analysisWs">The writing system of the caption and of ordinary cells.</param>
    /// <param name="vernacularWs">The writing system of cells in a column that holds forms; the analysis one when null.</param>
    /// <param name="vernacularColumn">Whether a column holds forms.</param>
    public static IReadOnlyList<Paragraph> Build(string caption, IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> rows,
        string analysisWs, string? vernacularWs, Func<string, bool> vernacularColumn)
    {
        var result = new List<Paragraph> { Plain(caption, analysisWs) };
        result.Add(Row(MotifMarkdownStyles.TableHeader, columns, columns, analysisWs, null, _ => false));
        foreach (var row in rows) result.Add(Row(MotifMarkdownStyles.TableRow, columns, row, analysisWs, vernacularWs, vernacularColumn));
        return result;
    }

    private static Paragraph Plain(string text, string ws)
    {
        var paragraph = new Paragraph { DefaultWs = ws, Style = "Normal" };
        paragraph.Runs.Add(new Run { Ws = ws, Text = text });
        return paragraph;
    }

    private static Paragraph Row(string style, IReadOnlyList<string> columns, IReadOnlyList<string> cells, string analysisWs, string? vernacularWs,
        Func<string, bool> vernacularColumn)
    {
        var paragraph = new Paragraph { DefaultWs = analysisWs, Style = style };
        for (var i = 0; i < columns.Count; i++)
        {
            if (i > 0) paragraph.Runs.Add(new Run { Ws = analysisWs, Text = "\t" });
            var text = Clean(i < cells.Count ? cells[i] : "");
            if (text.Length > 0)
                paragraph.Runs.Add(new Run { Ws = vernacularWs is not null && vernacularColumn(columns[i]) ? vernacularWs : analysisWs, Text = text });
        }
        return paragraph;
    }

    // A cell cannot hold a tab or a line break: the tab is the cell separator when a row is stored.
    private static string Clean(string text) => text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Replace('\u2028', ' ').Trim();

    /// <summary>
    /// <paramref name="paragraphs"/> with the saved table of each query in <paramref name="tables"/> replaced by the paragraphs given,
    /// or removed when the value is null; a block that had none gets the new table right after its closing fence.
    /// </summary>
    public static IReadOnlyList<Paragraph> Replace(IReadOnlyList<Paragraph> paragraphs, IReadOnlyDictionary<string, IReadOnlyList<Paragraph>?> tables)
    {
        var result = paragraphs.ToList();
        foreach (var site in Sites(paragraphs).OrderByDescending(site => site.BlockLast))
        {
            if (!tables.TryGetValue(site.QueryId, out var table)) continue;
            if (site.HasTable) result.RemoveRange(site.TableFirst, site.TableCount);
            if (table is not null) result.InsertRange(site.BlockLast + 1, table);
        }
        return result;
    }

    /// <summary>
    /// What <c>guide edit</c> reads from Markdown: the saved tables of <paramref name="read"/> are not authored text, so each block keeps
    /// the table <paramref name="original"/> holds for it, a block with none gets none, and a block the edit removes takes its table with it.
    /// A change to a table is dropped and named in <paramref name="warnings"/>.
    /// </summary>
    public static IReadOnlyList<Paragraph> Pin(IReadOnlyList<Paragraph> original, IReadOnlyList<Paragraph> read, List<string> warnings)
    {
        var held = Sites(original).Where(site => site.HasTable).ToDictionary(site => site.QueryId, StringComparer.Ordinal);
        var sites = Sites(read);
        var result = new List<Paragraph>();
        var at = 0;
        foreach (var site in sites.OrderBy(site => site.BlockLast))
        {
            result.AddRange(read.Skip(at).Take(site.BlockLast + 1 - at));
            at = site.BlockLast + 1;
            if (site.HasTable)
            {
                var same = held.TryGetValue(site.QueryId, out var before) && SameTable(original, before, read, site);
                if (!same)
                    warnings.Add($"the saved table of query {site.QueryId} is not authored text, so your change to it was ignored; " +
                        "'guide refresh-tables' rewrites it.");
                at = site.TableFirst + site.TableCount;
            }
            if (held.TryGetValue(site.QueryId, out var kept))
            {
                result.AddRange(original.Skip(kept.TableFirst).Take(kept.TableCount).Select(paragraph => paragraph.Clone()));
                if (!site.HasTable)
                {
                    // Text typed between block and table leaves the old table later on; drop it, never keep two.
                    var limit = sites.Where(next => next.BlockFirst > site.BlockLast).Select(next => next.BlockFirst).DefaultIfEmpty(read.Count).Min();
                    if (OrphanRun(read, at, limit) is { } orphan)
                    {
                        result.AddRange(read.Skip(at).Take(orphan.First - at));
                        at = orphan.First + orphan.Count;
                        warnings.Add($"the saved table of query {site.QueryId} must follow its block directly, so the copy after your text was dropped; " +
                            "'guide refresh-tables' rewrites the table.");
                    }
                }
            }
        }
        result.AddRange(read.Skip(at));
        return result;
    }

    private static (int First, int Count)? OrphanRun(IReadOnlyList<Paragraph> read, int from, int limit)
    {
        string Text(int index) => string.Concat(read[index].Runs.Select(run => run.Text));
        for (var i = from; i + 1 < limit; i++)
        {
            if (!TryParseCaption(Text(i), out _) || !IsHeader(read[i + 1].Style)) continue;
            var end = i + 2;
            while (end < limit && read[end].Style == MotifMarkdownStyles.TableRow) end++;
            return (i, end - i);
        }
        return null;
    }

    private static bool SameTable(IReadOnlyList<Paragraph> left, GdrSavedTableSite leftSite, IReadOnlyList<Paragraph> right, GdrSavedTableSite rightSite)
    {
        if (leftSite.TableCount != rightSite.TableCount) return false;
        for (var i = 0; i < leftSite.TableCount; i++)
        {
            var a = left[leftSite.TableFirst + i];
            var b = right[rightSite.TableFirst + i];
            if (a.Style != b.Style || string.Concat(a.Runs.Select(run => run.Text)) != string.Concat(b.Runs.Select(run => run.Text))) return false;
        }
        return true;
    }

    /// <summary>The indexes of the paragraphs that are a saved table's, for a reader that must not take them as authored text.</summary>
    public static HashSet<int> TableIndexes(IReadOnlyList<string> texts, IReadOnlyList<string?> styles)
    {
        var set = new HashSet<int>();
        foreach (var site in Sites(texts, styles).Where(site => site.HasTable))
            for (var i = 0; i < site.TableCount; i++) set.Add(site.TableFirst + i);
        // A table whose block is gone or no longer precedes it is still computed text, so no example is read from it.
        for (var i = 0; i + 1 < texts.Count; i++)
        {
            if (!TryParseCaption(texts[i], out _) || !IsHeader(styles[i + 1])) continue;
            var end = i + 2;
            while (end < texts.Count && styles[end] == MotifMarkdownStyles.TableRow) end++;
            for (var at = i; at < end; at++) set.Add(at);
            i = end - 1;
        }
        return set;
    }
}
