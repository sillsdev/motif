namespace SIL.Motif.Contract.Requests;

/// <summary>Locates the retained evidence file for one exact Trial job in a project.</summary>
/// <param name="ProjectPath">The known project whose paired store retains the Trial evidence.</param>
/// <param name="JobId">The exact Trial job ID.</param>
/// <param name="At">The frozen Proposal revision, Baseline digest or saved report digest the export must match.</param>
/// <param name="Page">The one-based page of words to read. Omit for the compact receipt.</param>
public sealed record ReadTrialExportRequest(string ProjectPath, string JobId, string? At = null, int? Page = null);
