using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Projection.HumanJudgments;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Commands;

/// <summary>Compares Parsimony decisions by their whole logical value, never by their minted identities.</summary>
internal static class ParsimonyDecisionIdentity
{
    private static readonly string[] MintedMembers = ["projectId", "judgmentId", "revisionId", "replaces"];

    /// <summary>The key of a decision as it would be stored, excluding minted ids and Report or Proposal provenance.</summary>
    internal static string KeyOf(RecordParsimonyDispositionIntent intent, string projectId)
    {
        // Placeholder ids are valid so the codec accepts the value; the key drops them.
        var judgment = RecordParsimonyDispositionComposer.JudgmentFor(intent, projectId,
            CanonicalId.Mint().Value, CanonicalId.Mint().Value, proposalId: null);
        return KeyOf(judgment);
    }

    /// <summary>Whether a current, non-superseded disposition in the saved project has the same key.</summary>
    internal static bool IsRecorded(LcmCache cache, string key)
    {
        var lineage = JudgmentLineageResolver.Resolve(HumanJudgmentReader.Read(cache));
        return lineage.Revisions.Any(item => item.State == "head" &&
            item.Judgment.Body is DispositionJudgment && KeyOf(item.Judgment) == key);
    }

    /// <summary>Whether a Draft already stages a disposition with the same key.</summary>
    internal static bool IsStaged(IEnumerable<OperationEnvelope> operations, string projectId, string key) =>
        operations.Where(operation => operation.Kind == HumanJudgmentCustomFieldOperationKinds.Set &&
                operation.Target is { } && operation.After is { } after &&
                after.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            .Any(operation => IsSameStagedDecision(operation, projectId, key));

    private static bool IsSameStagedDecision(OperationEnvelope operation, string projectId, string key)
    {
        try
        {
            var text = operation.After!.Value.GetProperty("text").GetString()!;
            var judgment = HumanJudgmentCodec.Parse(text, projectId, operation.Target!.Value.Value);
            return judgment.Body is DispositionJudgment && KeyOf(judgment) == key;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string KeyOf(HumanJudgment judgment)
    {
        var node = JsonNode.Parse(HumanJudgmentCodec.ToJson(judgment))!.AsObject();
        foreach (var member in MintedMembers) node.Remove(member);
        // Report and Proposal ids are provenance: a re-run measure reissues the Report for the same finding.
        if (node["source"] is JsonObject source)
        {
            source.Remove("reportId");
            source.Remove("proposalId");
        }
        return CanonicalJson.Canonicalize(node.ToJsonString());
    }
}
