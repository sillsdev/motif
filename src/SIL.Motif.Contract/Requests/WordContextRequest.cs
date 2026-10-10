using SIL.Motif.Contract.Baselines;

namespace SIL.Motif.Contract.Requests;

/// <summary>Reads one exact word's FieldWorks context, optionally requiring an exact current Baseline.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="Word">The exact word form to read.</param>
/// <param name="ExpectedBaseline">The required Baseline identity; null accepts the current Baseline.</param>
public sealed record WordContextRequest(string ProjectPath, string Word, BaselineToken? ExpectedBaseline = null);
