namespace SIL.Motif.Contract.Requests;

/// <summary>Reads the Reading changes of one Trial from the evidence it already retained.</summary>
/// <param name="ProjectPath">The known project whose paired store retains the Trial evidence.</param>
/// <param name="JobId">The exact Trial job. Give this or <paramref name="ProposalId"/>.</param>
/// <param name="ProposalId">The Proposal whose newest completed Trial with retained evidence is read.</param>
/// <param name="Classes">The classes to show: lost, gained, changed, doubled or stopped-negative. Omit for the first four.</param>
/// <param name="Cursor">Opaque cursor from a previous response's nextCursor; omit for the first page.</param>
/// <param name="Limit">The most rows to show in each class, from 1 to 200.</param>
/// <param name="GroupBy">entry or slot: group the changed Readings by cause and page the groups. Omit for per-word rows. <c>rule</c> is refused: Reading identity carries no rule.</param>
public sealed record ReadTrialDeltaRequest(string ProjectPath, string? JobId = null, string? ProposalId = null,
    IReadOnlyList<string>? Classes = null, string? Cursor = null, int Limit = 20,
    string? GroupBy = null)
{
    /// <summary>The rows to skip that <see cref="Cursor"/> names, or -1 when it is not a cursor this API issued. A method, so it is not a schema property.</summary>
    public int CursorOffset() => SIL.Motif.Contract.Responses.ResultPaging.OffsetOrInvalid(Cursor);
}
