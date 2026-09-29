using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Requests;

/// <summary>Reads the one pending-changes Draft for a project.</summary>
public sealed record PendingChangesRequest(string FwDataPath, string ProductVersion);

/// <summary>A change to one wordform, with a stored analysis id or an Assessment reading and its index.</summary>
public sealed record ChangeIntent(
    string ChangeId, string Kind, string WordformId, string Word,
    string? AssessmentId = null, ParseAnalysis? Reading = null, string? StoredAnalysisId = null,
    string? DisplayReading = null, int? ReadingIndex = null, string? OriginPage = null,
    OccurrenceAnchor? Occurrence = null);

/// <summary>The Text occurrence whose sentence provided context for a collected decision.</summary>
/// <param name="TextId">The GUID of the Text containing the occurrence.</param>
/// <param name="ParagraphId">The GUID of the paragraph containing the occurrence.</param>
/// <param name="SegmentId">The GUID of the Segment containing the occurrence.</param>
/// <param name="Index">The zero-based position in the Segment's analysis sequence.</param>
public sealed record OccurrenceAnchor(Guid TextId, Guid ParagraphId, Guid SegmentId, int Index);

/// <summary>Writes one change only if the Draft still has the revision the caller read.</summary>
public sealed record PutPendingChangeRequest(
    string FwDataPath, string ProductVersion, string ExpectedRevision, ChangeIntent Change);

/// <summary>Accepts the missing parser readings for one word, one Selection, or one Text.</summary>
/// <param name="FwDataPath">The path to the FieldWorks project's <c>.fwdata</c> file.</param>
/// <param name="ProductVersion">The Motif product version expected by the caller.</param>
/// <param name="ExpectedRevision">The pending-change revision the caller expects to remain current.</param>
/// <param name="AssessmentId">The Assessment that contains the readings to accept.</param>
/// <param name="WordformId">The one wordform to accept, or <see langword="null"/> for another scope.</param>
/// <param name="TextId">The Text to accept, or <see langword="null"/> for another scope.</param>
/// <param name="Selection">Whether to use every word in the named Assessment's Selection.</param>
public sealed record AcceptNewSetRequest(
    string FwDataPath, string ProductVersion, string ExpectedRevision, string AssessmentId,
    string? WordformId = null, Guid? TextId = null, bool Selection = false);

/// <summary>Stages removal of one stored analysis from its wordform.</summary>
public sealed record RemoveAnalysisRequest(
    string FwDataPath, string ProductVersion, string ExpectedRevision,
    string? ChangeId = null, string? WordformId = null, string? Word = null, string? AnalysisId = null,
    IReadOnlyList<string>? AnalysisIds = null, Guid? TextId = null);

/// <summary>Removes one change only if the Draft still has the revision the caller read.</summary>
public sealed record RemovePendingChangeRequest(
    string FwDataPath, string ProductVersion, string ExpectedRevision, string ChangeId);

/// <summary>Checks pending changes against the current Baseline and renews only fingerprints that still fit.</summary>
public sealed record RecheckPendingChangesRequest(
    string FwDataPath, string ProductVersion, string ExpectedRevision);

/// <summary>Refreshes one pending change's occurrence evidence when its semantic fit still holds.</summary>
public sealed record ReconfirmPendingChangeRequest(
    string FwDataPath, string ProductVersion, string ExpectedRevision, string ChangeId);
