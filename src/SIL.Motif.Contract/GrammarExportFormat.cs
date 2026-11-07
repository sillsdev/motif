namespace SIL.Motif.Contract;

/// <summary>Identifies the contract used to stage a grammar for a parser.</summary>
/// <param name="Name">The staged grammar format identifier.</param>
/// <param name="Version">The version of the staging contract.</param>
public sealed record GrammarExportFormat(string Name, int Version);
