using SIL.Motif.Contract.Baselines;

namespace SIL.Motif.Contract.Requests;

/// <summary>Reads one exact word's FieldWorks context, optionally requiring an exact current Baseline.</summary>
public sealed record WordContextRequest(string ProjectPath, string Word, BaselineToken? ExpectedBaseline = null);
