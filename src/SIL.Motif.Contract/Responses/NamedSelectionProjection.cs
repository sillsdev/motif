using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Requests;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>A named Selection's chosen inputs, complete parsing policy, revision, and timestamps.</summary>
[method: JsonConstructor]
public sealed record NamedSelectionProjection(
    string Name,
    IReadOnlyList<Guid> TextIds,
    IReadOnlyList<string> AddedWords,
    string CreatedUtc,
    string UpdatedUtc,
    SelectionParsingLimits Limits,
    string Revision)
{
    [JsonIgnore]
    public int? PerWordLimitMs => Limits.TimeMode == SelectionTimeLimitMode.Explicit
        ? Limits.ExplicitPerWordLimitMs : null;

    [JsonIgnore]
    public StepCap PerWordStepLimit => Limits.PerWordStepLimit;

    public NamedSelectionProjection(
        string name,
        IReadOnlyList<Guid> textIds,
        IReadOnlyList<string> addedWords,
        string createdUtc,
        string updatedUtc,
        int? perWordLimitMs = 1000,
        StepCap? perWordStepLimit = null)
        : this(name, textIds, addedWords, createdUtc, updatedUtc,
            new SelectionParsingLimits(perWordStepLimit ?? StepCap.Default,
                perWordLimitMs is null ? SelectionTimeLimitMode.Estimated : SelectionTimeLimitMode.Explicit,
                perWordLimitMs), "0")
    {
    }
}

/// <summary>The saved default Selection, if present, and whether first-time setup was skipped.</summary>
public sealed record DefaultSelectionResponse(NamedSelectionProjection? Selection, bool SetupSkipped = false);
