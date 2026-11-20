using SIL.Motif.Contract.Dispositions;
using SIL.Motif.Worker.Corpus.Ledger;
using Xunit;
using static SIL.Motif.Tests.Worker.Corpus.LedgerFixtures;

namespace SIL.Motif.Tests.Worker.Corpus;

/// <summary>A disposition's word type key is the key Coverage joins a ledger to the frequency list on.</summary>
public sealed class WordTypeKeyTests
{
    public static TheoryData<string> Fixtures => new()
    {
        "Nairobi", "nairobi", "\u00e9", "e\u0301", "\u00c9a", "E\u0301a", "\u1e6d\u0323a", "\ud55c\uae00", "\u1112\u1161\u11ab\u1100\u1173\u11af", "\u0131", "\u01c6", "Kiswahili-cha", "x1", "",
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void TheKeyIsTheKeyCoverageJoinsOn(string word) =>
        Assert.Equal(LedgerJoiner.Key(word), WordTypeKey.Of(word));

    [Fact]
    public void ComposedAndDecomposedSpellingsAreOneTypeAndCaseIsNotFolded()
    {
        Assert.Equal(WordTypeKey.Of("\u00e9"), WordTypeKey.Of("e\u0301"));
        Assert.Equal(WordTypeKey.Of("\ud55c\uae00"), WordTypeKey.Of("\u1112\u1161\u11ab\u1100\u1173\u11af"));
        Assert.NotEqual(WordTypeKey.Of("\u00c9a"), WordTypeKey.Of("\u00e9a"));
        Assert.NotEqual(WordTypeKey.Of("Nairobi"), WordTypeKey.Of("nairobi"));
    }

    [Fact]
    public void ADispositionKeyedByTheStoreKeyJoinsTheFrequencyTypeCoverageNamesInAnotherSpelling()
    {
        var key = WordTypeKey.Of("\u00c9a");
        var ledger = LedgerTsvReader.Read(Tsv(Row(key, 2, "Disposition", cls: "name", evidence: "a place")));
        var join = LedgerJoiner.Join(ledger, Sidecar(ledger.Rows.Count, ledger.TokenSum), Frequency(("\u00c9a", 2)));

        var type = Assert.Single(join.Types);
        Assert.True(type.Resolved);
        Assert.Equal("name", CoverageStateRule.DispositionKind(type.Row!));
        Assert.Empty(join.Extra);
    }
}
