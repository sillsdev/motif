using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Names an ad hoc prohibition to create: its kind, the first morpheme or allomorph, the rest and the adjacency.</summary>
/// <param name="Kind">Morpheme or allomorph.</param>
/// <param name="First">The morpheme or allomorph the prohibition is stated about.</param>
/// <param name="Rest">The sequence the first cannot occur with; empty when <paramref name="RestAnyOf"/> is used.</param>
/// <param name="Adjacency">Where the first sits relative to the rest.</param>
/// <param name="RestAnyOf">
/// A set of alternatives; each member becomes its own prohibition, because FieldWorks holds the rest as a sequence.
/// </param>
public sealed record AuthorAdhocProhibitionIntent(
    string Kind, CanonicalId First, IReadOnlyList<CanonicalId> Rest, string Adjacency,
    IReadOnlyList<CanonicalId>? RestAnyOf = null);

/// <summary>Reads a closed author-ad-hoc-prohibition intent; the adjacency defaults to anywhere.</summary>
public static class AuthorAdhocProhibitionIntentParser
{
    public static AuthorAdhocProhibitionIntent Parse(JsonElement input)
    {
        SoundSystemIntentParsing.Object(input, "AuthorAdhocProhibition", "kind", "first", "rest", "restAnyOf", "adjacency");
        var kind = SoundSystemIntentParsing.Text(input, "kind");
        if (kind is not (AdhocProhibitionPayload.Morpheme or AdhocProhibitionPayload.Allomorph))
            throw new ContractParseException("'AuthorAdhocProhibition': kind must be morpheme or allomorph.");
        var adjacency = input.TryGetProperty("adjacency", out _) ? SoundSystemIntentParsing.Text(input, "adjacency") : "anywhere";
        if (!AdhocProhibitionPayload.Adjacencies.Contains(adjacency))
            throw new ContractParseException($"'AuthorAdhocProhibition': adjacency must be one of {string.Join(", ", AdhocProhibitionPayload.Adjacencies)}.");
        var hasRest = input.TryGetProperty("rest", out _);
        var hasAnyOf = input.TryGetProperty("restAnyOf", out _);
        if (hasRest == hasAnyOf)
            throw new ContractParseException("'AuthorAdhocProhibition': give exactly one of 'rest' (a sequence) or 'restAnyOf' (a set of alternatives).");
        var anyOf = hasAnyOf ? SoundSystemIntentParsing.Ids(input, "restAnyOf", ordered: false) : null;
        if (anyOf is { Length: 0 })
            throw new ContractParseException("'AuthorAdhocProhibition': 'restAnyOf' must name at least one alternative.");
        return new(kind, SoundSystemIntentParsing.Id(input, "first"),
            hasRest ? SoundSystemIntentParsing.Ids(input, "rest", ordered: true) : [], adjacency, anyOf);
    }
}

/// <summary>
/// Stages the creation of a morpheme or allomorph co-occurrence prohibition, FieldWorks' ad hoc rule. A set of
/// alternatives expands into one prohibition per member, because the parser reads a prohibition's rest as one
/// sequence that must all be present.
/// </summary>
public static class AuthorAdhocProhibitionComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, AuthorAdhocProhibitionIntent intent, Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var mint = mintId ?? (() => CanonicalId.Mint());
        if (intent.RestAnyOf is not { } alternatives) return [Build(cache, intent, intent.Rest, mint)];
        if (intent.Rest.Count > 0)
            throw new InvalidOperationException("'AuthorAdhocProhibition': give 'rest' or 'restAnyOf', not both.");
        if (alternatives.Count == 0)
            throw new InvalidOperationException("'AuthorAdhocProhibition': 'restAnyOf' must name at least one alternative.");
        if (alternatives.Select(id => id.ToGuid()).Distinct().Count() != alternatives.Count)
            throw new InvalidOperationException("'AuthorAdhocProhibition': 'restAnyOf' must not repeat an alternative.");
        return alternatives.OrderBy(id => id.Value, StringComparer.Ordinal)
            .Select(member => Build(cache, intent, [member], mint)).ToArray();
    }

    private static OperationEnvelope Build(
        LcmCache cache, AuthorAdhocProhibitionIntent intent, IReadOnlyList<CanonicalId> rest, Func<CanonicalId> mint)
    {
        var payload = new AdhocProhibitionPayload(intent.Kind, intent.First, rest, intent.Adjacency);
        var (first, others) = AdhocProhibition.Check(cache, payload);
        var morphData = cache.LangProject.MorphologicalDataOA;
        return new(mint(), AdhocProhibitionCreateOperationKinds.Create, target: CanonicalId.FromGuid(morphData.Guid), entityId: mint(),
            after: JsonSerializer.SerializeToElement(new
            {
                kind = intent.Kind,
                first = intent.First.Value,
                rest = rest.Select(id => id.Value).ToArray(),
                adjacency = intent.Adjacency,
            }), rationale: "Prohibit this co-occurrence: " + AdhocProhibition.Describe(payload, first, others) + "." +
                (AdhocProhibition.DirectionWarning(payload, first, others) is { } warning ? " Warning: " + warning : ""));
    }
}
