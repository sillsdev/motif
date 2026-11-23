using System.Globalization;
using System.Text;
using SIL.Motif.Contract.Query.Corpus;

namespace SIL.Motif.Cli.Rendering;

/// <summary>Renders a corpus <c>pairs</c> result as readable tables.</summary>
internal static class PairsText
{
    public static string Render(PairsQueryResponse response)
    {
        var text = new StringBuilder();
        var corpus = response.Corpus;
        text.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "Corpus {0}: {1} types, {2} tokens, segmentation {3}{4}", corpus.Source, corpus.Types, corpus.Tokens, corpus.Segmentation,
            corpus.FoldCase ? ", case folded" : ""));
        text.AppendLine(string.Format(CultureInfo.InvariantCulture, "Pairs differing by {0}: {1}{2}, X up to {3} segments, {4} pairs",
            response.Differ, response.XPattern is null ? "any X" : "X ~ " + response.XPattern,
            response.Having.Count == 0 ? "" : ", bases having all of " + string.Join(",", response.Having), response.MaxXLength, response.PairCount));
        text.AppendLine();
        var rows = response.Xs.Select(x => new[]
        {
            x.X, x.Bases.ToString(CultureInfo.InvariantCulture), x.Tokens.ToString(CultureInfo.InvariantCulture),
            x.BaseTokens.ToString(CultureInfo.InvariantCulture),
            string.Join(", ", x.Examples.Select(e => e.Base + " > " + e.Derived)),
        }).ToList();
        Table(text, ["X", "bases", "tokens", "base tokens", "examples"], rows);
        if (response.Bases.Count > 0)
        {
            text.AppendLine();
            Table(text, ["base", "tokens", "Xs"], response.Bases.Select(b => new[]
                { b.Base, b.Tokens.ToString(CultureInfo.InvariantCulture), string.Join(" ", b.Xs) }).ToList());
        }
        var cut = response.Truncation;
        if (cut.Truncated)
        {
            text.AppendLine();
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "TRUNCATED: showing {0} of {1} Xs and {2} of {3} bases (cap {4}).", cut.XsShown, cut.XsTotal, cut.BasesShown, cut.BasesTotal, response.MaxResults));
        }
        return text.ToString();
    }

    private static void Table(StringBuilder text, string[] header, List<string[]> rows)
    {
        var widths = header.Select((cell, i) => Math.Max(cell.Length, rows.Select(row => row[i].Length).DefaultIfEmpty(0).Max())).ToArray();
        void Line(string[] row) => text.AppendLine(string.Join("  ", row.Select((cell, i) => i == row.Length - 1 ? cell : cell.PadRight(widths[i]))).TrimEnd());
        Line(header);
        foreach (var row in rows) Line(row);
    }
}
