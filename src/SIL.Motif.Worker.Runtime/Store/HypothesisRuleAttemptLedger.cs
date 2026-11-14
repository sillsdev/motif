using SIL.Motif.Contract.Parsimony.RuleAttempts;

namespace SIL.Motif.Worker.Store;

/// <summary>
/// The hypothesis ledger read as rule attempts (ADR 0116, 0117). A hypothesis answers a same-shape group when it passed or
/// failed, which needs a recorded test, and its change text is the group's full shape key or its string correspondence
/// (such as <c>V&gt;@final</c>) and its environment is blank or the group's environment ids. Either verdict clears the hold;
/// a proposed or superseded hypothesis does not. One seek on the shape index per group: O(log n + k).
/// </summary>
public sealed class HypothesisRuleAttemptLedger(HypothesisRepository ledger) : IRuleAttemptLedger
{
    private readonly HypothesisRepository _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));

    /// <inheritdoc />
    public RuleAttempt? Find(string shapeKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(shapeKey);
        var parts = shapeKey.Split('|');
        var correspondence = parts[^1];
        var environments = parts.Length >= 3 && parts[2].StartsWith("env:", StringComparison.Ordinal) ? parts[2][4..] : "";
        var candidates = _ledger.Neighbours(shapeKey, null, null, null)
            .Concat(_ledger.Neighbours(correspondence, null, null, null).Where(h =>
                string.IsNullOrWhiteSpace(h.Environment) ||
                string.Equals(h.Environment.Trim(), environments, StringComparison.OrdinalIgnoreCase)));
        var answer = candidates.Where(h => h.Status is HypothesisStatuses.Passed or HypothesisStatuses.Failed)
            .OrderByDescending(h => h.Seq).FirstOrDefault();
        if (answer is null) return null;
        var test = _ledger.Tests(answer.HypothesisId) is { Count: > 0 } tests ? tests[^1] : null;
        var result = test is null ? answer.Status
            : $"{test.Verdict}: {test.RuleHits} rule hits, margin {test.Margin} over the best control";
        return new RuleAttempt(shapeKey, $"{answer.HypothesisId}: {answer.Statement}", result,
            string.IsNullOrWhiteSpace(answer.Decision) ? $"Ledger status {answer.Status}." : answer.Decision!);
    }
}
