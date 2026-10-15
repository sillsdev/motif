using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// Requires a text create to declare, directly or through other declared dependencies, every wordform create of the
/// same Proposal whose identity one of its occurrences names. Array position never stands in for a dependency.
/// </summary>
internal static class TextCreateDependencies
{
    public static void Validate(IReadOnlyList<OperationEnvelope> operations)
    {
        var wordformCreates = operations
            .Where(operation => operation.Kind == LangProjectWordformsOperationKinds.Create && operation.EntityId is not null)
            .GroupBy(operation => operation.EntityId!.Value, CanonicalId.ByGuid)
            .ToDictionary(group => group.Key, group => group.First().OperationId, CanonicalId.ByGuid);
        if (wordformCreates.Count == 0) return;
        var byId = operations.GroupBy(operation => operation.OperationId).ToDictionary(group => group.Key, group => group.First());
        foreach (var create in operations.Where(operation => operation.Kind == LangProjectTextsOperationKinds.Create))
        {
            if (create.After is not { } after) continue;
            var declared = Declared(create, byId);
            foreach (var segment in LangProjectTextsCreatePayload.Parse(after).Paragraphs.SelectMany(paragraph => paragraph.Segments))
                foreach (var word in segment.Words)
                    if (wordformCreates.TryGetValue(word, out var creator) && !declared.Contains(creator))
                        throw new ContractParseException(
                            $"Operation '{create.OperationId.Value}' binds wordform '{word.Value}' created by operation " +
                            $"'{creator.Value}' without declaring it in dependsOn; order is never inferred from position.");
        }
    }

    private static HashSet<CanonicalId> Declared(OperationEnvelope operation, IReadOnlyDictionary<CanonicalId, OperationEnvelope> byId)
    {
        var seen = new HashSet<CanonicalId>();
        var pending = new Stack<OperationEnvelope>();
        pending.Push(operation);
        while (pending.Count > 0)
            foreach (var dependency in pending.Pop().DependsOn)
                if (seen.Add(dependency.OperationId) && byId.TryGetValue(dependency.OperationId, out var next))
                    pending.Push(next);
        return seen;
    }
}
