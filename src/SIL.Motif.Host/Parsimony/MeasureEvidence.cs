using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host.Parsimony;

/// <summary>Versioned item evidence for one measure, excluding display text and unrelated project content.</summary>
public static class MeasureEvidence
{
    public const int PreimageVersion = 2;

    public static string Contract(MeasureDefinition measure) => measure.QueryId
        ?? throw new ArgumentException("The measure has no registered detector.", nameof(measure));

    public static string Compute(MeasureDefinition measure, string relevantEvidence,
        int factsProjectionVersion, int evidenceProjectionVersion, string? scope = null,
        string? selectionDigest = null, string? humanInputRevision = null)
    {
        var preimage = new JsonObject
        {
            ["schema"] = "motif-parsimony-evidence/v" + PreimageVersion,
            ["measure"] = measure.Id, ["detector"] = Contract(measure),
            ["threshold"] = JsonSerializer.SerializeToNode(measure.Threshold),
            ["factsProjection"] = factsProjectionVersion, ["evidenceProjection"] = evidenceProjectionVersion,
            ["capabilities"] = JsonSerializer.SerializeToNode(measure.RequiredCapabilities.Order(StringComparer.Ordinal)),
            ["scope"] = scope, ["selection"] = selectionDigest, ["humanInputRevision"] = humanInputRevision,
            ["evidence"] = JsonNode.Parse(relevantEvidence),
        };
        return Convert.ToHexStringLower(SHA256.HashData(CanonicalJson.CanonicalizeToUtf8(preimage.ToJsonString())));
    }

    internal static string Compute(MeasureDefinition measure, ParsimonyQuerySession session, string evidence)
    {
        var inputs = session.Inputs;
        var scoped = measure.Tier != ParsimonyTier.Static || measure.EvidenceViews.Any(view =>
            view is "approved-morph-sequences" or "environment-excess" or "natural-class-excess" or
                "template-precedence" or "slot-blocking" or "adhoc-slot-order");
        return Compute(measure, evidence, inputs.GrammarFacts.SchemaVersion,
            inputs.Evidence.SchemaVersion, scoped ? inputs.EvidenceScope.ToString() : null,
            scoped && inputs.EvidenceScope == ParsimonyEvidenceScopeKind.DefaultSelection ? inputs.SelectionSha256 : null,
            measure.RequiredCapabilities.Contains("reviewed-negative-expectations") ? inputs.ExpectationRevisionSha256 : null);
    }
}
