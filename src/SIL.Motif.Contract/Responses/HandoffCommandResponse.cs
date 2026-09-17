using System.Collections.Generic;

namespace SIL.Motif.Contract.Responses;

/// <summary>
/// What a Handoff run published (design decision 6): where the folder now lives, the Baseline and
/// Selection it exported, every file it wrote, and the retained Assessment ids it used — empty when
/// the caller asked for <c>--no-assess</c>.
/// </summary>
public sealed record HandoffCommandResponse(
    string OutputDirectory,
    BaselineCaptureResponse Baseline,
    SelectionProjection Selection,
    IReadOnlyList<string> Files,
    IReadOnlyList<string> AssessmentIds)
{
    /// <summary>The retained invocation exported by this Handoff, or null for a Baseline-only Handoff.</summary>
    public string? InvocationId { get; init; }
}
