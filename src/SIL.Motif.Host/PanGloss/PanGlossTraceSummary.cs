namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// A one-line triage summary derived from a verbatim <see cref="PanGlossTraceNode"/> tree: what a
/// <c>grep</c> lands on, never the source of truth the tree itself is.
/// </summary>
/// <param name="Signature">The parser's own parity-line signature: the sorted, joined analysis signature, or
/// <c>-</c> when the word produced none.</param>
/// <param name="StepCount">The number of trace nodes PanGloss recorded — a size measure of the derivation,
/// not the same counter PanGloss's own step budget advances internally.</param>
/// <param name="Completed">Whether the tree is the whole derivation PanGloss walked, as opposed to whatever
/// was collected before Motif's own trace timeout or PanGloss's own cap cut it short.</param>
/// <param name="FailureReasons">Every distinct reason a node in the tree did not apply or did not succeed,
/// in a fixed order so two runs over the same tree compare equal.</param>
/// <param name="DeepestRuleReached">The named rule at the greatest depth the derivation reached, or
/// <see langword="null"/> when no rule node fired at all.</param>
public sealed record PanGlossTraceSummary(
    string Signature,
    int StepCount,
    bool Completed,
    IReadOnlyList<string> FailureReasons,
    string? DeepestRuleReached)
{
    private static readonly HashSet<string> RuleNodeTypes = new(StringComparer.Ordinal)
    {
        "MorphologicalRuleAnalysis", "MorphologicalRuleSynthesis",
        "PhonologicalRuleAnalysis", "PhonologicalRuleSynthesis",
        "CompoundingRuleAnalysis", "CompoundingRuleSynthesis",
    };

    /// <summary>
    /// Walks <paramref name="root"/> once: every node is one step, every node's failure reason (if any) joins
    /// the distinct set, and the deepest rule node's own name wins — first in derivation order on a tie,
    /// since PanGloss already orders children in the order it tried them.
    /// </summary>
    internal static PanGlossTraceSummary Derive(string signature, PanGlossTraceNode? root, bool completed)
    {
        if (root is null) return new PanGlossTraceSummary(signature, 0, completed, [], null);

        var stepCount = 0;
        var reasons = new SortedSet<string>(StringComparer.Ordinal);
        var deepestDepth = -1;
        string? deepestRule = null;
        Walk(root, 0);
        return new PanGlossTraceSummary(signature, stepCount, completed, [.. reasons], deepestRule);

        void Walk(PanGlossTraceNode node, int depth)
        {
            stepCount++;
            if (node.FailureReason is { } reason) reasons.Add(reason);
            if (depth > deepestDepth && node.Source is { } source && RuleNodeTypes.Contains(node.Type))
            {
                deepestDepth = depth;
                deepestRule = source;
            }
            foreach (var child in node.Children) Walk(child, depth + 1);
        }
    }
}
