using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// Names the operation kind that records a named non-human agent's approval or disapproval of one analysis.
/// </summary>
/// <remarks>
/// The closed <c>after</c> payload is <c>{ "agent": { "id", "name", "version" }, "member": "approves" | "disapproves" }</c>.
/// The agent is found by its authored identity or created non-human under the project's analyzing agents; it is
/// never matched by name. The default user agent, the parser agent and every other built-in agent are refused, so
/// no route from this kind reaches a person's decision. One agent exists per name and version.
/// </remarks>
public static class WfiAnalysisAgentEvaluationOperationKinds
{
    public const string Add = "analysis/wfiAnalysis/addAgentEvaluations";

    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(Add);
        OperationHandlerRegistry.Register(Add, new AgentEvaluationHandler());
        OperationSlotRegistry.Register(Add, operation => new OperationSlot(
            OperationSlotRegistry.Target(operation), SnapshotFields.WfiAnalysisEvaluations,
            OperationSlotRegistry.MemberKey(AgentEvaluationPayload.Parse(OperationSlotRegistry.After(operation)).AgentId)));
    }
}

/// <summary>The parsed closed payload of <see cref="WfiAnalysisAgentEvaluationOperationKinds.Add"/>.</summary>
public sealed record AgentEvaluationPayloadValue(CanonicalId AgentId, string Name, string Version, bool Approves);

/// <summary>The closed payload of <see cref="WfiAnalysisAgentEvaluationOperationKinds.Add"/>.</summary>
public static class AgentEvaluationPayload
{
    public const string Approves = "approves";
    public const string Disapproves = "disapproves";
    public const int MaxNameLength = 80;
    public const int MaxVersionLength = 60;

    public static AgentEvaluationPayloadValue Parse(JsonElement after)
    {
        const string kind = WfiAnalysisAgentEvaluationOperationKinds.Add;
        ClosedPayloadParsing.RequireObject(after, kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, ["agent", "member"], kind);
        if (!after.TryGetProperty("agent", out var agent))
            throw new ContractParseException($"'{kind}' operation requires 'after.agent'.");
        ClosedPayloadParsing.RequireObject(agent, kind);
        ClosedPayloadParsing.RejectUnknownProperties(agent, ["id", "name", "version"], kind);
        var id = ClosedPayloadParsing.GetRequiredCanonicalId(agent, "id", kind);
        var name = Text(agent, "name", MaxNameLength, kind);
        var version = Text(agent, "version", MaxVersionLength, kind);
        var member = ClosedPayloadParsing.GetRequiredString(after, "member", kind);
        if (member != Approves && member != Disapproves)
            throw new ContractParseException($"'{kind}' operation 'after.member' must be 'approves' or 'disapproves'.");
        return new AgentEvaluationPayloadValue(id, name, version, member == Approves);
    }

    private static string Text(JsonElement parent, string property, int max, string kind)
    {
        var value = ClosedPayloadParsing.GetRequiredString(parent, property, kind);
        if (value.Length == 0 || value.Length > max || value != value.Trim() || value.Any(char.IsControl) ||
            !value.IsNormalized(NormalizationForm.FormD))
            throw new ContractParseException(
                $"'{kind}' operation 'after.agent.{property}' must be 1 to {max} characters, trimmed, in NFD, with no control characters.");
        return value;
    }
}

