using System.Globalization;
using System.Text;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Cli.Rendering;

/// <summary>
/// The Markdown form of <c>motif generate --batch</c>: the bounded summary only, because the full rows are in the
/// artifact file it names. It prints what the response holds and nothing more.
/// </summary>
internal static class GenerateBatchMarkdown
{
    public static string Render(GenerateBatchResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine("# Batch generation");
        text.AppendLine();
        text.AppendLine($"Against {response.TracedAgainst}. Grammar `{response.GrammarHash}`. PanGloss {response.PanglossVersion}. " +
            $"Schema `{response.Schema}`.");
        text.AppendLine();
        text.AppendLine($"{Count(response.Rows)} rows, {Count(response.Surfaces)} surfaces, written to `{response.Out}`.");
        text.AppendLine("Status: " + string.Join(", ", response.Status.Select(pair => $"{pair.Key} {Count(pair.Value)}")) + ".");
        if (response.Causes.Count > 0)
            text.AppendLine("Causes: " + string.Join(", ", response.Causes.Select(pair => $"{pair.Key} {Count(pair.Value)}")) + ".");
        if (!response.Complete)
        {
            text.AppendLine();
            if (response.StopReason is { } stop)
                text.AppendLine($"> **stopped early ({stop}).** {Inline(response.StopMessage)} The rows written before it are kept in `{response.Out}`; " +
                    "a line with no row is a `no-result` error row.");
            text.AppendLine($"> **incomplete.** {Count(response.Incomplete)} rows stopped on a cap or deadline and " +
                $"{Count(response.Status.GetValueOrDefault("error"))} rows could not run. A surface missing from those rows is not proven impossible.");
        }
        Quote(text, "First rows with no surface", response.FirstNone);
        Quote(text, "First rows that could not run", response.FirstErrors);
        return text.ToString();
    }

    private static void Quote(StringBuilder text, string heading, IReadOnlyList<GenerateBatchNone> rows)
    {
        if (rows.Count == 0) return;
        text.AppendLine();
        text.AppendLine(heading + ":");
        foreach (var row in rows)
            text.AppendLine($"- {(row.Line is { } line ? "line " + Count(line) + " " : string.Empty)}`{Inline(row.Id)}` {Inline(row.ComboKey)}: {Inline(row.Cause.Code)}" +
                (row.Cause.Message is { } message ? " (" + Inline(message) + ")" : string.Empty));
    }

    private static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Inline(string? value) => (value ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ');
}
