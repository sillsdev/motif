using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Names an ad hoc prohibition to create: its kind, the first morpheme or allomorph, the rest and the adjacency.</summary>
public sealed record AuthorAdhocProhibitionIntent(string Kind, CanonicalId First, IReadOnlyList<CanonicalId> Rest, string Adjacency);

/// <summary>Reads a closed author-ad-hoc-prohibition intent; the adjacency defaults to anywhere.</summary>
public static class AuthorAdhocProhibitionIntentParser
{
    public static AuthorAdhocProhibitionIntent Parse(JsonElement input)
    {
        SoundSystemIntentParsing.Object(input, "AuthorAdhocProhibition", "kind", "first", "rest", "adjacency");
        var kind = SoundSystemIntentParsing.Text(input, "kind");
        if (kind is not (AdhocProhibitionPayload.Morpheme or AdhocProhibitionPayload.Allomorph))
            throw new ContractParseException("'AuthorAdhocProhibition': kind must be morpheme or allomorph.");
        var adjacency = input.TryGetProperty("adjacency", out _) ? SoundSystemIntentParsing.Text(input, "adjacency") : "anywhere";
        if (!AdhocProhibitionPayload.Adjacencies.Contains(adjacency))
            throw new ContractParseException($"'AuthorAdhocProhibition': adjacency must be one of {string.Join(", ", AdhocProhibitionPayload.Adjacencies)}.");
        return new(kind, SoundSystemIntentParsing.Id(input, "first"), SoundSystemIntentParsing.Ids(input, "rest", ordered: true), adjacency);
    }
}

/// <summary>Stages the creation of a morpheme or allomorph co-occurrence prohibition, FieldWorks' ad hoc rule.</summary>
public static class AuthorAdhocProhibitionComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, AuthorAdhocProhibitionIntent intent, Func<CanonicalId>? mintId = null)
    {
        var payload = new AdhocProhibitionPayload(intent.Kind, intent.First, intent.Rest, intent.Adjacency);
        var (first, rest) = AdhocProhibition.Check(cache, payload);
        var morphData = cache.LangProject.MorphologicalDataOA;
        var mint = mintId ?? (() => CanonicalId.Mint());
        return [new(mint(), AdhocProhibitionCreateOperationKinds.Create, target: CanonicalId.FromGuid(morphData.Guid), entityId: mint(),
            after: JsonSerializer.SerializeToElement(new
            {
                kind = intent.Kind,
                first = intent.First.Value,
                rest = intent.Rest.Select(id => id.Value).ToArray(),
                adjacency = intent.Adjacency,
            }), rationale: "Prohibit this co-occurrence: " + AdhocProhibition.Describe(payload, first, rest) + "." +
                (AdhocProhibition.DirectionWarning(payload, first, rest) is { } warning ? " Warning: " + warning : ""))];
    }
}
