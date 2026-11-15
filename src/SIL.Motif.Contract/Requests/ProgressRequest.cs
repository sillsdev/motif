using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Requests;

/// <summary>A Text population. An omitted or empty Text list selects every captured Text.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProgressScope(string Kind, IReadOnlyList<Guid>? Texts = null);

/// <summary>Reads saved Text Progress in summary or paged Passage form.</summary>
/// <param name="ProjectPath">The FieldWorks project paired with the store.</param>
/// <param name="Scope">The Text population to count.</param>
/// <param name="Detail">Either <c>summary</c> or <c>passages</c>.</param>
/// <param name="Limit">Maximum Passage rows per page, from 1 to 200.</param>
/// <param name="Cursor">A continuation from the same scope and Progress revision.</param>
/// <param name="SinceRevision">An optional checkpoint; changed-since evidence is currently unavailable.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProgressRequest(string ProjectPath, ProgressScope Scope, string Detail = "summary", int Limit = 50,
    string? Cursor = null, string? SinceRevision = null);
