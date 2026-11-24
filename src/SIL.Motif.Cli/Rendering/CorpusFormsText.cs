using System.Globalization;
using System.Text;
using SIL.Motif.Contract.Query.Corpus;
using SIL.Motif.Contract.Query.Phon;

namespace SIL.Motif.Cli.Rendering;

/// <summary>Renders a corpus <c>forms</c> result: the corpus line, what the patterns could not decide, then the matching words.</summary>
internal static class CorpusFormsText
{
    public static string Render(CorpusFormsResponse response)
    {
        var text = new StringBuilder();
        var corpus = response.Corpus;
        text.AppendLine(string.Format(CultureInfo.InvariantCulture, "Corpus {0}: {1} types, {2} tokens, segmentation {3}{4}",
            corpus.Source, corpus.Types, corpus.Tokens, corpus.Segmentation, corpus.FoldCase ? ", case folded" : ""));
        if (response.Where is not null) text.AppendLine("Where " + response.Where);
        foreach (var set in response.Realizes ?? [])
        {
            if (set.Pattern.Anchor == "context")
            {
                var shown = set.Pattern.Text.Length > 160 ? set.Pattern.Text[..160] + "..." : set.Pattern.Text;
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Realizes {0} ({1}, {2}): context-anchored search over {3} forms, each with its attested context and word edge: {4}; segmentation {5}; surface text only, so it can over-match.",
                    set.Morpheme, set.Tier, set.Complete ? "complete" : "NOT complete", set.Forms.Count, shown, set.Pattern.Segmentation));
            }
            else
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Realizes {0} ({1}, {2}): searched for {3} forms {4} the {5} of a word, as {6}; {7}.",
                set.Morpheme, set.Tier, set.Complete ? "complete" : "NOT complete", set.Forms.Count, set.Pattern.Anchor == "none" ? "anywhere in" : "at",
                set.Pattern.Anchor switch { "start" => "start", "end" => "end", _ => "text" },
                string.Join(" | ", set.Forms.Select(form => form.Form)), "segmentation " + set.Pattern.Segmentation + "; surface text only, so it can over-match"));
            if (set.Pattern.OverBroad)
                text.AppendLine("  overBroad: the broadest term is " + set.Pattern.Broadest + "; the search finds more than this morpheme.");
            if (set.Parse is { } parse)
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  Parse index {8} in {9} s ({0} of {1} word types answered, {2} s in all): {3} carry the morpheme, {4} have no analysis, {5} could not be segmented, {6} stopped at a limit, {7} got no answer; {10} read from PanGloss's morph spans, {11} stand in whole.",
                    parse.Parsed, parse.Types, parse.Seconds, parse.WithMorpheme, parse.NoAnalysis, parse.Unsegmentable, parse.Capped + parse.TimedOut, parse.Failed,
                    parse.IndexBuilt ? "built" : "cached", parse.IndexSeconds, parse.Placed, parse.WholeWord));
            foreach (var reason in set.Reasons) text.AppendLine("  " + reason.Code + (reason.Detail is null ? "" : ": " + reason.Detail));
        }
        if (response.Phon is { } phon) PhonText.Append(text, phon);
        text.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} words match, showing {1}", response.Matched, response.Rows.Count));
        foreach (var row in response.Rows)
        {
            var line = row.Form + "\t" + row.Tokens.ToString(CultureInfo.InvariantCulture);
            if (row.ParseState is not null) line += "\t" + row.ParseState;
            if (row.Matches is { Count: > 0 }) line += "\t" + PhonText.Spans(row.Matches);
            if (row.Realizes is { Count: > 0 }) line += "\t" + string.Join("; ", row.Realizes.Select(hit => "realizes " + string.Join("|", hit.Forms)));
            text.AppendLine(line);
        }
        if (response.Matched > response.Rows.Count)
            text.AppendLine(string.Format(CultureInfo.InvariantCulture, "TRUNCATED: showing {0} of {1} words.", response.Rows.Count, response.Matched));
        return text.ToString();
    }
}
