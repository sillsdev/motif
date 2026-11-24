using SIL.Motif.Commands.Queries;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>The class list a person copies from: each line leads with the token a pattern takes.</summary>
public sealed class PhonValuesTests
{
    [Theory]
    [InlineData("V", "V")]
    [InlineData("F-V", "F-V")]
    [InlineData("Exact featured V", "[Exact featured V]")]
    [InlineData("A(B)", "[A(B)]")]
    public void ATokenIsBracketedOnlyWhenThePatternParserWouldSplitIt(string text, string token) =>
        Assert.Equal(token, PhonValues.PatternToken(text));

    [Fact]
    public void TheRecordedInventoryListsEveryClassWithItsKindAndTheOneNoSpellingReaches()
    {
        var inventory = ResidentPhonMatcher.ReadInventory(SIL.Motif.Tests.TestFixtures.RecordedMatch.Inventory);
        Assert.Equal(13, inventory.Classes.Count);
        Assert.Equal("[Nasal Stop]", PhonValues.PatternToken(inventory.Classes.Single(c => c.Id == "ncNas").Token!));
        Assert.Null(inventory.Classes.Single(c => c.Id == "no]way").Token);
    }
}
