using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Parsimony.RuleAttempts;

namespace SIL.Motif.Contract.Review;

/// <summary>One group of same-shape added objects that reaches the rule-attempt threshold.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RuleAttemptGroupRow(
    [property: JsonRequired] string ShapeKey,
    [property: JsonRequired] int Count,
    [property: JsonRequired] IReadOnlyList<string> SampleObjectIds,
    [property: JsonRequired] bool AttemptRecorded) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (string.IsNullOrEmpty(ShapeKey) || Count < 0 || SampleObjectIds is null || SampleObjectIds.Any(id => id is null))
            throw new JsonException("Invalid RuleAttemptGroupRow.");
    }
}

/// <summary>
/// Whether the Proposal adds five or more same-shape variants without a recorded rule attempt (ADR 0116). Advisory;
/// <see cref="Hold"/> is true only when the project opted <c>P-variant-same-shape</c> in to blocking, and like the gain
/// gate it is report only: Apply does not read it.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RuleAttemptSummary(
    [property: JsonRequired] int Threshold,
    [property: JsonRequired] int ShapesEvaluated,
    [property: JsonRequired] IReadOnlyList<RuleAttemptGroupRow> Groups,
    [property: JsonRequired] bool OptedIn,
    [property: JsonRequired] bool Hold) : IJsonOnDeserialized, IJsonOnSerializing
{
    /// <summary>Evaluates the shapes against the ledger; cost is O(added objects) by hashing.</summary>
    public static RuleAttemptSummary Create(IReadOnlyList<AddedObjectShape> shapes, IRuleAttemptLedger ledger,
        IReadOnlyList<string> optedIn)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(optedIn);
        var groups = SameShapeGroups.Find(shapes);
        var rows = groups.Select(group => new RuleAttemptGroupRow(group.ShapeKey, group.Count,
            [.. group.ObjectIds.Take(5)], ledger.Find(group.ShapeKey) is { IsComplete: true })).ToArray();
        var policy = new BlockingPolicy(optedIn);
        var fired = RuleAttemptCheck.Evaluate(shapes, ledger).Where(finding => finding.Hold).Select(finding => (BlockingPolicy.RuleAttemptMeasureId, false));
        return new RuleAttemptSummary(SameShapeGroups.RuleAttemptThreshold, shapes.Count, rows,
            optedIn.Contains(BlockingPolicy.RuleAttemptMeasureId), policy.Decide(true, fired).Blocked);
    }

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Threshold < 1 || ShapesEvaluated < 0 || Groups is null || Groups.Any(row => row is null))
            throw new JsonException("Invalid RuleAttemptSummary.");
    }
}
