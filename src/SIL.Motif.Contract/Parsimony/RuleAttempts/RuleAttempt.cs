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
/// Where rule attempts are kept. The hypothesis ledger will back this; until then
/// <see cref="NoRuleAttemptLedger"/> holds none, so every large same-shape group is held.
/// </summary>
public interface IRuleAttemptLedger
{
    /// <summary>Records an attempt, replacing an earlier one for the same shape.</summary>
    /// <param name="attempt">The attempt to keep.</param>
    void Record(RuleAttempt attempt);

    /// <summary>Finds the attempt recorded for a shape, or null.</summary>
    /// <param name="shapeKey">The shape key.</param>
    RuleAttempt? Find(string shapeKey);
}

/// <summary>The stand-in used while no hypothesis ledger exists: it finds nothing and records nothing.</summary>
public sealed class NoRuleAttemptLedger : IRuleAttemptLedger
{
    /// <summary>The shared instance.</summary>
    public static NoRuleAttemptLedger Instance { get; } = new();

    /// <inheritdoc />
    public void Record(RuleAttempt attempt) =>
        throw new NotSupportedException("No hypothesis ledger is attached, so a rule attempt cannot be recorded.");

    /// <inheritdoc />
    public RuleAttempt? Find(string shapeKey) => null;
}
