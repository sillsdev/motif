using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Model.Effects;

/// <summary>
/// Orders effect tuples by id spelling (the digest order of ADR 0007 decision 2) and field, then by canonical
/// UTF-8 payload bytes when those keys tie.
/// The sort includes only the payload parts the caller emits; excluded evidence cannot affect ordering.
/// </summary>
internal static class EffectTupleOrdering
{
    private static readonly IComparer<byte[]> ByteOrder = Comparer<byte[]>.Create(
        (left, right) => left.AsSpan().SequenceCompareTo(right));

    internal static IReadOnlyList<ExpectedEffect> Sort(IEnumerable<ExpectedEffect> effects,
        bool includeAfter, bool includePreview)
    {
        var ordered = effects.OrderBy(effect => effect.CanonicalId, CanonicalId.SpellingOrder)
            .ThenBy(effect => effect.Field, StringComparer.Ordinal).ToList();
        for (var start = 0; start < ordered.Count;)
        {
            var end = start + 1;
            while (end < ordered.Count && ordered[end].CanonicalId.SpellingEquals(ordered[start].CanonicalId) &&
                string.Equals(ordered[end].Field, ordered[start].Field, StringComparison.Ordinal)) end++;
            if (end - start > 1)
            {
                var tied = ordered.GetRange(start, end - start).Select(effect => new KeyedEffect(effect,
                    CanonicalJson.CanonicalizeToUtf8(JsonSerializer.Serialize(effect.Before)),
                    includeAfter ? CanonicalJson.CanonicalizeToUtf8(JsonSerializer.Serialize(effect.After)) : [],
                    includePreview && effect.Preview is { } preview
                        ? CanonicalJson.CanonicalizeToUtf8(preview.GetRawText()) : []))
                    .OrderBy(item => item.Before, ByteOrder)
                    .ThenBy(item => item.After, ByteOrder)
                    .ThenBy(item => item.Preview, ByteOrder).ToArray();
                for (var index = 0; index < tied.Length; index++) ordered[start + index] = tied[index].Effect;
            }
            start = end;
        }
        return ordered;
    }

    private sealed record KeyedEffect(ExpectedEffect Effect, byte[] Before, byte[] After, byte[] Preview);
}
