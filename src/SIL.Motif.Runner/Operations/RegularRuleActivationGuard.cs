using SIL.LCModel;

namespace SIL.Motif.Runner.Operations;

internal static class RegularRuleActivationGuard
{
    public static void RequireComplete(IPhSegmentRule rule)
    {
        if (rule is not IPhRegularRule regularRule)
            return;

        if (regularRule.RightHandSidesOS.Count == 0 || regularRule.RightHandSidesOS.Any(rhs =>
                rhs.LeftContextOA is null || rhs.RightContextOA is null))
        {
            throw new InvalidOperationException(
                "A regular rule cannot be enabled until it has a right-hand side with left and right contexts.");
        }

        RuleContextSemanticValidator.ValidateRule(rule.Owner as IPhPhonData ??
            throw new InvalidOperationException("A regular rule must belong to this project's phonological data."), rule);
    }
}
