using System;
using System.Collections.Generic;
using System.Linq;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Operations;

/// <summary>Orders one Proposal's operations by their declared predecessor relationships.</summary>
public static class OperationExecutionOrder
{
    /// <summary>
    /// Returns a stable topological ordering; independent operations retain their relative input order.
    /// </summary>
    /// <param name="operations">Operations whose <see cref="OperationEnvelope.DependsOn"/> edges are honored.</param>
    /// <exception cref="ContractParseException">An operation identity is duplicated or an edge is invalid or cyclic.</exception>
    public static IReadOnlyList<OperationEnvelope> Sort(IReadOnlyList<OperationEnvelope> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        var positions = new Dictionary<CanonicalId, int>();
        for (var index = 0; index < operations.Count; index++)
            if (!positions.TryAdd(operations[index].OperationId, index))
                throw new ContractParseException(
                    $"Duplicate operationId '{operations[index].OperationId.Value}' within a Proposal.");

        var dependents = Enumerable.Range(0, operations.Count).Select(_ => new List<int>()).ToArray();
        var remainingPrerequisites = new int[operations.Count];
        for (var index = 0; index < operations.Count; index++)
        {
            foreach (var prerequisite in operations[index].DependsOn.Select(item => item.OperationId).Distinct())
            {
                if (!positions.TryGetValue(prerequisite, out var prerequisiteIndex))
                    throw new ContractParseException(
                        $"Operation '{operations[index].OperationId.Value}' depends on absent operation '{prerequisite.Value}'.");
                if (prerequisiteIndex == index)
                    throw new ContractParseException(
                        $"Operation '{operations[index].OperationId.Value}' cannot depend on itself.");
                dependents[prerequisiteIndex].Add(index);
                remainingPrerequisites[index]++;
            }
        }

        var ready = new SortedSet<int>(Enumerable.Range(0, operations.Count)
            .Where(index => remainingPrerequisites[index] == 0));
        var ordered = new List<OperationEnvelope>(operations.Count);
        while (ready.Count > 0)
        {
            var next = ready.Min;
            ready.Remove(next);
            ordered.Add(operations[next]);
            foreach (var dependent in dependents[next])
                if (--remainingPrerequisites[dependent] == 0) ready.Add(dependent);
        }

        if (ordered.Count != operations.Count)
            throw new ContractParseException("Operation dependsOn relationships contain a cycle.");
        return ordered;
    }
}
