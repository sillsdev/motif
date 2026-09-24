namespace SIL.Motif.Contract.Requests;

/// <summary>Reads the saved default Selection for a project.</summary>
public sealed record ReadDefaultSelectionRequest(string ProjectPath);

/// <summary>Saves a named Selection and makes it the default for future Assessments.</summary>
public sealed record SetDefaultSelectionRequest(
    string ProjectPath,
    string Name,
    IReadOnlyList<Guid> TextIds,
    IReadOnlyList<string> AddedWords);
