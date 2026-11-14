namespace SIL.Motif.Contract.Parsimony;

/// <summary>Whether a Proposal is held, and by which measures.</summary>
/// <param name="Blocked">True when at least one opted-in measure blocks.</param>
/// <param name="BlockingMeasureIds">The measures that block.</param>
public sealed record BlockDecision(bool Blocked, IReadOnlyList<string> BlockingMeasureIds);

/// <summary>
/// Which Parsimony measures may block a Proposal. Every measure is advisory unless the project opts a
/// block-capable one in, and only autonomous mode ever blocks: a linguist's window stays advisory.
/// </summary>
public sealed class BlockingPolicy
{
    /// <summary>The only measures that may block: two that measure a wrong reading, and the rule-attempt check.</summary>
    public static IReadOnlyList<string> BlockCapable { get; } =
        ["R-word-disapproved-produced", "R-word-negative-accepted", RuleAttemptMeasureId];

    /// <summary>The measure id the same-shape rule-attempt requirement reports under.</summary>
    public const string RuleAttemptMeasureId = "P-variant-same-shape";

    private readonly HashSet<string> _optedIn;

    /// <summary>Creates a policy.</summary>
    /// <param name="optedIn">Measure ids the project chose to block on; ids that cannot block are ignored.</param>
    public BlockingPolicy(IEnumerable<string>? optedIn = null) =>
        _optedIn = [.. (optedIn ?? []).Where(id => BlockCapable.Contains(id))];

    /// <summary>Advisory everywhere: nothing opted in.</summary>
    public static BlockingPolicy Advisory { get; } = new();

    /// <summary>Decides from the measures that fired. A wrong-reading measure blocks only with a measured wrong reading.</summary>
    /// <param name="autonomous">Whether the work is in autonomous mode.</param>
    /// <param name="fired">Each fired measure id and whether a Trial measured a wrong reading for it.</param>
    public BlockDecision Decide(bool autonomous, IEnumerable<(string MeasureId, bool MeasuredWrongReading)> fired)
    {
        if (!autonomous) return new BlockDecision(false, []);
        var blocking = fired
            .Where(f => _optedIn.Contains(f.MeasureId) && (f.MeasureId == RuleAttemptMeasureId || f.MeasuredWrongReading))
            .Select(f => f.MeasureId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new BlockDecision(blocking.Length > 0, blocking);
    }
}
