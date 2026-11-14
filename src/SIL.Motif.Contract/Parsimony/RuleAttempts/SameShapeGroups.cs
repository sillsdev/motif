namespace SIL.Motif.Contract.Parsimony.RuleAttempts;

/// <summary>One variant a package adds, reduced to what decides its shape.</summary>
/// <param name="ObjectId">The id the package gives the added object, or the variant entry id; used to name members.</param>
/// <param name="VariantTypeId">The variant type.</param>
/// <param name="EnvironmentIds">The environments the variant is conditioned by.</param>
/// <param name="FeatureIds">The features it carries.</param>
/// <param name="Correspondence">How its form differs from its main form.</param>
public sealed record AddedObjectShape(string ObjectId, string VariantTypeId, IReadOnlyList<string> EnvironmentIds,
    IReadOnlyList<string> FeatureIds, StringCorrespondence Correspondence)
{
    /// <summary>The same-shape key: variant type, environments, features and string correspondence.</summary>
    public string Key =>
        $"variant|{VariantTypeId}|env:{string.Join(',', EnvironmentIds.Order(StringComparer.Ordinal))}" +
        $"|feat:{string.Join(',', FeatureIds.Order(StringComparer.Ordinal))}|{Correspondence.Key}";
}

/// <summary>Objects of one shape added by one package.</summary>
/// <param name="ShapeKey">The shared shape key.</param>
/// <param name="ObjectIds">The members, in package order.</param>
public sealed record SameShapeGroup(string ShapeKey, IReadOnlyList<string> ObjectIds)
{
    /// <summary>The number of members.</summary>
    public int Count => ObjectIds.Count;
}

/// <summary>Groups added objects by shape and finds the groups big enough to need a rule attempt.</summary>
public static class SameShapeGroups
{
    /// <summary>The group size at which a rule attempt becomes required.</summary>
    public const int RuleAttemptThreshold = 5;

    /// <summary>All groups of <paramref name="threshold"/> or more added objects sharing one shape, largest first.</summary>
    /// <param name="added">The objects a package adds.</param>
    /// <param name="threshold">The smallest group size reported.</param>
    public static IReadOnlyList<SameShapeGroup> Find(IEnumerable<AddedObjectShape> added,
        int threshold = RuleAttemptThreshold)
    {
        ArgumentNullException.ThrowIfNull(added);
        return [.. added.GroupBy(shape => shape.Key, StringComparer.Ordinal)
            .Where(group => group.Count() >= threshold)
            .Select(group => new SameShapeGroup(group.Key, [.. group.Select(shape => shape.ObjectId)]))
            .OrderByDescending(group => group.Count).ThenBy(group => group.ShapeKey, StringComparer.Ordinal)];
    }
}

/// <summary>A same-shape group and the rule attempt, if any, that answers it.</summary>
/// <param name="Group">The group.</param>
/// <param name="Attempt">The complete recorded attempt, or null when none is on record.</param>
public sealed record RuleAttemptFinding(SameShapeGroup Group, RuleAttempt? Attempt)
{
    /// <summary>True when the group has no complete rule attempt, so a reviewer holds the package.</summary>
    public bool Hold => Attempt is null;
}

/// <summary>Checks a package's added objects against the rule-attempt requirement.</summary>
public static class RuleAttemptCheck
{
    /// <summary>One finding per large same-shape group; a finding holds unless a complete attempt is on record.</summary>
    /// <param name="added">The objects the package adds.</param>
    /// <param name="ledger">Where rule attempts are kept.</param>
    public static IReadOnlyList<RuleAttemptFinding> Evaluate(IEnumerable<AddedObjectShape> added,
        IRuleAttemptLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        return [.. SameShapeGroups.Find(added).Select(group =>
        {
            var attempt = ledger.Find(group.ShapeKey);
            return new RuleAttemptFinding(group, attempt is { IsComplete: true } ? attempt : null);
        })];
    }
}
