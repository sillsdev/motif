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
