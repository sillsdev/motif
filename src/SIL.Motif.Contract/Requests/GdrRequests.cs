using SIL.Motif.Contract.Baselines;

namespace SIL.Motif.Contract.Requests;

/// <summary>Lists the Grammar Design Records in the current Baseline.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="ExpectedBaseline">The required Baseline identity; null accepts the current Baseline.</param>
public sealed record GdrListRequest(string ProjectPath, BaselineToken? ExpectedBaseline = null);

/// <summary>Reads one Grammar Design Record's Discussion from the current Baseline.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="Id">The record's <c>GDR-n</c> abbreviation or its canonical id.</param>
/// <param name="ExpectedBaseline">The required Baseline identity; null accepts the current Baseline.</param>
public sealed record GdrShowRequest(string ProjectPath, string Id, BaselineToken? ExpectedBaseline = null);

/// <summary>Finds the object at a project path in the current Baseline, for a link to it.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="Path">The FieldWorks Markdown project path, such as <c>/lexicon/entry-1</c>.</param>
/// <param name="ExpectedBaseline">The required Baseline identity, such as the one the linking record was read from; null accepts the current Baseline.</param>
public sealed record GdrLinkToPathRequest(string ProjectPath, string Path, BaselineToken? ExpectedBaseline = null);
