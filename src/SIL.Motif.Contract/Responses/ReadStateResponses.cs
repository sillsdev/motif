using SIL.Motif.Contract.Requests;

namespace SIL.Motif.Contract.Responses;

/// <summary>The Read occurrences currently stored for one Text.</summary>
/// <param name="ReadOccurrences">Occurrence anchors whose saved fingerprints still fit current evidence.</param>
/// <param name="HasBaseline">Whether the project has a current Baseline containing Text word evidence.</param>
public sealed record WordReadStateResponse(
    IReadOnlyList<OccurrenceAnchor> ReadOccurrences,
    bool HasBaseline);
