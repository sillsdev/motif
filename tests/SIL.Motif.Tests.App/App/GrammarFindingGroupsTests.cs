using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins how the Grammar stage groups findings: load warnings by the shape of the parser's own sentence, using
/// lines copied from a real project's load, health findings by the name the parser gives them, and the split
/// between what the parser left out of the grammar and what is only worth a look.
/// </summary>
public sealed class GrammarFindingGroupsTests
{
    [Theory]
    [InlineData("warning: phoneme \"dbd6d116-b210-4dc0-9a0b-1d664751ca27\": representation collides with an earlier phoneme/boundary; skipped",
        "Representation collides with an earlier phoneme/boundary")]
    [InlineData("warning: invalid environment \"e1\" (/ _ [C]): unknown natural class \"C\"; treated as absent",
        "Invalid environment: unknown natural class")]
    [InlineData("warning: invalid environment \"e2\" (/[V+mid] ([preNas]) [C-nas] ([Mod]) _): unknown natural class \"Mod\"; treated as absent",
        "Invalid environment: unknown natural class")]
    [InlineData("warning: allomorph \"a1\": cannot segment \"kat\": cannot segment \"kat\": no character definition matches at position 0; skipped",
        "Cannot segment: no character definition matches at position")]
    [InlineData("warning: lex entry \"c1\" sense \"s1\": msa \"m1\" does not resolve within this entry",
        "Msa … does not resolve within this entry")]
    [InlineData("warning: MSA has zero loadable allomorphs for this stratum bucket",
        "MSA has zero loadable allomorphs for this stratum bucket")]
    [InlineData("warning: hc-partial-morpheme: Morphological rule 'meN' is partially analyzed. Supply its missing category.",
        "Morphological rule … is partially analyzed")]
    [InlineData("warning: circumfix entry \"ke- -an\": found 0 prefix half/halves and 0 suffix half/halves; a circumfix needs one",
        "Found … prefix half/halves and … suffix half/halves")]
    [InlineData("warning: morphology.adhocProhibitions: ad-hoc prohibition aea110aa-595a-40a7-bb62-d9bf95280bb4 references " +
        "inflectional affix 8497c72c-0000-9f69-0000-564cb0a2938f, whose slot is not in any template. Remove it.",
        "Ad-hoc prohibition … references inflectional affix …, whose slot is not in any template")]
    [InlineData("warning: cannot segment \"sábadu\": the failure position 2 remaps to 'b', which is already a registered character; " +
        "the true failing element is likely a standalone combining mark",
        "Cannot segment: the failure position … remaps to …, which is already a registered character")]
    [InlineData("warning: cannot segment \"mynoun2\": '2' at position 6 is neither a vernacular exemplar, an authored boundary, " +
        "nor in the safe boundary table; refusing rather than guessing",
        "Cannot segment: … at position … is neither a vernacular exemplar, an authored boundary, nor in the safe boundary table")]
    public void AWarningIsLabelledByTheParsersOwnWordsWithItsIdentifiersTakenOut(string line, string label)
    {
        Assert.Equal(label, GrammarFindingShapes.LabelOf(line));
    }

    [Theory]
    [InlineData("warning: phoneme \"p\": representation collides with an earlier phoneme/boundary; skipped", true)]
    [InlineData("warning: invalid environment \"e\" (/ _ [C]): unknown natural class \"C\"; treated as absent", true)]
    [InlineData("warning: MSA has zero loadable allomorphs for this stratum bucket", true)]
    [InlineData("warning: inferred segment \"x\" carries no authored feature values, so it satisfies every class", false)]
    public void ASentenceSayingSomethingWasLeftOutPutsTheFindingAmongThoseToFixFirst(string line, bool leftOut)
    {
        Assert.Equal(leftOut, GrammarFindingShapes.IsLeftOut(line));
    }

    [Fact]
    public void FindingsAreGroupedByKind_LargestFirst_AndChoosingAKindFiltersTheTable()
    {
        static GrammarWarning Load(string line) => new("warning", string.Empty, [], [new GrammarWarningPart(line, "text")], line);
        var findings = new List<GrammarWarning>
        {
            Load("warning: invalid environment \"a\" (/ _ [C]): unknown natural class \"C\"; treated as absent"),
            Load("warning: invalid environment \"b\" (/_[Nas]): unknown natural class \"Nas\"; treated as absent"),
            Load("warning: phoneme \"p\": representation collides with an earlier phoneme/boundary; skipped"),
            new("warning", "Partial morpheme", [], [new GrammarWarningPart("Entry is partially analyzed.", "text")],
                "warning: hc-partial-morpheme: Entry is partially analyzed.")
            {
                Group = "Partial morpheme analysis",
                Code = "hc-partial-morpheme",
            },
        };
        var warnings = new GrammarWarningsViewModel();

        warnings.Load(findings);

        Assert.Equal(["Invalid environment: unknown natural class", "Representation collides with an earlier phoneme/boundary"],
            warnings.LeftOutGroups.Select(group => group.Name));
        Assert.Equal([2, 1], warnings.LeftOutGroups.Select(group => group.Count));
        Assert.Equal("Partial morpheme analysis", Assert.Single(warnings.WorthALookGroups).Name);
        Assert.Equal(3, warnings.LeftOutCount);
        Assert.Equal(1, warnings.WorthALookCount);

        warnings.SelectGroupCommand.Execute(warnings.LeftOutGroups[0]);
        Assert.Equal(2, warnings.ShownCount);
        Assert.Equal("Invalid environment: unknown natural class", warnings.SelectedGroupTitle);
        Assert.True(warnings.LeftOutGroups[0].IsSelected);

        // A kind outside the chosen bucket is let go, so the table never shows an empty filter.
        warnings.SetBucketCommand.Execute(GrammarFindingBucket.WorthALook);
        Assert.Null(warnings.SelectedGroup);
        Assert.Equal(1, warnings.ShownCount);
        Assert.False(warnings.HasLeftOutGroups);

        warnings.SetBucketCommand.Execute(GrammarFindingBucket.All);
        Assert.Equal(4, warnings.ShownCount);
    }
}
