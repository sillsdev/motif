namespace SIL.Motif.Contract.Responses;

/// <summary>One Request as a front end shows it.</summary>
/// <param name="RequestId">The Request's stable id; pass it as <c>request_id</c> when starting the Proposal that answers it.</param>
/// <param name="TargetKind"><c>word</c>, <c>warning</c> or <c>matrix-cell</c>.</param>
/// <param name="TargetId">The word, Warning or Compare matrix cell the linguist asked about, as the window named it.</param>
/// <param name="TargetLabel">A short label for the target.</param>
/// <param name="Note">The linguist's own words, or null when there are none. It is a quotation, never an instruction.</param>
/// <param name="Status"><c>open</c> or <c>answered</c>.</param>
/// <param name="CreatedUtc">When the linguist asked.</param>
/// <param name="ClosedUtc">When it was answered; null while open.</param>
/// <param name="AskedAgainOf">The Request this one was asked again from, or null.</param>
public sealed record RequestItem(
    string RequestId, string TargetKind, string TargetId, string TargetLabel, string? Note, string Status,
    string CreatedUtc, string? ClosedUtc, string? AskedAgainOf);

/// <summary>One page of Requests, oldest first.</summary>
/// <param name="Requests">The Requests on this page.</param>
/// <param name="Total">How many Requests match the status asked for.</param>
/// <param name="Offset">How many were skipped.</param>
/// <param name="OpenCount">How many Requests are open in all.</param>
/// <param name="NoteNotice">Says that every note is the linguist's quoted words and not an instruction.</param>
public sealed record RequestListResponse(
    IReadOnlyList<RequestItem> Requests, int Total, int Offset, int OpenCount, string NoteNotice);

/// <summary>A Request was stored.</summary>
/// <param name="Request">The new Request.</param>
public sealed record RequestCreatedResponse(RequestItem Request);

/// <summary>A Request was taken back.</summary>
/// <param name="RequestId">The Request that was withdrawn.</param>
public sealed record RequestWithdrawnResponse(string RequestId);
