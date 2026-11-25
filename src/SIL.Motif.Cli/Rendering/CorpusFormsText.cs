using System.Globalization;
using System.Text;
using SIL.Motif.Contract.Query.Corpus;
using SIL.Motif.Contract.Query.Phon;

namespace SIL.Motif.Cli.Rendering;

/// <summary>Renders a corpus <c>forms</c> result: the corpus line, what the patterns could not decide, then the matching words.</summary>
internal static class CorpusFormsText
{
    private static void AppendReadings(StringBuilder text, CorpusFormsResponse response)
    {
        if (response.ReadingsInfo is { } info)
            text.AppendLine(string.Format(CultureInfo.InvariantCulture, "Readings: {0} ({1} word types parsed; {2} capped, {3} timed out, {4} no answer, {5} unsegmentable, {8} analyses unknown to the project; morph classes {6}){7}",
                info.Complete ? "complete" : "INCOMPLETE", info.Types, info.Capped, info.TimedOut, info.Failed, info.Unsegmentable, info.MorphClasses,
                info.Note is null ? "" : ". " + info.Note, info.UnknownMsa));
        if (response.Causes is { } causes)
        {
            text.AppendLine(string.Format(CultureInfo.InvariantCulture, "Causes: {0} word types, {1} tokens have more than one reading{2}",
                causes.Words, causes.Tokens, causes.Complete ? "" : " (INCOMPLETE: capped or failed words may have more readings)"));
            foreach (var group in causes.Groups)
            {
                text.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0}{1}: {2} words, {3} tokens; e.g. {4}", group.Cause + (group.Detail is null ? "" : " [" + group.Detail + "]"),
                    group.Benign ? " (benign)" : "", group.Words, group.Tokens, group.Example.Word));
                foreach (var reading in group.Example.Readings)
                    text.AppendLine("      " + string.Join(" + ", reading.Morphs.Select(morph => morph.Text.Length > 0 ? morph.Text : "?")) + "   [" +
                        string.Join(" ", reading.Morphs.Select(morph => (morph.Kind is { } kind ? kind[..1] + ":" : "") + morph.Msa[..Math.Min(8, morph.Msa.Length)])) + "]");
            }
        }
        if (response.MorphSeqs is { } seqs)
        {
            text.AppendLine(string.Format(CultureInfo.InvariantCulture, "Morph sequences: {0} distinct over {1} word types, {2} tokens{3}", seqs.Distinct, seqs.Words, seqs.Tokens,
                seqs.Complete ? "" : " (INCOMPLETE: capped or failed words may have more readings, so counts are lower bounds)"));
            foreach (var entry in seqs.Sequences)
                text.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0}\t{1} types\t{2} tokens\te.g. {3}", string.Join(" + ", entry.Seq), entry.Types, entry.Tokens,
                    string.Join(", ", entry.Examples)));
            if (seqs.Distinct > seqs.Sequences.Count) text.AppendLine("  ... " + (seqs.Distinct - seqs.Sequences.Count).ToString(CultureInfo.InvariantCulture) + " more sequences; --limit shows more");
        }
        if (response.ByMorph is { } morphemes)
        {
            text.AppendLine("By morpheme:");
            foreach (var group in morphemes)
                text.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0}\t{1} types\t{2} tokens\te.g. {3}", group.Morpheme, group.Types, group.Tokens, string.Join(", ", group.Examples)));
        }
        if (response.WhatIf is { } what)
        {
            text.AppendLine(string.Format(CultureInfo.InvariantCulture, "What if retired {0} ({1}; {2}{3}):", string.Join(", ", what.Retire),
                what.Estimate ? "ESTIMATE, " + what.Note : "exact", what.Method, what.Complete ? "" : "; INCOMPLETE parse"));
            text.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0} types ({1} tokens) use them; {2} types ({3} tokens) would lose every reading; {4} types ({5} tokens) would keep fewer",
                what.Affected.Types, what.Affected.Tokens, what.Lost.Types, what.Lost.Tokens, what.Reduced.Types, what.Reduced.Tokens));
            if (what.Capped.Types > 0 || what.Failed.Types > 0)
                text.AppendLine(string.Format(CultureInfo.InvariantCulture, "  not counted above: {0} types ({1} tokens) with a capped parse that use them, {2} types ({3} tokens) the batch gave no answer for",
                    what.Capped.Types, what.Capped.Tokens, what.Failed.Types, what.Failed.Tokens));
            foreach (var word in what.Words)
                text.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0}\t{1}\t{2}\t{3} via, {4} other of {5}{6}", word.Word, word.Tokens,
                    word.Lost ? "LOST" : "reduced", word.ReadingsVia, word.ReadingsOther, word.Readings, word.MayHaveMore ? " (parse capped: may keep a hidden reading)" : "") +
                    (word.Alternative is { Count: > 0 } alternative ? "; keeps " + string.Join(" | ", alternative) : ""));
        }
    }

    public static string Render(CorpusFormsResponse response)
    {
        var text = new StringBuilder();
        var corpus = response.Corpus;
        text.AppendLine(string.Format(CultureInfo.InvariantCulture, "Corpus {0}: {1} types, {2} tokens, segmentation {3}{4}",
            corpus.Source, corpus.Types, corpus.Tokens, corpus.Segmentation, corpus.FoldCase ? ", case folded" : ""));
        foreach (var source in corpus.Sources ?? [])
            text.AppendLine(string.Format(CultureInfo.InvariantCulture, "  source {0} ({1}): {2} types, {3} tokens", source.Path, source.Kind, source.Types, source.Tokens));
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
            if (set.Effects is { } effects)
                text.AppendLine(string.Format(CultureInfo.InvariantCulture, "  These {0} rules can change {1} ({2} more are blocked by the form, {3} could not be modelled; effects read in {4:0.0} s).",
                    effects.Rules, set.Morpheme, effects.NotOnForm, effects.Unmodelled, effects.Seconds)
                    + (effects.UnmodelledBy is { Count: > 0 } by ? " Not modelled, by reason: " + string.Join(", ", by.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + " " + pair.Value)) + "." : ""));
            foreach (var rule in set.Rules ?? [])
                text.AppendLine("  rule " + rule.Name + (rule.Environment is null ? "" : " in " + rule.Environment) + (rule.Rhs is null ? "" : " -> " + rule.Rhs) + " (" + rule.Verdict + ")");
            foreach (var form in set.Forms.Where(form => form.TestRoot is not null))
                text.AppendLine("  " + form.Form + " [" + form.Cell + "] on " + form.TestRoot + (form.PieceSource is { } source && source != "morphSpans" ? " (" + source + ")" : "") + (form.Rules is { Count: > 0 } rules ? " by " + string.Join(", ", rules) : ""));
            if (set.Pattern.OverBroad)
                text.AppendLine("  overBroad: the broadest term is " + set.Pattern.Broadest + "; the search finds more than this morpheme.");
            if (set.Parse is { } parse)
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  Parse index {8} in {9} s ({0} of {1} word types answered, {2} s in all): {3} carry the morpheme, {4} have no analysis, {5} could not be segmented, {6} stopped at a limit, {7} got no answer; {10} read from PanGloss's morph spans, {11} stand in whole.",
                    parse.Parsed, parse.Types, parse.Seconds, parse.WithMorpheme, parse.NoAnalysis, parse.Unsegmentable, parse.Capped + parse.TimedOut, parse.Failed,
                    parse.IndexBuilt ? "built" : "cached", parse.IndexSeconds, parse.Placed, parse.WholeWord));
            foreach (var reason in set.Reasons) text.AppendLine("  " + reason.Code + (reason.Detail is null ? "" : ": " + reason.Detail));
        }
        AppendReadings(text, response);
        if (response.Totals is { } totals)
            text.AppendLine(string.Format(CultureInfo.InvariantCulture, "Totals: {0} types, {1} tokens", totals.Types, totals.Tokens));
        if (response.Groups is { } groups)
        {
            text.AppendLine("Groups by " + string.Join(", ", groups.Columns) + ": types, tokens");
            foreach (var group in groups.Groups)
                text.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0}\t{1}\t{2}", group.Key, group.Types, group.Tokens));
        }
        if (response.Sample is { } sample)
            text.AppendLine(string.Format(CultureInfo.InvariantCulture, "Sample: {0} of {1} matching words, seed {2}", sample.Count, sample.Of, sample.Seed));
        if (response.Written is { } written)
            text.AppendLine(string.Format(CultureInfo.InvariantCulture, "Wrote {0} words to {1}{2}", written.Words, written.Path, written.WithCounts ? " (form, count)" : ""));
        if (response.Phon is { } phon) PhonText.Append(text, phon);
        text.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} words match, showing {1}", response.Matched, response.Rows.Count));
        foreach (var row in response.Rows)
        {
            var line = row.Form + "\t" + row.Tokens.ToString(CultureInfo.InvariantCulture);
            if (row.ParseState is not null) line += "\t" + row.ParseState;
            if (row.Readings is { } readings) line += "\t" + readings.ToString(CultureInfo.InvariantCulture) + " readings";
            if (row.Matches is { Count: > 0 }) line += "\t" + PhonText.Spans(row.Matches);
            if (row.Realizes is { Count: > 0 }) line += "\t" + string.Join("; ", row.Realizes.Select(hit => "realizes " + string.Join("|", hit.Forms)));
            if (row.Baseline is { } baseline) line += "\t" + baseline.Change + " (baseline " + baseline.ParseState + ", " + baseline.Readings.ToString(CultureInfo.InvariantCulture) + " readings)";
            foreach (var twin in row.Twins ?? [])
                line += "\t" + twin.Label + " " + twin.Form + " (" + twin.Tokens.ToString(CultureInfo.InvariantCulture) + " tokens" + (twin.InCorpus ? "" : ", not in corpus")
                    + (twin.ParseState is null ? "" : ", " + twin.ParseState + ", " + twin.Readings?.ToString(CultureInfo.InvariantCulture) + " readings") + ")";
            text.AppendLine(line);
        }
        var available = response.Sample?.Count ?? response.Matched;
        if (available > response.Rows.Count && !(response.Groups is not null && response.Rows.Count == 0))
            text.AppendLine(string.Format(CultureInfo.InvariantCulture, "TRUNCATED: showing {0} of {1} words.", response.Rows.Count, available));
        return text.ToString();
    }
}
