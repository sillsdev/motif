namespace SIL.Motif.Worker.Sources.Adapter;

internal sealed record SourceKey(string ProjectId, string SourceId);

internal sealed record DocumentKey(SourceKey Source, string DocumentId);

internal sealed record OccurrenceKey(DocumentKey Document, string DocumentHash, string SegmentationId,
    CorpusAnchor Anchor);

internal sealed record WordformKey(string WritingSystemId, string FormNfd);

internal sealed record CorpusAnchor(string Unit, int Segment, int Word, long? Offset = null, int? Length = null,
    string? Verse = null, int? RawWordIndex = null, int? PartIndex = null);

internal sealed record CorpusStamp(SourceKey Source, string Id, string InventoryDigest, string DocumentDigest,
    string SegmentationId, string ProviderVersion);

internal sealed record CorpusOccurrence(OccurrenceKey Key, long Ordinal, string FormId, WordformKey? Wordform,
    string? Surface);

internal sealed record WeightedWordform(string FormId, WordformKey? Wordform, long SourceFrequency, long SelectedWeight);

internal sealed record CorpusSelectionScope(CorpusStamp Stamp, string MembershipDigest, long TypeCount, long TokenCount,
    IReadOnlyList<string>? DocumentIds);
