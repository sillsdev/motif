using SIL.Motif.Contract.Requests;

namespace SIL.Motif.App.ViewModels;

/// <summary>A passage locator keeps its Source and exact occurrence separate from display text.</summary>
/// <param name="SourceId">Null for the FieldWorks Text reader; otherwise the stored Source id.</param>
/// <param name="Passage">The external chapter reference; never a fabricated FieldWorks occurrence.</param>
/// <param name="Occurrence">The exact FieldWorks place, when supplied by the Selection reader.</param>
public sealed record PassageChoiceViewModel(string? SourceId, string? Passage = null, OccurrenceAnchor? Occurrence = null);

/// <summary>Opens Texts at a Source and passage without substituting a spelling match.</summary>
public sealed record OpenPassageRequest(PassageChoiceViewModel Choice) : PageRequest(WorkspacePage.Texts);
