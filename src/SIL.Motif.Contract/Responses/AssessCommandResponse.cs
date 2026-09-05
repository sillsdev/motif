using System.Collections.Generic;

namespace SIL.Motif.Contract.Responses;

/// <summary>
/// What a synchronous <c>assess</c> run produced (design decision 4): the Baseline it measured, the
/// Selection it composed, every Assessment id it recorded, and PanGloss's own statistics summary.
/// </summary>
public sealed record AssessCommandResponse(
    BaselineCaptureResponse Baseline,
    SelectionProjection Selection,
    IReadOnlyList<string> AssessmentIds,
    string SummaryMarkdown);
