using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Requests;

/// <summary>Reads or changes Read state for one Text and its word occurrences.</summary>
/// <param name="ProjectPath">The FieldWorks project whose paired Motif store holds the state.</param>
/// <param name="TextId">The Text whose Read occurrences are requested or changed.</param>
/// <param name="Occurrences">The word occurrences to change, or null for every word in the Text.</param>
/// <param name="IsRead">True to mark Read, false to mark Unread, or null to read the current state.</param>
public sealed record WordReadStateRequest(
    string ProjectPath,
    Guid TextId,
    IReadOnlyList<OccurrenceAnchor>? Occurrences = null,
    bool? IsRead = null);
