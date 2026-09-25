using SIL.Motif.Contract.Assess;

namespace SIL.Motif.Contract.Responses;

/// <summary>A named Selection's chosen Text identities, words, per-word limits, and timestamps.</summary>
public sealed record NamedSelectionProjection(
    string Name,
    IReadOnlyList<Guid> TextIds,
    IReadOnlyList<string> AddedWords,
    string CreatedUtc,
    string UpdatedUtc,
    int PerWordLimitMs = 1000,
    StepCap? PerWordStepLimit = null);

/// <summary>The saved default Selection, if present, and whether first-time setup was skipped.</summary>
public sealed record DefaultSelectionResponse(NamedSelectionProjection? Selection, bool SetupSkipped = false);
