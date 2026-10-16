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

/// <summary>Queues a check of a stored phonology Example Set revision against a captured Baseline.</summary>
/// <param name="ProjectPath">The path to the project that owns the revision.</param>
/// <param name="RevisionId">The canonical id of the immutable Example Set revision to check.</param>
/// <param name="Baseline">The captured Baseline that binds this request, or null for the project's current Baseline.</param>
/// <param name="Limits">Request-wide step and work ceilings divided across rows in request order, plus one shared deadline that starts after grammar and cache preparation; null uses the defaults.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PhonologyCheckRequest(
    [property: JsonPropertyName("projectPath")] string ProjectPath,
    [property: JsonPropertyName("revisionId")] string RevisionId,
    [property: JsonPropertyName("baseline")] BaselineToken? Baseline = null,
    [property: JsonPropertyName("limits")] PhonologyLimits? Limits = null);

/// <summary>Reads stored results of a phonology check: a page of summaries or one complete example.</summary>
/// <param name="ProjectPath">The path to the project containing the assessment.</param>
/// <param name="AssessmentId">The canonical id of the assessment whose results are requested.</param>
/// <param name="Read">Which stored result to return: a bounded page of summaries, or one example's full detail.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PhonologyResultsRequest(
    [property: JsonPropertyName("projectPath")] string ProjectPath,
    [property: JsonPropertyName("assessmentId")] string AssessmentId,
    [property: JsonPropertyName("read"), JsonRequired] PhonologyResultsRead Read);

/// <summary>The closed choice of what a phonology results request returns.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind", UnknownDerivedTypeHandling =
    JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(PhonologyResultsRead.Page), "page")]
[JsonDerivedType(typeof(PhonologyResultsRead.Example), "example")]
public abstract record PhonologyResultsRead
{
    private PhonologyResultsRead() { }

    /// <summary>Returns a bounded page of example summaries in authored order.</summary>
    /// <param name="PageSize">The maximum number of summaries to return, from 1 through 100.</param>
    /// <param name="Continuation">The cursor for this exact assessment from the preceding page, or null for the first page.</param>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Page(
        [property: JsonPropertyName("pageSize")] int PageSize = 20,
        [property: JsonPropertyName("continuation")] PhonologyPageCursor? Continuation = null) : PhonologyResultsRead;

    /// <summary>Returns the complete retained detail of one example.</summary>
    /// <param name="ExampleId">The id of the example within the checked revision.</param>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Example(
        [property: JsonPropertyName("exampleId"), JsonRequired] string ExampleId) : PhonologyResultsRead;
}