internal sealed class AgentEvaluationHandler : IOperationHandler
{
    private const string Kind = WfiAnalysisAgentEvaluationOperationKinds.Add;

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var payload = AgentEvaluationPayload.Parse(operation.After ?? throw new ContractParseException(
            $"'{Kind}' operation requires 'after'."));
        var (id, analysis) = TargetResolution.Resolve<IWfiAnalysis>(cache, operation, Kind);
        touchedTargets.Add(id);
        var before = Read(cache, analysis, payload);
        var agent = FindOrCreateAgent(cache, payload);
        agent.SetEvaluation(analysis, payload.Approves ? Opinions.approves : Opinions.disapproves);
        return new ExpectedEffect(id, SnapshotFields.WfiAnalysisEvaluations, before, Read(cache, analysis, payload));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var payload = AgentEvaluationPayload.Parse(operation.After ?? throw new ContractParseException(
            $"'{Kind}' operation requires 'after'."));
        var (id, analysis) = TargetResolution.Resolve<IWfiAnalysis>(cache, operation, Kind);
        RequireUsable(cache, payload);
        var current = Read(cache, analysis, payload);
        return new ExpectedEffect(id, SnapshotFields.WfiAnalysisEvaluations, current, current);
    }

    private static HashSet<Guid> BuiltInAgents(LcmCache cache)
    {
        var project = cache.LangProject;
        return new[] { project.DefaultUserAgent, project.DefaultParserAgent, project.DefaultComputerAgent,
            project.ConstraintCheckerAgent }.Where(agent => agent is not null).Select(agent => agent.Guid).ToHashSet();
    }

    // One agent per name and version, so a built-in, human or renamed agent is never reused.
    private static ICmAgent? RequireUsable(LcmCache cache, AgentEvaluationPayloadValue payload)
    {
        var guid = payload.AgentId.ToGuid();
        if (BuiltInAgents(cache).Contains(guid))
            throw new InvalidOperationException($"'{Kind}': agent '{payload.AgentId.Value}' is a built-in agent; " +
                "a source-validated decision is recorded under its own non-human agent.");
        var human = cache.LangProject.DefaultUserAgent;
        if (human is not null && human.Name.AvailableWritingSystemIds.Select(ws => human.Name.get_String(ws).Text)
                .Any(text => string.Equals(text, payload.Name, StringComparison.Ordinal)))
            throw new InvalidOperationException($"'{Kind}': agent name '{payload.Name}' is the human agent's name.");
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        if (!repository.TryGetObject(guid, out var found)) return null;
        if (found is not ICmAgent existing)
            throw new InvalidOperationException($"'{Kind}': identity '{payload.AgentId.Value}' already exists as " +
                $"{found.ClassName}, expected CmAgent; this is a semantic conflict.");
        if (existing.Human)
            throw new InvalidOperationException($"'{Kind}': agent '{payload.AgentId.Value}' is a human agent.");
        var name = existing.Name.get_String(cache.DefaultAnalWs).Text;
        if (!string.Equals(name, payload.Name, StringComparison.Ordinal) ||
            !string.Equals(existing.Version, payload.Version, StringComparison.Ordinal))
            throw new InvalidOperationException($"'{Kind}': agent '{payload.AgentId.Value}' exists as " +
                $"'{name}' version '{existing.Version}', not '{payload.Name}' version '{payload.Version}'; " +
                "a new version is a new agent identity.");
        return existing;
    }

    private static ICmAgent FindOrCreateAgent(LcmCache cache, AgentEvaluationPayloadValue payload)
    {
        if (RequireUsable(cache, payload) is { } existing) return existing;
        var agent = cache.ServiceLocator.GetInstance<ICmAgentFactory>().Create(payload.AgentId.ToGuid());
        cache.LangProject.AnalyzingAgentsOC.Add(agent);
        agent.Human = false;
        agent.Name.set_String(cache.DefaultAnalWs, TsStringUtils.MakeString(payload.Name, cache.DefaultAnalWs));
        agent.Version = payload.Version;
        return agent;
    }

    private static Dictionary<string, string> Read(LcmCache cache, IWfiAnalysis analysis,
        AgentEvaluationPayloadValue payload)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        if (!repository.TryGetObject(payload.AgentId.ToGuid(), out var found) || found is not ICmAgent agent)
        {
            result["opinion:" + payload.AgentId.Value] = "none";
            return result;
        }
        result["agent:" + payload.AgentId.Value] =
            $"{agent.Name.get_String(cache.DefaultAnalWs).Text}|{agent.Version}|human={agent.Human}";
        result["opinion:" + payload.AgentId.Value] = analysis.GetAgentOpinion(agent) switch
        {
            Opinions.approves => "approves",
            Opinions.disapproves => "disapproves",
            _ => "none",
        };
        return result;
    }
}
