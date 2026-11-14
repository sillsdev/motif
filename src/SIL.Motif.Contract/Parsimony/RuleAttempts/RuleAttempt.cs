namespace SIL.Motif.Contract.Parsimony.RuleAttempts;

/// <summary>The recorded evidence that a rule was tried for one shape of added object and did not hold.</summary>
/// <param name="ShapeKey">The <see cref="AddedObjectShape.Key"/> the attempt answers.</param>
/// <param name="Rule">The rule that was tried, in the words a linguist would use.</param>
/// <param name="PredictionTestResult">The result of the prediction test, such as hits against control letters.</param>
/// <param name="WhyItFailed">Why the rule was rejected, for example attested losses or doubled readings.</param>
public sealed record RuleAttempt(string ShapeKey, string Rule, string PredictionTestResult, string WhyItFailed)
{
    /// <summary>Whether all three narrative fields say something; a blank field is not an attempt.</summary>
    public bool IsComplete => !string.IsNullOrWhiteSpace(Rule) && !string.IsNullOrWhiteSpace(PredictionTestResult) &&
        !string.IsNullOrWhiteSpace(WhyItFailed);
}

/// <summary>
/// Where rule attempts are read from. The hypothesis ledger backs it (ADR 0117); only the CLI writes the ledger, so a
/// review or a check depends on this read-only lookup.
/// </summary>
public interface IRuleAttemptLedger
{
    /// <summary>Finds the attempt recorded for a shape, passed or failed, or null.</summary>
    /// <param name="shapeKey">The shape key.</param>
    RuleAttempt? Find(string shapeKey);
}
