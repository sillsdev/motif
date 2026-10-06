using SIL.Motif.Contract.Assess;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Requests;

/// <summary>How a saved Selection determines the parser's per-word time limit.</summary>
public enum SelectionTimeLimitMode
{
    Estimated,
    Explicit,
}

/// <summary>The complete saved per-word limit policy for a Selection.</summary>
public sealed record SelectionParsingLimits(
    StepCap PerWordStepLimit,
    SelectionTimeLimitMode TimeMode,
    int? ExplicitPerWordLimitMs)
{
    /// <summary>Explains why this limit policy cannot be saved, or returns <see langword="null"/>.</summary>
    public string? ValidationError()
    {
        if (PerWordStepLimit is null) return "A per-word step cap is required.";
        if (!Enum.IsDefined(TimeMode)) return "The Selection time-limit mode is unknown.";
        if (PerWordStepLimit.IsUnbounded)
            return TimeMode == SelectionTimeLimitMode.Estimated && ExplicitPerWordLimitMs is null
                ? null
                : "An unbounded step cap has no per-word time limit or explicit override.";
        return TimeMode switch
        {
            SelectionTimeLimitMode.Estimated when ExplicitPerWordLimitMs is null => null,
            SelectionTimeLimitMode.Explicit when ExplicitPerWordLimitMs is > 0 => null,
            SelectionTimeLimitMode.Estimated => "Estimated time limits cannot include explicit milliseconds.",
            _ => "An explicit time limit must be a positive number of milliseconds.",
        };
    }
}

/// <summary>Saves new chosen inputs and limits as the project's Default Selection.</summary>
[method: JsonConstructor]
public sealed record SetDefaultSelectionRequest(
    string ProjectPath,
    string Name,
    IReadOnlyList<Guid> TextIds,
    IReadOnlyList<string> AddedWords,
    SelectionParsingLimits Limits,
    string? ExpectedRevision = null)
{
    [JsonIgnore]
    public int? PerWordLimitMs
    {
        get => Limits.TimeMode == SelectionTimeLimitMode.Explicit ? Limits.ExplicitPerWordLimitMs : null;
        init => Limits = new SelectionParsingLimits(Limits.PerWordStepLimit,
            value is null ? SelectionTimeLimitMode.Estimated : SelectionTimeLimitMode.Explicit, value);
    }

    [JsonIgnore]
    public StepCap PerWordStepLimit
    {
        get => Limits.PerWordStepLimit;
        init => Limits = new SelectionParsingLimits(value, Limits.TimeMode, Limits.ExplicitPerWordLimitMs);
    }

    /// <summary>Creates a Selection request for callers that supply the former explicit-millisecond shape.</summary>
    public SetDefaultSelectionRequest(
        string projectPath,
        string name,
        IReadOnlyList<Guid> textIds,
        IReadOnlyList<string> addedWords,
        int? perWordLimitMs = 1000,
        StepCap? perWordStepLimit = null)
        : this(projectPath, name, textIds, addedWords,
            new SelectionParsingLimits(perWordStepLimit ?? StepCap.Default,
                perWordLimitMs is null ? SelectionTimeLimitMode.Estimated : SelectionTimeLimitMode.Explicit,
                perWordLimitMs), null)
    {
    }
}

/// <summary>Changes only the saved parsing limits on the current Default Selection.</summary>
public sealed record SetSelectionLimitsRequest(
    string ProjectPath,
    string SelectionName,
    string ExpectedRevision,
    SelectionParsingLimits Limits);
