namespace SIL.Motif.Contract.Requests;

/// <summary>Reads stored grammar findings, optionally narrowed to one diagnostic code or warnings only.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="Kind">The declared report kind or diagnostic code to select.</param>
/// <param name="LeftOut">Whether to select only findings about grammar content left out of compilation.</param>
public sealed record WarningsRequest(string ProjectPath, string? Kind = null, bool LeftOut = false);
