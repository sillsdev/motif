namespace SIL.Motif.Contract.Requests;

/// <summary>Reads the latest Overview matching the current Baseline and resolved default Selection.</summary>
public sealed record OverviewRequest(string ProjectPath);
