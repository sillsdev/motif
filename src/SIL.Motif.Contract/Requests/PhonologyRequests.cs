using System.Text.Json.Serialization;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Phonology;

namespace SIL.Motif.Contract.Requests;

/// <summary>Saves a new immutable revision of a phonology Example Set.</summary>
/// <param name="ProjectPath">The path to the project that owns the set.</param>
/// <param name="Name">A trimmed, nonblank display name of at most 128 Unicode scalar values.</param>
/// <param name="Examples">The 1–500 examples to store in authored order, including their expected forms, context, and optional Citation.</param>
/// <param name="ExpectedRevisionId">Null creates a set; otherwise this must name the set's exact current revision.</param>
/// <param name="ProposalId">The existing Proposal associated with this revision, or null when there is no supporting Proposal.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SavePhonologyExamplesRequest(
    [property: JsonPropertyName("projectPath")] string ProjectPath,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("examples")] IReadOnlyList<PhonologyExample> Examples,
    [property: JsonPropertyName("expectedRevisionId")] string? ExpectedRevisionId = null,
    [property: JsonPropertyName("proposalId")] string? ProposalId = null);

/// <summary>Lists phonology Example Set headers in a project.</summary>
/// <param name="ProjectPath">The path to the project containing the sets.</param>
/// <param name="PageSize">The maximum number of set headers to return, from 1 through 100.</param>
/// <param name="Continuation">The project-bound cursor from the preceding page, or null for the first page.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ListPhonologyExamplesRequest(
    [property: JsonPropertyName("projectPath")] string ProjectPath,
    [property: JsonPropertyName("pageSize")] int PageSize = 20,
    [property: JsonPropertyName("continuation")] PhonologySetListCursor? Continuation = null);

/// <summary>Reads one phonology Example Set revision and a page of its examples.</summary>
/// <param name="ProjectPath">The path to the project containing the revision.</param>
/// <param name="RevisionId">The canonical id of the immutable revision to read.</param>
/// <param name="PageSize">The maximum number of examples to return, from 1 through 100.</param>
/// <param name="Continuation">The cursor for this exact revision from the preceding page, or null for the first page.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ShowPhonologyExamplesRequest(
    [property: JsonPropertyName("projectPath")] string ProjectPath,
    [property: JsonPropertyName("revisionId")] string RevisionId,
    [property: JsonPropertyName("pageSize")] int PageSize = 20,
    [property: JsonPropertyName("continuation")] PhonologyPageCursor? Continuation = null);

/// <summary>Checks a stored phonology Example Set against a captured baseline.</summary>
/// <param name="ProjectPath">The path to the project that owns the revision.</param>
/// <param name="RevisionId">The canonical id of the immutable Example Set revision to check.</param>
/// <param name="Baseline">The captured project Baseline that binds this request.</param>
/// <param name="Limits">Request-wide step and work ceilings divided across rows in request order, plus one shared deadline that starts after grammar and cache preparation.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PhonologyCheckRequest(
    [property: JsonPropertyName("projectPath")] string ProjectPath,
    [property: JsonPropertyName("revisionId")] string RevisionId,
    [property: JsonPropertyName("baseline")] BaselineToken Baseline,
    [property: JsonPropertyName("limits")] PhonologyLimits Limits);

/// <summary>Reads a page of results for a phonology check assessment.</summary>
/// <param name="ProjectPath">The path to the project containing the assessment.</param>
/// <param name="AssessmentId">The canonical id of the assessment whose results are requested.</param>
/// <param name="PageSize">The maximum number of results to return, from 1 through 100.</param>
/// <param name="Continuation">The cursor for this exact assessment from the preceding page, or null for the first page.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PhonologyResultsRequest(
    [property: JsonPropertyName("projectPath")] string ProjectPath,
    [property: JsonPropertyName("assessmentId")] string AssessmentId,
    [property: JsonPropertyName("pageSize")] int PageSize = 20,
    [property: JsonPropertyName("continuation")] PhonologyPageCursor? Continuation = null);
