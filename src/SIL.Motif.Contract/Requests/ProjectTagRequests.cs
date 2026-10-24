namespace SIL.Motif.Contract.Requests;

/// <summary>Reads the machine-local tag assigned to a FieldWorks project.</summary>
/// <param name="ProjectPath">The FieldWorks project's data-file path.</param>
public sealed record ReadProjectTagRequest(string ProjectPath);

/// <summary>Sets the machine-local tag for a FieldWorks project by a person's choice.</summary>
/// <param name="ProjectPath">The FieldWorks project's data-file path.</param>
/// <param name="Tag">A lowercase tag beginning with a letter, containing only lowercase letters, digits and hyphens, and at most 10 characters.</param>
public sealed record SetProjectTagRequest(string ProjectPath, string Tag);
