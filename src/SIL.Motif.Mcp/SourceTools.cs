using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Mcp;

/// <summary>
/// The two read tools for Motif Sources. Each serves several catalogued requests, so both are written by hand;
/// their descriptions come from Help and their argument text from the request docs. There is no tool that writes a
/// Source, Current or a Source's folder: an AI edit of a Source is a composer step in a named Draft.
/// </summary>
internal static class SourceTools
{
    private static readonly IReadOnlyDictionary<string, string?> SourcesFields = new Dictionary<string, string?>
    {
        ["Id"] = "id", ["Ref"] = "ref", ["All"] = "all", ["Source"] = null, ["ExpectedBaseline"] = null,
    };

    private static readonly IReadOnlyDictionary<string, string?> CurrentFields = new Dictionary<string, string?>
    {
        ["Source"] = "source", ["ExpectedBaseline"] = null,
    };

    public static IReadOnlyList<AgentTool> All { get; } =
    [
        new("motif_sources", string.Empty, SIL.Motif.Commands.Catalog.AgentClass.Read,
            Schema.Object(("id", Schema.String("One Source id, such as WEB. Omit to list every Source."), false),
                ("ref", Schema.String("A chapter such as MAT 5. Adds a place to each Source: InContext, Current or Later."), false),
                ("all", Schema.Boolean("Also list Removed Sources."), false)),
            true, true, false, true,
            (c, a, ct) => Task.FromResult(Sources(c.ProjectPath, a.Optional("id"), a.Optional("ref"), a.Flag("all"))))
        { Requests = [typeof(SourceListRequest), typeof(SourceShowRequest), typeof(SourceWhereRequest)], FieldNames = SourcesFields },

        new("motif_current", string.Empty, SIL.Motif.Commands.Catalog.AgentClass.Read,
            Schema.Object(("source", Schema.String("One Source id. Omit for the Current chapter of every Source."), false)),
            true, true, false, true,
            (c, a, ct) => Task.FromResult(ToolOutcome.From(SourceQuery.CurrentGet(new CurrentGetRequest(c.ProjectPath, a.Optional("source"))))))
        { Requests = [typeof(CurrentGetRequest)], FieldNames = CurrentFields },
    ];

    /// <summary>One list shape for every request: the Sources, each with a place when a chapter was asked.</summary>
    internal static ToolOutcome Sources(string project, string? id, string? chapter, bool all)
    {
        var read = id is null
            ? SourceQuery.List(new SourceListRequest(project, all))
            : Single(SourceQuery.Show(new SourceShowRequest(project, id)));
        if (!read.Succeeded || chapter is null) return ToolOutcome.From(read);
        var where = SourceQuery.Where(new SourceWhereRequest(project, chapter, id));
        if (!where.Succeeded) return ToolOutcome.Refused(where.Refusal!);
        var places = where.Value!.Places.ToDictionary(place => place.Source, StringComparer.OrdinalIgnoreCase);
        var rows = read.Value!.Sources.Select(row => places.TryGetValue(row.Id, out var place)
            ? row with { Place = place.Place, Error = row.Error ?? place.Error } : row).ToArray();
        return ToolOutcome.From(CommandOutcome<SourceListResponse>.Success(read.Value with { Sources = rows }));
    }

    private static CommandOutcome<SourceListResponse> Single(CommandOutcome<SourceShowResponse> shown) =>
        shown.Succeeded
            ? CommandOutcome<SourceListResponse>.Success(new([shown.Value!.Source])
                { Baseline = shown.Value.Baseline, IsStale = shown.Value.IsStale })
            : CommandOutcome<SourceListResponse>.Refused(shown.Refusal!);
}
