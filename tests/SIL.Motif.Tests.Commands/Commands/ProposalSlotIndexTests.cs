using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Execution;
using SIL.Motif.Runner.Operations;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class ProposalSlotIndexTests
{
    /// <summary>Checks each added operation against every earlier one, pair by pair.</summary>
    private static (string Existing, string Duplicate)? Pairwise(
        IReadOnlyList<OperationEnvelope> retained, IReadOnlyList<OperationEnvelope> added)
    {
        for (var index = 0; index < added.Count; index++)
            foreach (var other in retained.Concat(added.Take(index)))
                if (ProposalOperationSlotValidator.FindConflict(new[] { other, added[index] }) is { } collision)
                    return (collision.Existing.OperationId.Value, collision.Duplicate.OperationId.Value);
        return null;
    }

    private static (string Existing, string Duplicate)? Incremental(
        IReadOnlyList<OperationEnvelope> retained, IReadOnlyList<OperationEnvelope> added)
    {
        try
        {
            ProposalOperationSlotValidator.ValidateAppend(retained, added);
            return null;
        }
        catch (ConflictRefusal refusal)
        {
            return (refusal.Facts["existingOperationId"], refusal.Facts["duplicateOperationId"]);
        }
    }

    private static OperationEnvelope Gloss(CanonicalId sense, string ws) => new(CanonicalId.Mint(),
        LexicalSenseOperationKinds.SetGloss, target: sense,
        after: JsonSerializer.SerializeToElement(new { ws, text = "t" }));

    private static OperationEnvelope Delete(CanonicalId analysis) => new(CanonicalId.Mint(),
        WfiAnalysisOperationKinds.DeleteAnalysis, target: analysis);

    private static OperationEnvelope Retarget(CanonicalId bundle) => new(CanonicalId.Mint(),
        AllomorphRetargetOperationKinds.SetBundleMorph, target: bundle);

    private static OperationEnvelope CreateWordform(CanonicalId root, CanonicalId entity) => new(CanonicalId.Mint(),
        LangProjectWordformsOperationKinds.Create, entityId: entity, target: root,
        after: JsonSerializer.SerializeToElement(new { ws = "en", text = "w" }));

    [Fact]
    public void AMixedDraftReportsTheSameConflictingPairAsPairwiseChecking()
    {
        // A small shared pool makes one target carry a gloss, a deletion and a retarget in the same draft.
        var targets = Enumerable.Range(0, 4).Select(_ => CanonicalId.Mint()).ToArray();
        var roots = Enumerable.Range(0, 2).Select(_ => CanonicalId.Mint()).ToArray();
        var entities = Enumerable.Range(0, 3).Select(_ => CanonicalId.Mint()).ToArray();
        var random = new Random(20261020);
        CanonicalId Target() => targets[random.Next(targets.Length)];
        OperationEnvelope Next() => random.Next(6) switch
        {
            0 => Delete(Target()),
            1 => Retarget(Target()),
            2 => CreateWordform(roots[random.Next(roots.Length)], entities[random.Next(entities.Length)]),
            _ => Gloss(Target(), random.Next(2) == 0 ? "en" : "fr"),
        };

        var conflicts = 0;
        const int trials = 400;
        for (var trial = 0; trial < trials; trial++)
        {
            // The retained part may itself conflict: an append that fixes a Draft must still be accepted.
            var retained = Enumerable.Range(0, random.Next(0, 5)).Select(_ => Next()).ToArray();
            var added = Enumerable.Range(0, random.Next(1, 4)).Select(_ => Next()).ToArray();
            var expected = Pairwise(retained, added);
            Assert.Equal(expected, Incremental(retained, added));
            if (expected is not null) conflicts++;
        }
        Assert.True(conflicts > trials / 5, $"Only {conflicts} of {trials} drafts conflicted.");
        Assert.True(conflicts < trials * 9 / 10, $"{conflicts} of {trials} drafts conflicted; too few clean ones.");
    }

    [Fact]
    public void AnIndexKeptAcrossAppendsTreatsEarlierAdditionsAsRetained()
    {
        var sense = CanonicalId.Mint();
        var first = Gloss(sense, "en");
        var index = new ProposalSlotIndex([]);
        index.Append([first]);
        index.Append([Gloss(sense, "fr")]);

        var duplicate = Gloss(sense, "en");
        var refusal = Assert.Throws<ConflictRefusal>(() => index.Append([duplicate]));
        Assert.Equal(first.OperationId.Value, refusal.Facts["existingOperationId"]);
        Assert.Equal(duplicate.OperationId.Value, refusal.Facts["duplicateOperationId"]);
    }
}
