using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Requests;

/// <summary>
/// Asks the stored Assessment which of the Selection's words use an object, which words it ran in, and which
/// morphemes a set of words shares. At least one of <paramref name="Ref"/> and <paramref name="Words"/> is required.
/// </summary>
/// <param name="ProjectPath">The project whose current Baseline and default Selection to read.</param>
/// <param name="Ref">The object to look up, by identity.</param>
/// <param name="Words">The words whose shared morphemes to list.</param>
public sealed record ObjectUsesRequest(
    string ProjectPath, ObjectUseRef? Ref = null, IReadOnlyList<string>? Words = null);
