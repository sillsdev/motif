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
        if (phon.Unsegmentable > 0)
            text.AppendLine(string.Format(CultureInfo.InvariantCulture, "UNSEGMENTABLE: {0} words cannot be segmented by the grammar and match nothing, for example {1}",
                phon.Unsegmentable, string.Join(", ", phon.UnsegmentableSample.Select(word => word.Word))));
        if (phon.Incomplete > 0)
            text.AppendLine(string.Format(CultureInfo.InvariantCulture, "INCOMPLETE: {0} words were not decided ({1}), for example {2}",
                phon.Incomplete, phon.StopReason ?? "stopped", string.Join(", ", phon.IncompleteSample.Select(word => word.Word))));
    }

    /// <summary>Lists the class and feature names a pattern can use.</summary>
    public static string Values(SIL.Motif.Commands.Queries.PhonValuesResponse response)
    {
        var text = new StringBuilder();
        string Name(SIL.Motif.Commands.Queries.PhonValue value) =>
            value.Name + (value.Abbreviation is null || value.Abbreviation == value.Name ? "" : " (" + value.Abbreviation + ")") +
            (value.Members is { } members ? $"  {members} phonemes" : "");
        text.AppendLine($"Natural classes ({response.NaturalClasses.Count}), from the {response.Source}:");
        foreach (var value in response.NaturalClasses) text.AppendLine("  " + Name(value));
        text.AppendLine($"Phonological features ({response.Features.Count}):");
        foreach (var value in response.Features) text.AppendLine("  " + Name(value));
        text.AppendLine(response.Note);
        return text.ToString();
    }

    /// <summary>The matched text of each predicate, as <c>phon(pattern)=text@start-end</c>.</summary>
    public static string Spans(IReadOnlyList<QueryMatch> matches) =>
        string.Join("; ", matches.Where(match => match.Spans.Count > 0).Select(match =>
            $"{match.Kind}({match.Pattern})=" + string.Join(",", match.Spans.Select(span =>
                $"{(span.TargetStart is not null ? "[" + span.Text + "]" : span.Text)}@{span.Start}-{span.End}"))));
}
