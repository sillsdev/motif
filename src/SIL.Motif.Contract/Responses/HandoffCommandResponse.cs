using System.Collections.Generic;

namespace SIL.Motif.Contract.Responses;

/// <summary>
/// What a Handoff run published (design decision 6): where the folder now lives, the Baseline it was
/// built from, the Selection it composed, every file it wrote, and the Assessment ids it recorded —
/// empty when the caller asked for <c>--no-assess</c>.
/// </summary>
public sealed record HandoffCommandResponse(
    string OutputDirectory,
    BaselineCaptureResponse Baseline,
    SelectionProjection Selection,
    IReadOnlyList<string> Files,
    IReadOnlyList<string> AssessmentIds);
