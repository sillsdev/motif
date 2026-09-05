namespace SIL.Motif.Contract.Requests;

/// <summary>Which project to measure, and which Selection to measure it over (design decision 4).</summary>
public sealed record AssessRequest(string ProjectPath, SelectionRequest Selection);
