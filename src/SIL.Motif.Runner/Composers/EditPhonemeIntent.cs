using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Composers;

/// <summary>One saved written representation of a phoneme and the spelling that should replace it.</summary>
/// <param name="From">The representation the phoneme has now.</param>
/// <param name="To">The representation it should have instead.</param>
public sealed record PhonemeRespelling(string From, string To);

/// <summary>
/// Adds written representations to, or respells representations of, one existing phoneme. A multigraph such as
/// ng, ts or dy is one representation of one phoneme.
/// </summary>
/// <param name="Target">The existing phoneme identity.</param>
/// <param name="Add">New representations, appended after the phoneme's current ones.</param>
/// <param name="Respell">Current representations to replace in place, keeping the code's identity.</param>
public sealed record EditPhonemeIntent(CanonicalId Target, IReadOnlyList<string> Add,
    IReadOnlyList<PhonemeRespelling> Respell);

/// <summary>Parses the closed phoneme edit document.</summary>
public static class EditPhonemeIntentParser
{
    /// <summary>Parses <c>{ target, add: [{representation}], respell: [{from, to}] }</c>.</summary>
    /// <exception cref="ContractParseException">The document has unknown fields or malformed values.</exception>
    public static EditPhonemeIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "EditPhoneme", "target", "add", "respell");
        return new(SoundSystemIntentParsing.Id(authored, "target"),
            authored.TryGetProperty("add", out _)
                ? SoundSystemIntentParsing.Array(authored, "add").Select(item =>
                {
                    SoundSystemIntentParsing.Object(item, "EditPhoneme add", "representation");
                    return SoundSystemIntentParsing.Text(item, "representation");
                }).ToArray()
                : [],
            authored.TryGetProperty("respell", out _)
                ? SoundSystemIntentParsing.Array(authored, "respell").Select(item =>
                {
                    SoundSystemIntentParsing.Object(item, "EditPhoneme respell", "from", "to");
                    return new PhonemeRespelling(
                        SoundSystemIntentParsing.Text(item, "from"), SoundSystemIntentParsing.Text(item, "to"));
                }).ToArray()
                : []);
    }
}
