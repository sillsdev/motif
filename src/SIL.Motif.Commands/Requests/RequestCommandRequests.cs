namespace SIL.Motif.Commands.Requests;

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="TargetKind">What the Request is about: <c>word</c>, <c>warning</c> or <c>matrix-cell</c>.</param>
/// <param name="TargetId">The word, Warning or Compare matrix cell, as the window named it.</param>
/// <param name="TargetLabel">A short label a person or an assistant can read.</param>
/// <param name="Note">What the linguist wants, in their own words; optional.</param>
public sealed record CreateRequestRequest(
    string FwDataPath, string ProductVersion, string TargetKind, string TargetId, string TargetLabel,
    string? Note = null);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="Status"><c>open</c> or <c>answered</c>; leave out for both.</param>
/// <param name="Offset">How many Requests to skip, for the next page.</param>
/// <param name="Limit">The most Requests to return, from 1 to 200.</param>
public sealed record ListRequestsRequest(
    string FwDataPath, string ProductVersion, string? Status = null, int Offset = 0, int Limit = 25);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="RequestId">The id of the open Request to take back.</param>
public sealed record WithdrawRequestRequest(string FwDataPath, string ProductVersion, string RequestId);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="ProposalId">The full id of a declined Proposal that answered a Request.</param>
public sealed record AskAgainRequest(string FwDataPath, string ProductVersion, string ProposalId);
