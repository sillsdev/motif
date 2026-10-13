namespace SIL.Motif.Contract.Requests;

/// <summary>A request to create a new, blank FieldWorks project.</summary>
/// <param name="ParentFolder">The existing folder the project's own folder is created in.</param>
/// <param name="Name">The project name; the project lands at <c>{ParentFolder}/{Name}/{Name}.fwdata</c>.</param>
/// <param name="VernacularTag">The BCP 47 tag of the one vernacular writing system, such as <c>swh</c>.</param>
/// <param name="VernacularName">The vernacular language's display name, or null for the name the tag already carries.</param>
/// <param name="WordFormingCharacters">
/// Characters, such as an apostrophe, that the vernacular writing system treats as part of a word although
/// Unicode does not class them as letters. Letters and marks need no entry.
/// </param>
/// <param name="AnalysisTag">The BCP 47 tag of the one analysis writing system, or null for <c>en</c>.</param>
public sealed record ProjectCreateRequest(
    string ParentFolder,
    string Name,
    string VernacularTag,
    string? VernacularName = null,
    string? WordFormingCharacters = null,
    string? AnalysisTag = null);
