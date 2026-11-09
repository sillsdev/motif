using SIL.LCModel;

namespace SIL.Motif.Runner.Operations;

internal static class RegularRuleActivationGuard
{
    public static void RequireComplete(IPhSegmentRule rule)
    {
        if (rule is IPhMetathesisRule metathesis)
        {
            if (metathesis.StrucDescOS.Count < 2 || string.IsNullOrEmpty(metathesis.StrucChange?.Text) ||
                metathesis.LeftSwitchIndex < 0 || metathesis.RightSwitchIndex < 0)
                throw new InvalidOperationException(
                    "A metathesis rule cannot be enabled until it has a left switch and a right switch, " +
                    "with its structural change positions set.");
            RuleContextSemanticValidator.ValidateRule(rule.Owner as IPhPhonData ??
                throw new InvalidOperationException("A metathesis rule must belong to this project's phonological data."), rule);
            return;
        }

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
