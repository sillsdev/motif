namespace SIL.Motif.Contract.Responses;

/// <summary>One Source word place and its optional current reading projection.</summary>
public sealed record SourcePassagePlace(string Anchor, string Verse, int Ordinal, string Surface, string WritingSystem,
    string WordformKey, string? FieldWorksWordformId, string? ReadingKey, string? Signature, string State);

/// <summary>A bounded, generation-bound page; approved judgements are unavailable.</summary>
public sealed record SourcePassageResponse(string Source, string Generation, string Ref, string ContentHash,
    string TokenizerVersion, string AnchorVersion, IReadOnlyList<SourcePassagePlace> Places, string? NextCursor)
{
    public int SchemaVersion { get; init; } = 1;
    public string IndexState { get; init; } = "ready";
    public string ApprovedState { get; init; } = "unavailable";
}
