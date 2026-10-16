using System.Text.Json.Serialization;
using SIL.Motif.Contract.Phonology;

namespace SIL.Motif.Contract.Responses;

/// <summary>Confirms the current revision created or updated by a save request.</summary>
/// <param name="ExampleSet">The saved revision header.</param>
/// <param name="EntryVerification">Whether the examples' FieldWorks entries were found in the current Baseline:
/// <c>verified</c>, <c>not-applicable</c> (no example names a FieldWorks entry), <c>not-verified-no-baseline</c>
/// or <c>not-verified-baseline-file-missing</c>.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PhonologyExamplesSavedResponse(
    [property: JsonPropertyName("exampleSet")] PhonologyExampleSetHeader ExampleSet,
    [property: JsonPropertyName("entryVerification"), JsonRequired] string EntryVerification);

/// <summary>Returns a page of Example Set headers in a project.</summary>
/// <param name="Items">The headers in this page.</param>
/// <param name="Continuation">The cursor for the next page, or null when the list is complete.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PhonologyExamplesListResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<PhonologyExampleSetHeader> Items,
    [property: JsonPropertyName("continuation")] PhonologySetListCursor? Continuation);

/// <summary>Returns a page of examples from one immutable revision.</summary>
/// <param name="ExampleSet">The header for the revision containing these examples.</param>
/// <param name="Items">The examples in this page, in authored order.</param>
/// <param name="Continuation">The cursor for the next page, or null when the revision is complete.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PhonologyExamplesPageResponse(
    [property: JsonPropertyName("exampleSet")] PhonologyExampleSetHeader ExampleSet,
    [property: JsonPropertyName("items")] IReadOnlyList<PhonologyExample> Items,
    [property: JsonPropertyName("continuation")] PhonologyPageCursor? Continuation);

/// <summary>The stored result of a phonology check read: a page of summaries or one complete example.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind", UnknownDerivedTypeHandling =
    JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(PhonologyResultsPageResponse), "page")]
[JsonDerivedType(typeof(PhonologyExampleResultResponse), "example")]
public abstract record PhonologyResultsResponse;

/// <summary>A bounded page of example summaries from one check.</summary>
/// <param name="Assessment">The stored header of the check these summaries belong to.</param>
/// <param name="Items">The summaries in this page, in authored order.</param>
/// <param name="Continuation">The cursor for the next page, or null when the check is complete.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PhonologyResultsPageResponse(
    [property: JsonPropertyName("assessment"), JsonRequired] PhonologyAssessmentHeader Assessment,
    [property: JsonPropertyName("items"), JsonRequired] IReadOnlyList<PhonologyExampleSummary> Items,
    [property: JsonPropertyName("continuation"), JsonRequired] PhonologyPageCursor? Continuation)
    : PhonologyResultsResponse;

/// <summary>The complete retained detail of one example from one check.</summary>
/// <param name="Assessment">The stored header of the check this example belongs to.</param>
/// <param name="Result">The complete retained example, including every rule list.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PhonologyExampleResultResponse(
    [property: JsonPropertyName("assessment"), JsonRequired] PhonologyAssessmentHeader Assessment,
    [property: JsonPropertyName("result"), JsonRequired] PhonologyExampleDetail Result)
    : PhonologyResultsResponse;
