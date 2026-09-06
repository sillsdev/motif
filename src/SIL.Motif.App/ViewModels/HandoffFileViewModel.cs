namespace SIL.Motif.App.ViewModels;

/// <summary>One file a completed Handoff wrote, already verified to sit inside its own output directory.</summary>
public sealed record HandoffFileViewModel(string RelativePath, string FullPath);
