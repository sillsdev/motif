using System;
using System.Collections.Generic;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Runner.Execution;

/// <summary>Which Apply precondition a <see cref="PreconditionRefusal"/> reports as unmet.</summary>
public enum PreconditionKind
{
    /// <summary>No bound Dry Run anchor was supplied.</summary>
    DryRunMissing,

    /// <summary>The anchor was bound to a different Proposal than the one presented.</summary>
    AnchorMismatch,

    /// <summary>The anchor was recorded under a different semantic snapshot projection version.</summary>
    ProjectionVersion,

    /// <summary>A Proposal this one requires has not been applied to the project.</summary>
    PrerequisiteMissing,

    /// <summary>A target the Dry Run found no longer exists in the project.</summary>
    TargetGone,

    /// <summary>The live footprint no longer matches the one the Dry Run measured.</summary>
    FootprintDrift,
}

/// <summary>
/// Apply's precondition is not met, raised before any unit of work opens, so nothing rolled back.
/// <see cref="Kind"/> says which precondition, and the facts repeat it as <c>precondition</c> in kebab case.
/// </summary>
public sealed class PreconditionRefusal : ProposalRefusalException
{
    public PreconditionRefusal(
        PreconditionKind kind, string message, IReadOnlyDictionary<string, string>? facts = null)
        : base(RefusalCodes.ApplyPreconditionFailed, FailureReason.Refused, message, WithKind(kind, facts))
    {
        Kind = kind;
    }

    /// <summary>The precondition that was not met.</summary>
    public PreconditionKind Kind { get; }

    private static Dictionary<string, string> WithKind(PreconditionKind kind, IReadOnlyDictionary<string, string>? facts)
    {
        var all = facts is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(facts, StringComparer.Ordinal);
        all["precondition"] = Kebab(kind);
        return all;
    }

    private static string Kebab(PreconditionKind kind) => kind switch
    {
        PreconditionKind.DryRunMissing => "dry-run-missing",
        PreconditionKind.AnchorMismatch => "anchor-mismatch",
        PreconditionKind.ProjectionVersion => "projection-version",
        PreconditionKind.PrerequisiteMissing => "prerequisite-missing",
        PreconditionKind.TargetGone => "target-gone",
        PreconditionKind.FootprintDrift => "footprint-drift",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
