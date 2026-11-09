namespace SIL.Motif.Contract.Requests;

/// <summary>Lists the Grammar Design Records in the current Baseline.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
public sealed record GdrListRequest(string ProjectPath);

/// <summary>Reads one Grammar Design Record's Discussion from the current Baseline.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="Id">The record's <c>GDR-n</c> abbreviation or its canonical id.</param>
public sealed record GdrShowRequest(string ProjectPath, string Id);
