namespace SIL.Motif.Contract.Requests;

/// <summary>Reads the saved default Selection for a project.</summary>
public sealed record ReadDefaultSelectionRequest(string ProjectPath);

/// <summary>Records that the person chose to skip first-time project setup.</summary>
public sealed record SkipSetupRequest(string ProjectPath);
