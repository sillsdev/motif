using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Composers;

/// <summary>Changes the segment members of one existing natural class after checking its current identities.</summary>
/// <param name="Target">The existing natural class identity.</param>
/// <param name="ExpectedMembers">The exact segment identities the class currently contains.</param>
/// <param name="Members">The distinct existing segment identities the class should contain.</param>
public sealed record EditNaturalClassIntent(CanonicalId Target,
    IReadOnlyList<CanonicalId> ExpectedMembers, IReadOnlyList<CanonicalId> Members);

/// <summary>Parses the closed identity lists for an existing segment-class edit.</summary>
public static class EditNaturalClassIntentParser
{
    /// <summary>Parses a natural-class edit request with canonical identity arrays.</summary>
    /// <param name="authored">The JSON object authored by the caller.</param>
    /// <exception cref="ContractParseException">The object contains unsupported fields or malformed identities.</exception>
    public static EditNaturalClassIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "EditNaturalClass", "target", "expectedMembers", "members");
        return new(SoundSystemIntentParsing.Id(authored, "target"),
            Ids(authored, "expectedMembers"), Ids(authored, "members"));
    }

    private static CanonicalId[] Ids(JsonElement authored, string name)
    {
        var values = SoundSystemIntentParsing.Array(authored, name).Select(element =>
            element.ValueKind == JsonValueKind.String && CanonicalId.TryParse(element.GetString(), out var id)
                ? id
                : throw new ContractParseException($"'{name}' must contain canonical id strings.")).ToArray();
        if (values.Distinct().Count() != values.Length)
            throw new ContractParseException($"'{name}' must not contain duplicate ids.");
        return values;
    }
}
