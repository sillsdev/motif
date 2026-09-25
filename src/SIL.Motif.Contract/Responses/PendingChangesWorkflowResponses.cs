namespace SIL.Motif.Contract.Responses;

/// <summary>The outcome of applying the current pending changes.</summary>
/// <param name="Applied">Whether Motif applied a Proposal.</param>
/// <param name="Receipt">The recorded apply result, or <see langword="null"/> when nothing was pending.</param>
public sealed record ApplyPendingResult(bool Applied, ApplyProjection? Receipt);

/// <summary>The outcome of measuring one pending revision.</summary>
/// <param name="JobId">The Trial job that produced this outcome.</param>
/// <param name="Revision">The pending revision used for the Trial.</param>
/// <param name="NumbersText">The measured numbers as one English sentence.</param>
/// <param name="EvidenceComplete">Whether every requested word produced complete correctness evidence.</param>
public sealed record MeasurePendingResult(
    string JobId, string Revision, string NumbersText, bool EvidenceComplete);

/// <summary>Reports completed work while Motif measures words for one pending revision.</summary>
/// <param name="Completed">The number of words whose Trial work has finished.</param>
/// <param name="Total">The number of words requested.</param>
/// <param name="CurrentWord">The word whose Trial work has started, when available.</param>
public sealed record MeasureProgress(int Completed, int Total, string? CurrentWord);
