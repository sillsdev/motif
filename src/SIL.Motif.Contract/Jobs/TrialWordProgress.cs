namespace SIL.Motif.Contract.Jobs;

/// <summary>Completed searches and the word whose search has started in a sequential Trial batch.</summary>
public sealed record TrialWordProgress(int Completed, int Total, string? CurrentWord);
