using SIL.Motif.Contract.Review;

namespace SIL.Motif.Worker.Runtime.Review;

internal static class DeclaredTrialGateEvaluation
{
    internal static string? Reason(FrozenReviewInputs inputs, ReviewSummary summary, bool incomplete,
        TrialReviewParsimonySnapshot? parsimony)
    {
        if (inputs.DeclaredGate is not { } declaration) return null;
        if (declaration.WordSets.Any(pin => !inputs.Sets.Any(set => set.Id == pin.Id &&
                set.SourceDigest == pin.SourceDigest && set.MembershipDigest == pin.MembershipDigest &&
                set.ExpectationDigest == pin.ExpectationDigest) ||
            !inputs.Cases.Any(item => item.Memberships.Any(member => member.SetId == pin.Id))))
            return "A declared gate word set was omitted or has no captured cases.";
        if (incomplete) return "The declared gate word sets were not fully measured on both sides.";
        if (summary.Gains?.Gate.Hold == true) return "The project's declared gain gate holds this Trial.";
        if (summary.RuleAttempts?.Hold == true) return "The opted-in Parsimony rule-attempt policy holds this Trial.";
        foreach (var id in (inputs.ParsimonyBlockOn ?? []).Where(id => id != "P-variant-same-shape"))
        {
            var measure = parsimony?.Findings.Measures.SingleOrDefault(item => item.MeasureId == id);
            if (measure is null || measure.Limitation is not null)
                return "The opted-in Parsimony measure " + id + " was not measured.";
            if (parsimony!.Rows.Any(item => item.Row is FindingRow finding && finding.MeasureId == id &&
                    finding.Change != FindingChange.Removed))
                return "The opted-in Parsimony measure " + id + " reports a wrong reading.";
        }
        return null;
    }
}
