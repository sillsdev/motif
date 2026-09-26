namespace SIL.Motif.Contract.Requests;

/// <summary>Which project's grammar to check; the grammar is read from its current Baseline.</summary>
public sealed record GrammarCheckRequest(string ProjectPath);
