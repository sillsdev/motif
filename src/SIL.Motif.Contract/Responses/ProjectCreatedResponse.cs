namespace SIL.Motif.Contract.Responses;

/// <summary>A blank FieldWorks project Motif created and reopened to verify.</summary>
/// <param name="FwDataPath">The full path of the new <c>.fwdata</c> file.</param>
/// <param name="ProjectName">The project name, which is also its folder and file name.</param>
/// <param name="Vernacular">The vernacular writing system's tag as LibLCM stored it.</param>
/// <param name="VernacularName">The vernacular language's display name.</param>
/// <param name="WordFormingCharacters">The characters declared word-forming beyond letters and marks, in order.</param>
/// <param name="Analysis">The analysis writing system's tag as LibLCM stored it.</param>
public sealed record ProjectCreatedResponse(
    string FwDataPath,
    string ProjectName,
    string Vernacular,
    string VernacularName,
    IReadOnlyList<string> WordFormingCharacters,
    string Analysis);
