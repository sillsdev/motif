using System.Text;
using SIL.Motif.Contract.Dispositions;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Cli.Rendering;

internal static class DispositionRenderer
{
    public static string RenderDisposed(DisposeFormsResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine($"{response.Recorded} recorded, {response.Replaced} replaced, {response.AlreadyRecorded} already recorded.");
        foreach (var result in response.Results)
            text.AppendLine($"  {result.Form}  {result.Result}  {Describe(result.Disposition)}");
        return text.ToString();
    }

    private const int RefusalsShown = 20;

    public static string RenderFromFile(DisposeFromFileResponse response)
    {
        var text = new StringBuilder();
        var extended = response.EvidenceExtended > 0 ? $" ({response.EvidenceExtended} gained evidence)" : "";
        text.AppendLine($"Read {response.Rows:N0} {response.Format} rows: {response.Added:N0} added, {response.Replaced:N0} replaced, " +
            $"{response.Unchanged:N0} unchanged{extended}, {response.Refused:N0} refused." +
            (response.Skipped > 0 ? $" {response.Skipped:N0} rows were not Disposition rows and were left out." : "") +
            (response.EvidenceTruncated > 0 ? $" {response.EvidenceTruncated:N0} rows had evidence cut to fit." : ""));
        text.AppendLine(response.DryRun ? "Dry run: nothing was written."
            : response.Written ? "Written."
            : response.Refused > 0 ? "Nothing was written because rows were refused; fix them, or add --skip-refused to write the rest."
            : "Nothing was written.");
        foreach (var refusal in response.Refusals.Take(RefusalsShown))
            text.AppendLine($"  row {refusal.Row}  {refusal.Form}  {refusal.Code}: {refusal.Message}");
        if (response.Refusals.Count > RefusalsShown)
            text.AppendLine($"  and {response.Refusals.Count - RefusalsShown:N0} more; --format json lists every row.");
        return text.ToString();
    }

    /// <summary>One JSON object per row, then a last object with the counts.</summary>
    public static string RenderFromFileLines(DisposeFromFileResponse response)
    {
        var options = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
        var reasons = response.Refusals.ToDictionary(refusal => refusal.Row);
        var text = new StringBuilder();
        foreach (var row in response.Results)
        {
            reasons.TryGetValue(row.Row, out var refusal);
            text.AppendLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                row = row.Row, form = row.Form, key = row.Key, category = row.Category, intended = row.Intended,
                result = row.Result, code = refusal?.Code, message = refusal?.Message,
            }, options));
        }
        text.AppendLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            summary = new
            {
                format = response.Format, rows = response.Rows, added = response.Added, replaced = response.Replaced,
                unchanged = response.Unchanged, evidenceExtended = response.EvidenceExtended, refused = response.Refused,
                skipped = response.Skipped, evidenceTruncated = response.EvidenceTruncated, dryRun = response.DryRun,
                written = response.Written,
            },
        }, options));
        return text.ToString();
    }

    public static string RenderUndisposed(UndisposeFormsResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine($"{response.Cleared} cleared, {response.NotRecorded} had no disposition.");
        foreach (var result in response.Results)
            text.AppendLine($"  {result.Form}  {result.Result}" + (result.Cleared is { } cleared ? "  was " + Describe(cleared) : ""));
        return text.ToString();
    }

    public static string RenderShown(ShowDispositionResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine(response.Current is { } current
            ? $"{response.Form}: {Describe(current)}"
            : $"{response.Form}: no disposition ({response.Scope} default; it is analysed like any word).");
        if (response.Current is { } standing)
            text.AppendLine($"  Recorded by {standing.RecordedBy} at {standing.RecordedUtc}; {response.Scope}.");
        foreach (var ended in response.History)
            text.AppendLine($"  Was {Describe(ended.Disposition)}; {ended.EndedKind} by {ended.EndedBy} at {ended.EndedUtc}" +
                (ended.EndedNote is null ? "." : $": {ended.EndedNote}"));
        return text.ToString();
    }

    public static string RenderListed(ListDispositionsResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine($"{response.Total:N0} word types" + (response.Category is null ? "" : $" as {response.Category}") +
            $". Page {response.Page} of {response.Pages}.");
        text.AppendLine("By category: " + string.Join(", ", response.ByCategory.Select(count => $"{count.Category} {count.Types:N0}")));
        foreach (var item in response.Items)
            text.AppendLine($"  {item.Form}  {Describe(item)}");
        if (response.Page < response.Pages) text.AppendLine($"Next page: --page {response.Page + 1}.");
        return text.ToString();
    }

    private static string Describe(WordTypeDisposition item) =>
        item.Category + (item.Intended is null ? "" : $" (intended {item.Intended})") + $": {item.Evidence}";
}
