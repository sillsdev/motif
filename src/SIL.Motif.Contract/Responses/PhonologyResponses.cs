using System.Text.Json.Serialization;
using SIL.Motif.Contract.Phonology;

namespace SIL.Motif.Contract.Responses;

/// <summary>Confirms the current revision created or updated by a save request.</summary>
/// <param name="ExampleSet">The saved revision header.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PhonologyExamplesSavedResponse(
    [property: JsonPropertyName("exampleSet")] PhonologyExampleSetHeader ExampleSet);

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
