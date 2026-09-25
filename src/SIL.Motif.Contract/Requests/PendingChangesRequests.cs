using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Requests;

/// <summary>Reads the one pending-changes Draft for a project.</summary>
public sealed record PendingChangesRequest(string FwDataPath, string ProductVersion);

/// <summary>A change to one wordform, with a stored analysis id or an Assessment reading and its index.</summary>
public sealed record ChangeIntent(
    string ChangeId, string Kind, string WordformId, string Word,
    string? AssessmentId = null, ParseAnalysis? Reading = null, string? StoredAnalysisId = null,
    string? DisplayReading = null, int? ReadingIndex = null, string? OriginPage = null);

/// <summary>Writes one change only if the Draft still has the revision the caller read.</summary>
public sealed record PutPendingChangeRequest(
    string FwDataPath, string ProductVersion, string ExpectedRevision, ChangeIntent Change);

/// <summary>Removes one change only if the Draft still has the revision the caller read.</summary>
public sealed record RemovePendingChangeRequest(
    string FwDataPath, string ProductVersion, string ExpectedRevision, string ChangeId);
