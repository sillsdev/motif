using SIL.LCModel;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Mcp;

/// <summary>
/// Names the rules in a failed word's answer as FieldWorks shows them. The concise trace skips the project copy,
/// so a failed word reads the shared Baseline copy once, only for the rules it mentions.
/// </summary>
internal static class TryWordRuleNames
{
    /// <remarks>
    /// A word traced against a Proposal's Trial copy keeps the parser's ids: the Baseline holds neither the Trial's
    /// new rules nor its renames, so a Baseline name could describe a different grammar than the one parsed.
    /// </remarks>
    public static WordTraceResponse Named(string projectPath, WordTraceResponse response, string? proposal = null)
    {
        if (proposal is not null || response.Parsed || response.InvalidShape || TraceRuleNames.AuthoredIdentities(response) is not { Count: > 0 } ids)
            return response;
        var names = new Dictionary<Guid, string?>();
        var read = BaselineReads.Read(projectPath, cache =>
        {
            var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
            foreach (var id in ids)
                names[id] = repository.TryGetObject(id, out var found) ? TraceRuleNames.NameOf(found) : null;
            return new System.Text.Json.Nodes.JsonObject();
        });
        return read.Succeeded ? TraceRuleNames.Apply(response, id => names.GetValueOrDefault(id)) : response;
    }
}
