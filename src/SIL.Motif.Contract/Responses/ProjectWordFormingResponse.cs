namespace SIL.Motif.Contract.Responses;

/// <summary>The word-forming characters of a project's vernacular writing system after an addition.</summary>
/// <param name="FwDataPath">The full path of the project's <c>.fwdata</c> file.</param>
/// <param name="Vernacular">The tag of the vernacular writing system that was changed.</param>
/// <param name="Added">The characters this call added; empty when all were already declared.</param>
/// <param name="WordFormingCharacters">Every character declared word-forming beyond letters and marks, in order.</param>
public sealed record ProjectWordFormingResponse(
    string FwDataPath,
    string Vernacular,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> WordFormingCharacters);
