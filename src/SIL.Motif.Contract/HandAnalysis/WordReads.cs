using System;
using System.Collections.Immutable;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.HandAnalysis;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(WordQuery.Identity), "identity")]
[JsonDerivedType(typeof(WordQuery.Reading), "reading")]
[JsonDerivedType(typeof(WordQuery.Alternatives), "alternatives")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public abstract record WordQuery
{
    /// <summary>Exact selection by Key, with optional other-case lookup in the supplied writing system and comparison case.</summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Identity(WordIdentity Key, WordformKey? CasePair = null) : WordQuery;

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Reading(ReadingKey Key) : WordQuery;

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Alternatives(WordIdentity Wordform, WordOrder Order) : WordQuery;
}

[JsonConverter(typeof(StrictStringEnumConverter<WordOrder>))]
public enum WordOrder
{
    [JsonStringEnumMemberName("default-rank")] DefaultRank,
    [JsonStringEnumMemberName("encounter")] Encounter
}

/// <summary>Exact NFD and case identity within a writing system, independent of collation equivalence.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WordformKey(string Ws, string NfdCaseForm);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(WordIdentity.Native), "native")]
[JsonDerivedType(typeof(WordIdentity.CaseForm), "case-form")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public abstract record WordIdentity
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Native(Guid Id) : WordIdentity;

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record CaseForm(WordformKey Form) : WordIdentity;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ReadingKey.Analysis), "analysis")]
[JsonDerivedType(typeof(ReadingKey.NativeGloss), "gloss")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public abstract record ReadingKey
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Analysis(AnalysisRef Reference) : ReadingKey;

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record NativeGloss(Guid Id) : ReadingKey;
}

/// <summary>Parser-only readings are Assessment-scoped and need no native identity.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(AnalysisRef.Native), "native")]
[JsonDerivedType(typeof(AnalysisRef.Parser), "parser")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public abstract record AnalysisRef
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Native(Guid Id) : AnalysisRef;

    /// <summary>AssessmentId is the opaque canonical address returned by Assess, including its prefix.</summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Parser(string AssessmentId, string ReadingKey) : AnalysisRef;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(WordFact.Header), "wordform")]
[JsonDerivedType(typeof(WordFact.Reading), "reading")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public abstract record WordFact
{
    /// <summary>Exact matches keep all writing-system forms. Key is the focused form when the request supplies one.</summary>
    /// <remarks>GUID lookups can leave Key null. Other-case matches are grouped by their key in the requested CasePair writing system.</remarks>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Header(WordformKey? Key, ImmutableArray<Guid> ExactMatches,
        ImmutableArray<WordformForms> Wordforms, ImmutableArray<OtherCaseFact> OtherCases,
        GuessFact? BestGuess, GuessFact? CachedGuess, ChooserCounts Counts) : WordFact;

    /// <summary>Analysis locates hydration. Signature carries the complete canonical identity of the observed Reading.</summary>
    /// <remarks>
    /// Preserve observed Reading and occurrence facts and use a null Signature with a limitation when exact comparison
    /// is unmeasured. This includes an empty morpheme path, a missing entry, allomorph or MSA, an unverified authored link,
    /// an unresolved sense or unproved candidate set, or an unsupported authored feature construction.
    /// Null optional links and empty feature sets describe proved absence. Shared, open, negated and disjunctive
    /// specifications and feature-structure disjunctions cannot be reduced to closed symbolic paths.
    /// A missing unique summary alone does not prevent comparison of complete morpheme facts.
    /// Exact corpus ground truth uses settled guard-valid approvals. Preserve guesses, stale approvals and conflicts as
    /// observations outside that ground truth.
    /// </remarks>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Reading(AnalysisRef Analysis, Guid? WordformId, Guid? CategoryId, Guid? SelectedGlossId,
        ImmutableArray<BundleFact> Bundles, ImmutableArray<GlossFact> Glosses, ImmutableArray<EvaluationFact> Evaluations,
        string? Producer, string? ResultId, string EncounterKey,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? OccurrenceUseCount, string? DefaultRankKey,
        ReadingSignature? Signature, ImmutableArray<ReadLimitation> Limitations) : WordFact;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WordformForms(Guid WordformId, ImmutableDictionary<string, string> Forms);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OtherCaseFact(WordformKey Key, ImmutableArray<Guid> WordformIds);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChooserCounts([property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Analyses,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? Glosses, bool Complete);

/// <summary>Id identifies the native bundle. MemberId identifies its list occurrence and may carry a suffix.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BundleFact(Guid? Id, string? MemberId, Guid? MorphId, Guid? SenseId, Guid? MsaId, Guid? InflTypeId,
    ImmutableDictionary<string, string> Forms, ImmutableDictionary<string, string> ConcreteForms);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GlossFact(Guid Id, ImmutableDictionary<string, string> Forms);

/// <summary>An unresolved agent leaves AgentId and Human unknown and is qualified by a reading limitation.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EvaluationFact(Guid Id, Guid? AgentId, bool? Human, bool Approves);
