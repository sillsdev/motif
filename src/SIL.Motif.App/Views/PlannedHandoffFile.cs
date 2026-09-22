namespace SIL.Motif.App.Views;

/// <summary>One file a Handoff will write, and what a reader would use it for.</summary>
/// <param name="Name">The file's name in the Handoff folder.</param>
/// <param name="Purpose">One sentence: what it holds.</param>
public sealed record PlannedHandoffFile(string Name, string Purpose);
