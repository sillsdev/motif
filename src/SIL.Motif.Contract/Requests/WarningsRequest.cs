namespace SIL.Motif.Contract.Requests;

/// <summary>Reads stored grammar findings, optionally narrowed to one diagnostic code or warnings only.</summary>
public sealed record WarningsRequest(string ProjectPath, string? Kind = null, bool LeftOut = false);
