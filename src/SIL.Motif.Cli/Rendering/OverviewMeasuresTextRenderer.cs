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
                ? $", and {unparsed.GroupCount - unparsed.Groups.Count:N0} more reasons" : string.Empty));
        if (unparsed.Top.Count > 0)
            text.AppendLine($"{Indent}{(unparsed.RankedByOccurrences ? "most occurrences" : "first by spelling")}: " +
                string.Join(", ", unparsed.Top.Select(word => unparsed.RankedByOccurrences
                    ? $"{word.Word} {word.Occurrences:N0}" : word.Word)) +
                (unparsed.Words > unparsed.Top.Count ? $" (top {unparsed.Top.Count} of {unparsed.Words:N0})" : string.Empty));
        if (unparsed.IncorrectSpellingWords > 0)
            text.AppendLine($"{Indent}Not listed: {Count(unparsed.IncorrectSpellingWords, "word", "words")} " +
                "the project marks as an incorrect spelling");
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
