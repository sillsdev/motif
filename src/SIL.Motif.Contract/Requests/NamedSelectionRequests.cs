namespace SIL.Motif.Contract.Requests;

/// <summary>Reads the saved default Selection for a project.</summary>
public sealed record ReadDefaultSelectionRequest(string ProjectPath);

/// <summary>
/// Chooses the Selection that Parsimony's Text group measures. With Text ids it saves a new Selection of that name
/// (or revises one that is not the Default Selection); without them it points at a Selection already saved.
/// </summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="Name">The Selection name.</param>
/// <param name="TextIds">The chosen Text identities, or null to reuse the Selection already saved under the name.</param>
public sealed record SetParsimonySelectionRequest(string ProjectPath, string Name,
    IReadOnlyList<Guid>? TextIds = null);

/// <summary>Records that the person chose to skip first-time project setup.</summary>
public sealed record SkipSetupRequest(string ProjectPath);
