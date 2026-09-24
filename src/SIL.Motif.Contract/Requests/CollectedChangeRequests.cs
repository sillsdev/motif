namespace SIL.Motif.Contract.Requests;

/// <summary>Collects an explicitly chosen reading into a named Draft.</summary>
public sealed record CollectedChangeRequest(
    string FwDataPath, string ProductVersion, string DraftName,
    string Kind, string Word, string Reading, string? AssessmentId);

/// <summary>Removes one word's collected change from a named Draft.</summary>
public sealed record RemoveCollectedChangeRequest(
    string FwDataPath, string ProductVersion, string DraftName, string Word);

/// <summary>Removes every collected change that no longer fits the project.</summary>
public sealed record RemoveNonFittingChangesRequest(
    string FwDataPath, string ProductVersion, string DraftName);

/// <summary>Reads one persistent Draft's change fit against the project.</summary>
public sealed record PreflightDraftRequest(string FwDataPath, string ProductVersion, string DraftName);
