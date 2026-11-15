using SIL.Motif.Contract.Responses;
using SIL.Motif.Commands.Handoff;

namespace SIL.Motif.App.ViewModels;

/// <summary>Opens Settings with some words selected for a file Handoff.</summary>
/// <param name="Words">The words to hand off.</param>
/// <param name="SelectedTrace">The displayed trace and Baseline to preserve, when this request came from Try a Word.</param>
/// <param name="WarningScope">The PanGloss finding that selected these words, when opened from Warnings.</param>
public sealed record HandOffRequest(
    IReadOnlyList<string> Words, WordTraceResponse? SelectedTrace = null, WarningHandoffScope? WarningScope = null)
    : PageRequest(WorkspacePage.Settings);
