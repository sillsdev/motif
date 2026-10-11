using System.Text.Json;
using SIL.Motif.Commands.Retirement;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.HumanJudgments;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class RetirementRecipeDeclarationTests
{
    private static readonly RetirementFindingBinding Binding = new("RecordParsimonyDisposition", _ => true);

    private static JsonElement Entry(string composer, string? format) => JsonSerializer.SerializeToElement(new
    {
        composer,
        input = format is null ? (object)new { } : new { format },
    });

    private static DraftDocument Draft(params JsonElement[] entries)
    {
        var draft = new DraftDocument();
        draft.ComposerProvenance.AddRange(entries);
        return draft;
    }

    private static RetirementRecipeDeclaration<string> Declare(string composer, string format) =>
        new("test", composer, format, json => json, Binding);

    [Fact]
    public void ADeclarationBindsOnlyItsOwnComposerAndFormat()
    {
        var draft = Draft(Entry("RetireAllomorph", "motif-allomorph-retirement"),
            Entry("RetireAllomorph", "other-format"), Entry("OtherComposer", "motif-allomorph-retirement"));

        Assert.Single(Declare("RetireAllomorph", "motif-allomorph-retirement").Compositions(draft));
        Assert.Single(Declare("OtherComposer", "motif-allomorph-retirement").Compositions(draft));
        Assert.Empty(Declare("RetireAllomorph", "unrelated").Compositions(draft));
    }

    [Fact]
    public void ARecipeWithoutADeclarationHasNoCompositionToReview()
    {
        var draft = Draft(Entry("RetireRedundantZeroAffix", null));

        Assert.Empty(RetirementRecipes.RuleReplacement.Compositions(draft));
    }

    [Fact]
    public void TheFindingBindingReadsOnlyItsDeclaredComposerEntries()
    {
        var draft = Draft(Entry("RecordParsimonyDisposition", null), Entry("RetireAllomorph", "x"));

        Assert.Single(Binding.Entries(draft));
        Assert.Empty(new RetirementFindingBinding("Nothing", _ => true).Entries(draft));
    }

    [Fact]
    public void RuleReplacementRefusesAnAdhocClusterButAcceptsAFindingOrAnotherGroup()
    {
        var accepts = RetirementRecipes.RuleReplacement.FindingBinding.AcceptsSubject;

        Assert.True(accepts(new ParsimonyFindingJudgmentSubject("m", "f")));
        Assert.True(accepts(new GroupJudgmentSubject("m", JudgmentGroupRole.DuplicatePair, [])));
        Assert.False(accepts(new GroupJudgmentSubject("m", JudgmentGroupRole.AdhocCluster, [])));
        Assert.False(accepts(new ProjectJudgmentSubject("p")));
    }
}
