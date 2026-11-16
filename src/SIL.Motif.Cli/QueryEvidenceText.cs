using System.Globalization;
using SIL.Motif.Contract.Query;

namespace SIL.Motif.Cli;

/// <summary>Writes the split contexts and the evidence screen of a query as plain lines.</summary>
internal static class QueryEvidenceText
{
    private static string Yes(bool? value) => value is { } flag ? (flag ? "yes" : "no") : "-";

    public static IEnumerable<string> Contexts(IEnumerable<QueryContext> contexts)
    {
        foreach (var context in contexts)
        {
            var depth = context.Depth == 2 ? $"then, outside {context.Outside}: " : "";
            if (context.Status == "too-few-to-tell")
            {
                yield return $"  [too few to tell] {depth}{context.Label}\tscope={context.Scope} deviants={context.Deviants}";
                continue;
            }
            var lower = context.Lower is { } value ? value.ToString("0.###", CultureInfo.InvariantCulture) : "-";
            yield return $"  [{context.Status}] {depth}{context.Label}\tscope={context.Scope} deviants={context.Deviants} lower={lower} " +
                $"covers={Yes(context.Covers)} reliable={Yes(context.Reliable)} smaller={Yes(context.Smaller)} " +
                $"expressible={context.Expressible} held-out={context.HeldOut}" +
                (context.HeldOutCounts is { } counts ? " (" + string.Join(", ", counts.Select(p => $"{p.Key}={p.Value}")) + ")" : "");
            if (context.CounterExamples is { Count: > 0 } counters) yield return "      counter-examples: " + string.Join(", ", counters);
        }
    }

    public static IEnumerable<string> Scores(IEnumerable<QueryScore> scores)
    {
        foreach (var score in scores)
        {
            yield return $"{score.Scorer} ({score.Unit})\t{score.Version}";
            yield return "  " + string.Join(", ", score.Counts.Select(pair => $"{pair.Key}={pair.Value}").Concat(
                score.Numbers.Select(pair => $"{pair.Key}={(pair.Value is { } number ? number.ToString("0.####", CultureInfo.InvariantCulture) : "undefined")}"))) +
                (score.Verdict is null ? "" : $", verdict={score.Verdict}");
            if (score.Controls is { } controlHits) yield return "  controls: " + string.Join(", ", controlHits.Select(pair => $"{pair.Key}={pair.Value}"));
            foreach (var note in score.Notes) yield return "  " + note;
            if (score.Contexts is { } contexts) foreach (var line in Contexts(contexts)) yield return line;
        }
    }

    public static IEnumerable<string> Evidence(QueryEvidence evidence)
    {
        yield return $"evidence over {evidence.Source}: {evidence.Members} members of {evidence.Universe} rows";
        yield return "shared contexts (at least half the members)";
        foreach (var line in Contexts(evidence.Shared)) yield return line;
        if (evidence.Shared.Count == 0) yield return "  none";
        if (evidence.TooFew > 0) yield return $"  {evidence.TooFew} more contexts matched fewer than 20 rows: too few to tell";
        foreach (var line in Scores(evidence.Scores)) yield return line;
        if (evidence.SameShape is { } shape)
        {
            yield return $"same shape\t{shape.Key}\tcount={shape.Count}\trule attempt required={(shape.RuleAttemptRequired ? "yes" : "no")}";
            if (shape.Ledger is { } ledger)
            {
                yield return ledger.Neighbours.Count == 0 ? "  ledger: none-found" : "  ledger: " + string.Join("; ",
                    ledger.Neighbours.Select(n => $"{n.HypothesisId} {n.Status}{(n.LatestVerdict is null ? "" : " " + n.LatestVerdict)}"));
            }
        }
        yield return "examples";
        foreach (var example in evidence.Examples)
            yield return "  " + example.Text + (example.Tokens is { } tokens ? $"\t{tokens}" : "");
        yield return "speaker questions";
        foreach (var question in evidence.Questions) yield return "  " + question.Text;
        if (evidence.Questions.Count == 0) yield return "  none";
        foreach (var note in evidence.NotMeasured) yield return "not measured: " + note;
    }
}
