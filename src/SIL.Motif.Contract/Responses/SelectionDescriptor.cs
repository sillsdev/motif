using System;
using System.Collections.Generic;

namespace SIL.Motif.Contract.Responses;

/// <summary>
/// The caller's complete Selection request together with the words and source counts that were actually
/// resolved. Retry provenance is recorded as the exact source Assessment selected for that request.
/// </summary>
/// <param name="TextIds">The chosen Text identities, sorted by GUID and without duplicates.</param>
/// <param name="PastedWords">The pasted or typed entries, trimmed, nonblank, NFD-normalized, and in meaningful order.</param>
/// <param name="AllWordforms">Whether the project wordform source was requested.</param>
/// <param name="RetryFailed">Whether failed words from the retry source were requested.</param>
/// <param name="RetrySourceAssessmentId">The resolved Baseline ParseTime source, when retry was requested.</param>
/// <param name="RetrySlowerThan">The elapsed-time threshold used by the retry source, when supplied.</param>
/// <param name="ResolvedWords">The distinct, ordinally sorted words passed to the Assessor.</param>
/// <param name="ResolvedSha256">The hash of <paramref name="ResolvedWords"/>.</param>
/// <param name="SourceCounts">The words contributed by each requested source before union.</param>
public sealed record SelectionDescriptor(
    IReadOnlyList<Guid> TextIds,
    IReadOnlyList<string> PastedWords,
    bool AllWordforms,
    bool RetryFailed,
    string? RetrySourceAssessmentId,
    TimeSpan? RetrySlowerThan,
    IReadOnlyList<string> ResolvedWords,
    string ResolvedSha256,
    IReadOnlyList<SelectionProvenanceEntry> SourceCounts)
{
    /// <summary>The RFC 8785 SHA-256 digest of this descriptor's canonical content.</summary>
    public string DescriptorSha256 { get; init; } = string.Empty;
}
