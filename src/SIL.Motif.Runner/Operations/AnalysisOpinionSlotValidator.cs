using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Operations;

/// <summary>Enforces one default-user opinion slot per analysis in a Proposal.</summary>
public static class AnalysisOpinionSlotValidator
{
    public static void Validate(Proposal proposal)
    {
        var seen = new HashSet<CanonicalId>();
        foreach (var operation in proposal.Operations)
        {
            if (operation.Kind != WfiAnalysisOperationKinds.AddRefEvaluations &&
                operation.Kind != WfiAnalysisOperationKinds.RemoveRefEvaluations) continue;
            if (operation.Target is not { } target) continue;
            if (!seen.Add(target))
                throw new ContractParseException(
                    $"Analysis {target.Value} has more than one default-user opinion operation. " +
                    "A Proposal may decide that opinion only once.");
        }
    }
}
