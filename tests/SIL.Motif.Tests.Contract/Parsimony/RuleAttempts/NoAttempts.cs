using SIL.Motif.Contract.Parsimony.RuleAttempts;

namespace SIL.Motif.Tests.Contract.Parsimony.RuleAttempts;

/// <summary>A ledger with no recorded attempts, for tests of the hold.</summary>
internal sealed class NoAttempts : IRuleAttemptLedger
{
    public static NoAttempts Instance { get; } = new();

    public RuleAttempt? Find(string shapeKey) => null;
}
