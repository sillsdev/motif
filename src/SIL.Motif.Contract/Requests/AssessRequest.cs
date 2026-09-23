namespace SIL.Motif.Contract.Requests;

/// <summary>
/// Which project to measure, and which Selection to measure it over (design decision 4). A per-word time limit,
/// in milliseconds, overrides the project's configured one for this run only; <see langword="null"/> keeps it.
/// </summary>
public sealed record AssessRequest(string ProjectPath, SelectionRequest Selection, int? PerWordLimitMs = null);
