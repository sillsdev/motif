using SIL.LCModel;

namespace SIL.Motif.Runner.Operations;

internal static class RegularRuleActivationGuard
{
    public static void RequireComplete(IPhSegmentRule rule)
    {
        if (rule is not IPhRegularRule regularRule)
            return;

        if (regularRule.RightHandSidesOS.Count == 0)
        {
            throw new InvalidOperationException(
                "A regular rule cannot be enabled until it has a right-hand side; its left and right contexts are optional.");
        }

        RuleContextSemanticValidator.ValidateRule(rule.Owner as IPhPhonData ??
            throw new InvalidOperationException("A regular rule must belong to this project's phonological data."), rule);
    }
}
