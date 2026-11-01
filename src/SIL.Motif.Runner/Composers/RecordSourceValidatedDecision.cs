using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>
/// Stages a decision a named program made from cited sources: an approval or disapproval of one analysis under a
/// non-human agent, or the evidence for a wordform or analysis alone. It never decides as a person.
/// </summary>
/// <param name="Target">The existing wordform or analysis the decision or evidence is about.</param>
/// <param name="AgentName">The agent's name, given together with <paramref name="AgentVersion"/> and <paramref name="Approves"/>.</param>
/// <param name="AgentVersion">The source release the agent stands for.</param>
/// <param name="Approves">Whether the agent approves (true) or disapproves (false); null stages evidence alone.</param>
/// <param name="Evidence">The tier, sources and facts the decision rests on.</param>
public sealed record RecordSourceValidatedDecisionIntent(
    CanonicalId Target, string? AgentName, string? AgentVersion, bool? Approves, DecisionEvidence Evidence);

/// <summary>Parses a closed source-validated decision: an agent and member together, or neither.</summary>
public static class RecordSourceValidatedDecisionIntentParser
{
    private const string Kind = "RecordSourceValidatedDecision";

    public static RecordSourceValidatedDecisionIntent Parse(JsonElement input)
    {
        SoundSystemIntentParsing.Object(input, Kind, "target", "agent", "member", "evidence");
        var target = LexicalFormAuthoring.Normalize(SoundSystemIntentParsing.Id(input, "target"));
        var hasAgent = input.TryGetProperty("agent", out var agent);
        var hasMember = input.TryGetProperty("member", out var member);
        if (hasAgent != hasMember)
            throw new ContractParseException($"'{Kind}': 'agent' and 'member' are given together or not at all.");
        string? name = null, version = null;
        bool? approves = null;
        if (hasAgent)
        {
            SoundSystemIntentParsing.Object(agent, Kind, "name", "version");
            name = SoundSystemIntentParsing.Text(agent, "name");
            version = SoundSystemIntentParsing.Text(agent, "version");
            var text = member.ValueKind == JsonValueKind.String ? member.GetString() : null;
            approves = text switch
            {
                "approves" => true,
                "disapproves" => false,
                _ => throw new ContractParseException($"'{Kind}': 'member' must be 'approves' or 'disapproves'."),
            };
        }
        if (!input.TryGetProperty("evidence", out var evidence) || evidence.ValueKind != JsonValueKind.Object)
            throw new ContractParseException($"'{Kind}': 'evidence' is required and must be an object.");
        var entry = new JsonObject { ["type"] = DecisionEvidence.ProvenanceType };
        foreach (var property in evidence.EnumerateObject()) entry[property.Name] = JsonNode.Parse(property.Value.GetRawText());
        var parsed = DecisionEvidence.FromProvenance([JsonSerializer.SerializeToElement(entry)])
            ?? throw new ContractParseException($"'{Kind}': 'evidence' is not a decision evidence record.");
        return new RecordSourceValidatedDecisionIntent(target, name, version, approves, parsed);
    }
}

/// <summary>Lowers a source-validated decision to one agent evaluation or one evidence record, carrying its evidence.</summary>
public static class RecordSourceValidatedDecisionComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, RecordSourceValidatedDecisionIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var target = LexicalFormAuthoring.Resolve<ICmObject>(cache, intent.Target, Kind);
        var provenance = new[] { intent.Evidence.ToProvenanceEntry() };
        if (intent.Approves is not { } approves)
        {
            if (target is not (IWfiWordform or IWfiAnalysis))
                throw new InvalidOperationException("The target must be an existing wordform or analysis.");
            return [new(CanonicalId.Mint(), DecisionEvidenceOperationKinds.Record, target: intent.Target,
                after: JsonSerializer.SerializeToElement(new { }), provenance: provenance,
                rationale: "Authored by the RecordSourceValidatedDecision composer.")];
        }
        if (target is not IWfiAnalysis)
            throw new InvalidOperationException("An approval or disapproval needs an existing analysis as its target.");
        var after = JsonSerializer.SerializeToElement(new
        {
            agent = new
            {
                id = AgentIdentity(cache, intent.AgentName!, intent.AgentVersion!).Value,
                name = intent.AgentName,
                version = intent.AgentVersion,
            },
            member = approves ? "approves" : "disapproves",
        });
        return [new(CanonicalId.Mint(), WfiAnalysisAgentEvaluationOperationKinds.Add, target: intent.Target, after: after,
            provenance: provenance, rationale: "Authored by the RecordSourceValidatedDecision composer.")];
    }

    private const string Kind = "RecordSourceValidatedDecision";

    /// <summary>One identity per project, name and version, so two Drafts agree on which agent decided.</summary>
    public static CanonicalId AgentIdentity(LcmCache cache, string name, string version)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"motif-source-validation\n{cache.LangProject.Guid:D}\n{name}\n{version}"));
        return CanonicalId.FromGuid(Guid.Parse(Convert.ToHexString(hash.AsSpan(0, 16))));
    }
}
