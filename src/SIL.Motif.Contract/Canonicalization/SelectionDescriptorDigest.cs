using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Canonicalization;

/// <summary>Computes the RFC 8785 digest of a canonical retained Selection descriptor.</summary>
public static class SelectionDescriptorDigest
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Hashes descriptor content without including the digest field itself.</summary>
    public static string Compute(SelectionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var projection = new DescriptorProjection(
            descriptor.TextIds.Select(id => id.ToString("D")).ToArray(),
            descriptor.PastedWords.ToArray(), descriptor.AllWordforms, descriptor.RetryFailed,
            descriptor.RetrySourceAssessmentId, descriptor.RetrySlowerThan?.Ticks,
            descriptor.ResolvedWords.ToArray(), descriptor.ResolvedSha256,
            descriptor.SourceCounts.Select(entry => new SourceCountProjection(entry.Source, entry.Count)).ToArray());
        var json = JsonSerializer.Serialize(projection, Options);
        return IntentDigest.Sha256Of(CanonicalJson.CanonicalizeToUtf8(json));
    }

    private sealed record DescriptorProjection(
        IReadOnlyList<string> TextIds,
        IReadOnlyList<string> PastedWords,
        bool AllWordforms,
        bool RetryFailed,
        string? RetrySourceAssessmentId,
        long? RetrySlowerThanTicks,
        IReadOnlyList<string> ResolvedWords,
        string ResolvedSha256,
        IReadOnlyList<SourceCountProjection> SourceCounts);

    private sealed record SourceCountProjection(string Source, int Count);
}
