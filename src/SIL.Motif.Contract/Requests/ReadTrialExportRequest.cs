namespace SIL.Motif.Contract.Requests;

/// <summary>Locates the retained evidence file for one exact Trial job in a project.</summary>
/// <param name="ProjectPath">The known project whose paired store retains the Trial evidence.</param>
/// <param name="JobId">The exact Trial job ID.</param>
/// <param name="At">The frozen Proposal revision, Baseline digest or saved report digest the export must match.</param>
/// <param name="Cursor">Opaque cursor from a previous response's page.nextCursor; omit for the first page of words.</param>
/// <param name="Limit">How many words to return, from 1 to 200; the default is 100. Omit both Cursor and Limit for the compact receipt.</param>
public sealed record ReadTrialExportRequest(string ProjectPath, string JobId, string? At = null, string? Cursor = null, int? Limit = null)
{
    /// <summary>The words to skip that <see cref="Cursor"/> names, or -1 when it is not a cursor this API issued. A method, so it is not a schema property.</summary>
    public int CursorOffset() => SIL.Motif.Contract.Responses.ResultPaging.OffsetOrInvalid(Cursor);
}
