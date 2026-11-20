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
