namespace SIL.Motif.Contract.Requests;

/// <summary>Reads lexical entries from the current Baseline by entry id or sense id.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="Ids">Portable ids of entries or senses; a sense id reads the entry that owns it. At most 100.</param>
public sealed record LexicalEntryRequest(string ProjectPath, IReadOnlyList<string> Ids);
