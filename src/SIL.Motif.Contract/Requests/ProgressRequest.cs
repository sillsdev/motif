using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Requests;

/// <summary>A Text or Source population. An omitted or empty Text list selects every captured Text.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProgressScope(string Kind, IReadOnlyList<Guid>? Texts = null, string? Source = null);

/// <summary>Reads saved Text Progress or Source denominators in summary or paged Passage form.</summary>
/// <param name="ProjectPath">The FieldWorks project paired with the store.</param>
/// <param name="Scope">The Text or Source population to count.</param>
/// <param name="Detail">Either <c>summary</c> or <c>passages</c>.</param>
/// <param name="Limit">Maximum Passage rows per page, from 1 to 200.</param>
/// <param name="Cursor">A continuation from the same scope and Progress revision.</param>
/// <param name="SinceRevision">An optional checkpoint; changed-since evidence is currently unavailable.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProgressRequest(string ProjectPath, ProgressScope Scope, string Detail = "summary", int Limit = 50,
    string? Cursor = null, string? SinceRevision = null);
