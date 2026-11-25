using SIL.Motif.Commands.Queries;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Commands.RecordedMorphemes;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// The planner and the generated tier's completeness claims. The lexicon is two stems; the match answers replay a recorded reply
/// for a word that crosses the junction, so the planner sees every candidate as triggering the rule.
/// </summary>
public sealed class RealizesPlannerTests
{
    private static readonly PlannerCandidate[] Lexicon =
    [
        new("e1", "m1", "MoStemMsa", "da", "stem", 10),
        new("e2", "m2", "MoStemMsa", "ka", "stem", 5),
    ];

    private static ReplayGrammar Grammar() => new()
    {
        OnEffects = _ => RecordedRealizes.FillForms(RecordedRealizes.PrefixTa, "ta+", "ta+"),
        OnMatch = (_, words) => RecordedRealizes.FillWords(RecordedRealizes.DataVoicing, words[0]),
        OnGenerate = _ => RecordedRealizes.DaWithTa,
    };

    private static EffectsResult Ta() => EffectsResult.Read(RecordedRealizes.FillForms(RecordedRealizes.PrefixTa, "ta+", "ta+"));

    [Fact]
    public void ACellThatHasARootButNoRoomUnderTheContextCapIsNamedAsCapped()
    {
        var effects = Ta();
        var plan = RealizesPlanner.Plan(Grammar(), [.. effects.Survivors], effects.Allomorphs, false, "start", Lexicon, 2, 1, CancellationToken.None);

        Assert.Equal(["control"], plan.Contexts.Select(context => context.Why));
        Assert.Single(plan.Capped);
        Assert.Empty(plan.Uncovered);
    }

    [Fact]
    public void ACellWithRoomGetsItsRootAndIsNotCapped()
    {
        var effects = Ta();
        var plan = RealizesPlanner.Plan(Grammar(), [.. effects.Survivors], effects.Allomorphs, false, "start", Lexicon, 2, 8, CancellationToken.None);

        Assert.Contains("cell", plan.Contexts.Select(context => context.Why));
        Assert.Empty(plan.Capped);
        Assert.Single(plan.Ignored);
    }

    [Fact]
    public void AnAffixIsNeverClaimedCompleteBecauseTheTemplateIsNotWalkedAndTheOtherEdgeIsNotTested()
    {
        var target = new RealizesTarget(TaEntry, TaMsa, null);
        var variants = RealizesGenerated.Collect(Grammar(), target, Lexicon, [], false, CancellationToken.None);

        var codes = variants.Reasons.Select(reason => reason.Code).ToList();
        Assert.Contains("template_not_walked", codes);
        Assert.Contains("edge_not_tested", codes);
        Assert.False(variants.Complete);
    }

    [Fact]
    public void AnAllomorphNoPlannedContextProducedIsNamed()
    {
        var grammar = Grammar();
        var target = new RealizesTarget(KatEntry, KatMsa, null);
        var kat = new ReplayGrammar
        {
            OnEffects = _ => RecordedRealizes.FillForms(RecordedRealizes.KatRoot, "kat"),
            OnMatch = grammar.OnMatch,
            OnGenerate = _ => RecordedRealizes.KatAlone,
        };
        var variants = RealizesGenerated.Collect(kat, target, Lexicon, [], false, CancellationToken.None);

        Assert.Contains("allomorph_not_generated", variants.Reasons.Select(reason => reason.Code));
    }
}
