using System.Text;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Cli.Rendering;

/// <summary>Plain-text rendering of the Source reads.</summary>
internal static class SourceTextRenderer
{
    internal static string RenderList(SourceListResponse response)
    {
        if (response.Sources.Count == 0) return "This project has no Sources." + Environment.NewLine;
        var text = new StringBuilder();
        foreach (var source in response.Sources) text.AppendLine(Row(source));
        return text.ToString();
    }

    internal static string RenderShow(SourceShowResponse response)
    {
        var source = response.Source;
        var text = new StringBuilder();
        text.AppendLine($"{source.Id}  {source.Name}");
        text.AppendLine($"Kind: {source.Kind}; status: {source.Status}; versification: {Or(source.Versification, "none")}");
        text.AppendLine($"Folder: {Or(source.Folder, "none on this machine")}");
        text.AppendLine($"Context: {Or(source.Context, "none")} ({response.ChapterCount} chapters)");
        foreach (var range in source.ContextRanges) text.AppendLine($"  {range.Book}  {range.Chapters}");
        if (source.Error is not null) text.AppendLine($"Cannot be used: {source.Error}");
        return text.ToString();
    }

    internal static string RenderWhere(SourceWhereResponse response)
    {
        if (response.Places.Count == 0) return "This project has no Sources." + Environment.NewLine;
        var text = new StringBuilder();
        foreach (var place in response.Places)
            text.AppendLine($"{place.Source}  {response.Ref}  {place.Place ?? place.Error ?? "unknown"}");
        return text.ToString();
    }

    internal static string RenderCurrent(CurrentGetResponse response)
    {
        if (response.Current.Count == 0) return "No Current chapter is set on this machine." + Environment.NewLine;
        var text = new StringBuilder();
        foreach (var current in response.Current)
            text.AppendLine($"{current.Source}  {current.Chapter}" + (current.Place is null ? "" : $"  {current.Place}"));
        return text.ToString();
    }

    private static string Row(SourceSummary source) =>
        $"{source.Id}  {source.Name}  [{source.Status}]  {Or(source.Context, "no Context")}" +
        (source.Error is null ? "" : $"  ERROR: {source.Error}");

    private static string Or(string? value, string fallback) => string.IsNullOrEmpty(value) ? fallback : value;
}
