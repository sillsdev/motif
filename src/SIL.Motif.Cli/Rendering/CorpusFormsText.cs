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
        if (response.Phon is { } phon) PhonText.Append(text, phon);
        text.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} words match, showing {1}", response.Matched, response.Rows.Count));
        foreach (var row in response.Rows)
        {
            var line = row.Form + "\t" + row.Tokens.ToString(CultureInfo.InvariantCulture);
            if (row.ParseState is not null) line += "\t" + row.ParseState;
            if (row.Matches is { Count: > 0 }) line += "\t" + PhonText.Spans(row.Matches);
            text.AppendLine(line);
        }
        if (response.Matched > response.Rows.Count)
            text.AppendLine(string.Format(CultureInfo.InvariantCulture, "TRUNCATED: showing {0} of {1} words.", response.Rows.Count, response.Matched));
        return text.ToString();
    }
}
