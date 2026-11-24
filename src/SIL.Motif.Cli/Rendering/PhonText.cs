using System.Globalization;
using System.Text;
using SIL.Motif.Contract.Query.Phon;

namespace SIL.Motif.Cli.Rendering;

/// <summary>Words for what <c>phon</c> and <c>env</c> predicates matched and what they could not decide.</summary>
internal static class PhonText
{
    /// <summary>Writes the header lines of a pattern query: the work done, and the words that could not be decided.</summary>
    public static void Append(StringBuilder text, PhonReport phon)
    {
        text.AppendLine(string.Format(CultureInfo.InvariantCulture, "Patterns: {0} candidate words in {1} PanGloss request(s){2}",
            phon.Candidates, phon.Requests, phon.ParserVersion is null ? "" : ", PanGloss " + phon.ParserVersion));
        if (phon.MayUnderReport)
            text.AppendLine("MATCH MODE: identity. A literal or a segment-list class matches only that segment, so spans may under-report segments the grammar's rules treat as the same.");
        foreach (var warning in phon.Warnings ?? [])
            text.AppendLine("WARNING: " + warning);
        if (phon.Unsegmentable > 0)
            text.AppendLine(string.Format(CultureInfo.InvariantCulture, "UNSEGMENTABLE: {0} words cannot be segmented by the grammar and match nothing, for example {1}",
                phon.Unsegmentable, string.Join(", ", phon.UnsegmentableSample.Select(word => word.Word))));
        else text.AppendLine("Unsegmentable: 0 words the grammar cannot segment");
        if (phon.Incomplete > 0)
            text.AppendLine(string.Format(CultureInfo.InvariantCulture, "INCOMPLETE: {0} words were not decided ({1}), for example {2}",
                phon.Incomplete, phon.StopReason ?? "stopped", string.Join(", ", phon.IncompleteSample.Select(word => word.Word))));
        else text.AppendLine("Incomplete: 0 words not decided");
    }

    /// <summary>Lists the class and feature names a pattern can use.</summary>
    public static string Values(SIL.Motif.Commands.Queries.PhonValuesResponse response)
    {
        var text = new StringBuilder();
        string Feature(SIL.Motif.Commands.Queries.PhonValue value) =>
            value.Name + (value.Abbreviation is null || value.Abbreviation == value.Name ? "" : " (" + value.Abbreviation + ")");
        // The first column is what to type in a pattern; a name with spaces needs the brackets shown.
        string Class(SIL.Motif.Commands.Queries.PhonValue value) =>
            (value.Token ?? value.Abbreviation ?? value.Name) + "  " + value.Name + (value.Members is { } members ? $"  {members} phonemes" : "");
        text.AppendLine($"Natural classes ({response.NaturalClasses.Count}), from the {response.Source}; the first column is what to type in a pattern, and a class is also named by its full name in brackets, as in [Name]:");
        foreach (var value in response.NaturalClasses) text.AppendLine("  " + Class(value));
        text.AppendLine($"Phonological features ({response.Features.Count}):");
        foreach (var value in response.Features) text.AppendLine("  " + Feature(value));
        text.AppendLine(response.Note);
        return text.ToString();
    }

    /// <summary>The matched text of each predicate, as <c>phon(pattern)=text@start-end</c>.</summary>
    public static string Spans(IReadOnlyList<QueryMatch> matches) =>
        string.Join("; ", matches.Where(match => match.Spans.Count > 0).Select(match =>
            $"{match.Kind}({match.Pattern})=" + string.Join(",", match.Spans.Select(span =>
                $"{(span.TargetStart is not null ? "[" + span.Text + "]" : span.Text)}@{span.Start}-{span.End}")) +
                (match.Partial ? " (partial)" : "")));
}
