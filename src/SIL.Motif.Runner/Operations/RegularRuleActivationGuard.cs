using SIL.LCModel;

namespace SIL.Motif.Runner.Operations;

internal static class RegularRuleActivationGuard
{
    public static void RequireComplete(IPhSegmentRule rule)
    {
        if (rule is IPhMetathesisRule metathesis)
        {
            RequireMetathesisPositions(metathesis);
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

    private static void RequireMetathesisPositions(IPhMetathesisRule rule)
    {
        const string incomplete = "A metathesis rule cannot be enabled until it has a left switch and a right switch, " +
            "with its structural change positions set.";
        var count = rule.StrucDescOS.Count;
        if (count < 2 || string.IsNullOrWhiteSpace(rule.StrucChange?.Text))
            throw new InvalidOperationException(incomplete);

        int[] indices;
        try
        {
            indices = rule.GetStrucChangeIndices(out _);
        }
        catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or NullReferenceException
            or OverflowException or ArgumentException)
        {
            throw new InvalidOperationException(
                $"A metathesis rule cannot be enabled because its structural change \"{rule.StrucChange.Text}\" is malformed; " +
                "expected five positions: left environment, right switch, middle, left switch, right environment.", ex);
        }

        if (indices.Length != 5)
            throw new InvalidOperationException(
                $"A metathesis rule cannot be enabled because its structural change \"{rule.StrucChange.Text}\" is malformed: it does not have five positions.");
        if (indices[PhMetathesisRuleTags.kidxLeftSwitch] < 0 || indices[PhMetathesisRuleTags.kidxRightSwitch] < 0)
            throw new InvalidOperationException(incomplete);
        foreach (var index in indices)
            if (index >= count)
                throw new InvalidOperationException(
                    $"A metathesis rule cannot be enabled because its structural change \"{rule.StrucChange.Text}\" " +
                    $"refers to position {index}, but the rule has only {count} structural description items.");
    }
}
