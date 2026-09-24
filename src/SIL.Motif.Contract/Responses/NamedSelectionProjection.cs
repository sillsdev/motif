namespace SIL.Motif.Contract.Responses;

/// <summary>A named Selection's chosen Text identities, added words, and saved timestamps.</summary>
public sealed record NamedSelectionProjection(
    string Name,
    IReadOnlyList<Guid> TextIds,
    IReadOnlyList<string> AddedWords,
    string CreatedUtc,
    string UpdatedUtc);

/// <summary>The saved default Selection, or an empty response when setup has not saved one.</summary>
public sealed record DefaultSelectionResponse(NamedSelectionProjection? Selection);
