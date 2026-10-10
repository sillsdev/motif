namespace SIL.Motif.Contract.Requests;

/// <summary>A confirmed request to prepare one FieldWorks project for Motif human judgments.</summary>
/// <param name="ProjectPath">The path to the project's <c>.fwdata</c> file.</param>
/// <param name="Confirmation">The exact confirmation sentence shown by Motif.</param>
public sealed record ProjectInitializationRequest(string ProjectPath, string? Confirmation);
