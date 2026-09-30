using System;
using System.Collections.Generic;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Worker.Store;

/// <summary>Maps one Assessment kind in a retained invocation to its immutable Assessment identity.</summary>
public sealed record RetainedInvocationMember(string Kind, string AssessmentId);

/// <summary>
/// The immutable aggregate that binds one invocation's Baseline, Selection, scope, Assessor, artifacts, and
/// per-kind Assessment members.
/// </summary>
public sealed record RetainedInvocationRecord(
    string InvocationId,
    string ProjectKey,
    BaselineToken BaselineToken,
    string BaselineRootDirectory,
    string BaselineFwDataPath,
    DateTimeOffset BaselineSourceLastWriteUtc,
    DateTimeOffset BaselinePublishedUtc,
    DateTimeOffset SavedUtc,
    SelectionDescriptor Selection,
    string Assessor,
    string ScopeJson,
    string ScopeDigest,
    string ArtifactInvocationId,
    IReadOnlyList<RetainedInvocationMember> Members)
{
    /// <summary>Validated child Assessments; populated by retained-result reads.</summary>
    public IReadOnlyList<AssessmentRecord> Assessments { get; init; } = [];
}
