namespace SIL.Motif.Contract.Requests;

/// <summary>Reads one bounded page of word places from a published Source Passage.</summary>
/// <param name="ProjectPath">The recorded FieldWorks project path.</param>
/// <param name="Source">The Source id, matched ignoring case.</param>
/// <param name="Ref">The Passage locator, such as MAT 5.</param>
/// <param name="Limit">The number of places, from 1 to 200; defaults to 100.</param>
/// <param name="Cursor">The opaque continuation for this Source, Passage and generation.</param>
public sealed record SourcePassageRequest(string ProjectPath, string Source, string Ref, int Limit = 100, string? Cursor = null);
