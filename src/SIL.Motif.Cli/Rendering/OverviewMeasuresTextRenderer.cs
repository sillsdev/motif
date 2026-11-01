using System.Globalization;
using System.Text;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Cli.Rendering;

/// <summary>
/// The Overview's counts of unparsed words and of what Text Coverage leaves out, as the same fields the window and
/// MCP read. Only counts appear, never a time and never a grade.
/// </summary>
internal static class OverviewMeasuresTextRenderer
{
    private const string Indent = "           ";

    public static void AppendLeftOut(StringBuilder text, OverviewTextCoverage coverage)
    {
        if (coverage is not { DigitOnlyOccurrences: { } digits, OtherWritingSystemOccurrences: { } other }) return;
        text.AppendLine($"{Indent}Not in Text Coverage: {Count(digits, "digit-only occurrence", "digit-only occurrences")}, " +
            $"{Count(other, "occurrence", "occurrences")} in other writing systems");
    }

    public static void AppendUnparsed(StringBuilder text, OverviewUnparsedWords? unparsed)
    {
        if (unparsed is null) return;
        var places = unparsed.RankedByOccurrences ? $", {Count(unparsed.Occurrences, "occurrence", "occurrences")}" : string.Empty;
        text.AppendLine($"Unparsed   {Count(unparsed.Words, "word", "words")}{places}" +
            (unparsed.Groups.Count == 0 ? string.Empty : ": " + string.Join(", ", unparsed.Groups.Select(Reason))) +
            (unparsed.GroupCount > unparsed.Groups.Count
                ? $", and {Count(unparsed.GroupCount - unparsed.Groups.Count, "more reason", "more reasons")}" : string.Empty));
        if (unparsed.Top.Count > 0)
            text.AppendLine($"{Indent}{(unparsed.RankedByOccurrences ? "most occurrences" : "first by spelling")}: " +
                string.Join(", ", unparsed.Top.Select(word => unparsed.RankedByOccurrences
                    ? $"{word.Word} {word.Occurrences:N0}" : word.Word)) +
                (unparsed.Words > unparsed.Top.Count ? $" (top {unparsed.Top.Count} of {unparsed.Words:N0})" : string.Empty));
        if (unparsed.IncorrectSpellingWords > 0)
            text.AppendLine($"{Indent}Not listed: {Count(unparsed.IncorrectSpellingWords, "word", "words")} " +
                "the project marks as an incorrect spelling");
    }

    public static void AppendNotMeasured(StringBuilder text, OverviewTextCoverage coverage)
    {
        if (coverage.NotMeasuredWords > 0)
            text.AppendLine($"{Indent}Partly measured: {Count(coverage.NotMeasuredWords, "word", "words")} " +
                "of the Selection are not in the stored parse, so figures built from it leave them out");
    }

    public static void AppendBands(StringBuilder text, OverviewFrequencyBands? bands)
    {
        if (bands is null) return;
        text.AppendLine(bands.ByText ? "Bands      Text Coverage by frequency band" : "Bands      By frequency band");
        foreach (var band in bands.Bands)
        {
            var ranks = band.LastRank is { } last
                ? $"{band.FirstRank:N0} to {last:N0}" : $"{band.FirstRank:N0} and beyond";
            text.AppendLine($"{Indent}words ranked {ranks}: {band.ParsedWords:N0}/{band.Words:N0} words parse, " +
                $"{band.ParsedOccurrences:N0}/{band.Occurrences:N0} occurrences covered");
        }
    }

    public static void AppendAmbiguity(StringBuilder text, OverviewAmbiguity? ambiguity)
    {
        if (ambiguity is null) return;
        text.AppendLine($"Ambiguity  {ambiguity.MeanAnalyses.ToString("N2", CultureInfo.CurrentCulture)} analyses per parsed word, " +
            $"{Count(ambiguity.AmbiguousWords, "word", "words")} with more than one");
        if (ambiguity.CountedWords < ambiguity.ParsedWords)
            text.AppendLine($"{Indent}Counted {ambiguity.CountedWords:N0} of {ambiguity.ParsedWords:N0} parsed words; " +
                "the rest have analyses that could not all be read");
        if (ambiguity.Top.Count > 0)
            text.AppendLine($"{Indent}most analyses: " + string.Join(", ", ambiguity.Top.Select(word => $"{word.Word} {word.Analyses:N0}")) +
                (ambiguity.AmbiguousWords > ambiguity.Top.Count ? $" (top {ambiguity.Top.Count} of {ambiguity.AmbiguousWords:N0})" : string.Empty));
    }

    private static string Reason(OverviewUnparsedGroup group) => group.Kind switch
    {
        UnparsedKind.Refused => $"{group.Words:N0} refused ({group.Code ?? "no code"})",
        UnparsedKind.WorkLimit => $"{group.Words:N0} stopped at the work limit",
        UnparsedKind.TimeLimit => $"{group.Words:N0} stopped at the time limit",
        UnparsedKind.BothLimits => $"{group.Words:N0} stopped at the work and time limits",
        UnparsedKind.Stopped => $"{group.Words:N0} stopped before finishing",
        _ => $"{group.Words:N0} no analysis",
    };

    private static string Count(int value, string singular, string plural) =>
        $"{value.ToString("N0", CultureInfo.CurrentCulture)} {(value == 1 ? singular : plural)}";
}
