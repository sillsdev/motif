using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SIL.Motif.Model.Effects;

/// <summary>
/// Renders a set of <see cref="ExpectedEffect"/>s as (pre-canonicalization) JSON text: the preimage
/// for the effect digest — the Proposal contract's "Expected effects" rule 4: "the effect
/// digest is the RFC 8785 hash of the full set of <c>(canonicalId, field, before, after)</c> deltas".
/// </summary>
/// <remarks>
/// The effect set is semantically a *set*, not a sequence, but it is rendered as a JSON array, and
/// RFC 8785 canonicalization preserves array order rather than sorting it — so this writer must
/// presort deterministically itself, the same discipline
/// <see cref="SIL.Motif.Contract.Canonicalization.IntentProjectionWriter"/> applies to
/// <c>requires</c>/<c>dependsOn</c>. Effects sort through <c>EffectTupleOrdering</c>: by canonical id
/// spelling first (<see cref="SIL.Motif.Contract.Ids.CanonicalId.SpellingOrder"/>), then by field name
/// (ordinal), then canonical UTF-8 bytes of before and after. Published output also uses preview bytes to break remaining ties;
/// preview evidence never participates in semantic ordering or the effect digest.
/// </remarks>
public static class ExpectedEffectSetJsonWriter
{
    public static string WriteJson(IEnumerable<ExpectedEffect> effects)
        => WriteJson(effects, includePreview: false);

    public static string WritePublishedJson(IEnumerable<ExpectedEffect> effects)
        => WriteJson(effects, includePreview: true);

    private static string WriteJson(IEnumerable<ExpectedEffect> effects, bool includePreview)
    {
        var ordered = EffectTupleOrdering.Sort(effects, includeAfter: true, includePreview: includePreview);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var effect in ordered)
            {
                writer.WriteStartObject();
                writer.WriteString("canonicalId", effect.CanonicalId.Value);
                writer.WriteString("field", effect.Field);
                WriteAlternatives(writer, "before", effect.Before);
                WriteAlternatives(writer, "after", effect.After);
                if (includePreview && effect.Preview is { } preview)
                {
                    writer.WritePropertyName("preview");
                    preview.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteAlternatives(
        Utf8JsonWriter writer, string propertyName, IReadOnlyDictionary<string, string> alternatives)
    {
        writer.WriteStartObject(propertyName);
        foreach (var alternative in alternatives.OrderBy(item => item.Key, StringComparer.Ordinal))
            writer.WriteString(alternative.Key, alternative.Value);
        writer.WriteEndObject();
    }
}
