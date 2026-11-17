namespace SIL.Motif.Contract.Responses;

/// <summary>One Source word place and its optional current reading projection.</summary>
public sealed record SourcePassagePlace(string Anchor, string Verse, int Ordinal, string Surface, string WritingSystem,
    string WordformKey, string? FieldWorksWordformId, string? ReadingKey, string? Signature, string State);

/// <summary>
/// A bounded, generation-bound page. A place's State is <c>approved</c> or <c>stale</c> when the Occurrence has a stored
/// approval. <see cref="ApprovedState"/> is <c>available</c>, <c>unreadable</c> when some of the Passage's stored
/// approvals cannot be read, or <c>unavailable</c> when the Source's kind does not measure approval.
/// </summary>
public sealed record SourcePassageResponse(string Source, string Generation, string Ref, string ContentHash,
    string TokenizerVersion, string AnchorVersion, IReadOnlyList<SourcePassagePlace> Places, string? NextCursor)
{
    public int SchemaVersion { get; init; } = 1;
    public string IndexState { get; init; } = "ready";
    public string ApprovedState { get; init; } = "available";
}
