namespace SIL.Motif.Contract.Requests;

/// <summary>A request to declare more characters word-forming in a project's vernacular writing system.</summary>
/// <param name="ProjectPath">The <c>.fwdata</c> file of the project.</param>
/// <param name="Characters">
/// Characters, such as the digits <c>0123456789</c> or an apostrophe, to add to the writing system's word-forming
/// characters. Characters already declared are left as they are, and none is ever removed.
/// </param>
public sealed record ProjectWordFormingRequest(string ProjectPath, string Characters);
