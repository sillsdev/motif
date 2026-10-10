using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.HumanJudgments;

/// <summary>A logical Notebook judgment revision; the readable reason is its sole stored source.</summary>
public sealed record HumanJudgment(
    string ProjectId,
    string JudgmentId,
    string RevisionId,
    IReadOnlyList<JudgmentPredecessor> Replaces,
    HumanJudgmentBody Body,
    string? Reason = null,
    JudgmentActor? Actor = null,
    DateTimeOffset? JudgedAtUtc = null,
    JudgmentSource? Source = null,
    bool ResolvesConflict = false,
    string ReadableFormat = "en-v1",
    IReadOnlyDictionary<string, JsonElement>? Extensions = null)
{
    public string Format { get; init; } = "motif-human-judgment";
    public int Version { get; init; } = 1;
}

/// <summary>A replaced Notebook revision and its expected logical content digest.</summary>
public sealed record JudgmentPredecessor(string RevisionId, string ContentDigest);

/// <summary>Explicitly supplied provenance, never inferred from a login or a save time.</summary>
public sealed record JudgmentActor(JudgmentActorKind Kind, string? Id = null, string? Name = null);

/// <summary>The provenance category, independent of authority to confirm a negative.</summary>
public enum JudgmentActorKind { Human, Agent, Unknown }

/// <summary>Optional frozen links to the Report and Proposal that elicited this judgment.</summary>
public sealed record JudgmentSource(string? ReportId = null, string? ProposalId = null);

/// <summary>The closed kinds of human input carried by a Notebook revision.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(DispositionJudgment), "parsimony-disposition")]
[JsonDerivedType(typeof(ReviewedNegativeJudgment), "reviewed-negative")]
[JsonDerivedType(typeof(RetractionJudgment), "retraction")]
public abstract record HumanJudgmentBody;

/// <summary>An advisory choice about an exact subject under item-relevant evidence.</summary>
public sealed record DispositionJudgment(
    HumanJudgmentSubject Subject,
    string MeasureId,
    ParsimonyDispositionKind Disposition,
    string EvidenceDigest,
    string EvidenceContract,
    string SubjectCaption,
    string MeasureCaption,
    string? Question = null) : HumanJudgmentBody;

/// <summary>A human-confirmed forbidden example; it needs no lexical or grammar owner.</summary>
public sealed record ReviewedNegativeJudgment(
    string CaseId,
    string WritingSystem,
    string Form,
    string Context,
    NegativeJudgmentTarget Target,
    string? WordformId = null,
    string? AnalysisId = null) : HumanJudgmentBody;

/// <summary>Withdrawal of explicitly named heads without deletion of their prior records.</summary>
public sealed record RetractionJudgment : HumanJudgmentBody;

/// <summary>The closed distinction between a forbidden surface and a forbidden morphology.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SurfaceNegativeTarget), "surface")]
[JsonDerivedType(typeof(ReadingNegativeTarget), "reading")]
public abstract record NegativeJudgmentTarget;

/// <summary>No reading of this surface is acceptable within the recorded context.</summary>
public sealed record SurfaceNegativeTarget : NegativeJudgmentTarget;

/// <summary>Only this ordered morphology is forbidden, following ADR 0027.</summary>
public sealed record ReadingNegativeTarget(IReadOnlyList<NegativeJudgmentMorph> Morphs) : NegativeJudgmentTarget;

/// <summary>Authoritative parser identity with conditional guessed WS and captured readable captions.</summary>
public sealed record NegativeJudgmentMorph(
    ParseMorph Identity,
    string? GuessedWritingSystem,
    string FormCaption,
    string MsaCaption);

/// <summary>The closed exact identities to which a disposition can attach.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ObjectJudgmentSubject), "object")]
[JsonDerivedType(typeof(ProjectJudgmentSubject), "project")]
[JsonDerivedType(typeof(EdgeJudgmentSubject), "edge")]
[JsonDerivedType(typeof(GroupJudgmentSubject), "computed-group")]
[JsonDerivedType(typeof(ParsimonyFindingJudgmentSubject), "parsimony-finding")]
public abstract record HumanJudgmentSubject;

/// <summary>A model class and portable GUID identity, with the owning entry required for an MSA.</summary>
public sealed record JudgmentObject(string Class, string Id, string? OwningEntryId = null);

/// <summary>One exact authored model object.</summary>
public sealed record ObjectJudgmentSubject(JudgmentObject Object) : HumanJudgmentSubject;

/// <summary>The actual containing LangProject identity.</summary>
public sealed record ProjectJudgmentSubject(string Id) : HumanJudgmentSubject;

/// <summary>A directed relationship whose endpoint roles remain authoritative.</summary>
public sealed record EdgeJudgmentSubject(
    JudgmentEdgeRole Role, JudgmentObject Owner, JudgmentObject From, JudgmentObject To) : HumanJudgmentSubject;

/// <summary>The supported relationship roles; their direction is never inferred from array position.</summary>
public enum JudgmentEdgeRole { Membership, Precedence, Rhs, LeftContext, RightContext, ConstraintUse }

/// <summary>A computed set; its key is a digest of exact members and role, never an invented entity GUID.</summary>
public sealed record GroupJudgmentSubject(
    string MeasureId, JudgmentGroupRole Role, IReadOnlyList<JudgmentObject> Members) : HumanJudgmentSubject;

/// <summary>One exact reported finding whose subject is a word case rather than a model object.</summary>
public sealed record ParsimonyFindingJudgmentSubject(string MeasureId, string FindingId) : HumanJudgmentSubject;

/// <summary>The supported computed-family semantics.</summary>
public enum JudgmentGroupRole { AlternationFamily, DuplicatePair, AdhocCluster, AdhocSlotOrder }
