using System.Collections.Immutable;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Review;
using Xunit;

namespace SIL.Motif.Tests.Contract.Review;

public sealed class ReadingDeltaBuilderTests
{
    private static string Id(int value) => CanonicalId.FromGuid(new Guid(value, 0, 0, [1, 2, 3, 4, 5, 6, 7, 8])).Value;

    private static ReadingCopy Copy(int number)
    {
        var signature = new ReadingSignature([new ReadingMorpheme(Id(number), Id(900001), [], ReadingSenseStatus.Resolved,
            Id(number + 100), Id(number + 200), null, true, null, [])]);
        return new ReadingCopy(signature.Id, signature);
    }

    private static ReadingDeltaInput Word(string word, int[] before, int[] after, bool negative = false) =>
        new(word, "case/" + word, negative, [.. before.Select(Copy)], [.. after.Select(Copy)]);

    [Fact]
    public void ChangedNeedsNoSharedReadingAndDoubledNeedsEveryOldReadingKept()
    {
        var forms = ReadingDeltaBuilder.Compute([Word("a", [1, 2], [2, 3]), Word("b", [1, 2], [3, 4]), Word("c", [1], [1, 1])]);

        Assert.Empty(forms[0].Classes);
        Assert.Equal(["changed"], forms[1].Classes);
        Assert.Equal(["doubled"], forms[2].Classes);
    }

    [Fact]
    public void ANonWordThatStopsParsingIsRejectedAndNeverLost()
    {
        var form = Assert.Single(ReadingDeltaBuilder.Compute([Word("x", [1], [], negative: true)]));

        Assert.Equal(["stopped-negative"], form.Classes);
    }

    [Fact]
    public void RowTextUsesTheLaneSeparatorsAndSortedKeys()
    {
        var row = ReadingManifestFormat.Row("k\"á", Copy(1).Signature, 2);

        Assert.StartsWith("{\"word\": \"k\\\"á\", \"signature\": [{\"allomorphId\": \"", row);
        Assert.Contains("\"features\": {}, \"isStem\": true, \"msaId\": \"", row);
        Assert.EndsWith(", \"count\": 2}", row);
        Assert.True(ReadingManifestFormat.TryParseRow(row, out var word, out var key, out var count));
        Assert.Equal(("k\"á", 2), (word, count));
        Assert.Equal(ReadingManifestFormat.Key(Copy(1).Signature), key);
    }

    [Fact]
    public void OnlyOperationsWhoseKindCreatesNameAnAliasAndEveryIdSpellingIsCovered()
    {
        var created = CanonicalId.FromGuid(new Guid(5, 0, 0, [1, 2, 3, 4, 5, 6, 7, 8]));
        var edited = CanonicalId.FromGuid(new Guid(6, 0, 0, [1, 2, 3, 4, 5, 6, 7, 8]));
        var empty = System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone();
        var aliases = ReadingManifestFormat.CreatedAliases(
        [
            new OperationEnvelope(CanonicalId.Mint("operation/"), "lexical/lexEntry/setGloss", entityId: edited, after: empty),
            new OperationEnvelope(CanonicalId.Mint("operation/"), "lexical/lexDb/createEntries", entityId: created, after: empty),
            new OperationEnvelope(CanonicalId.Mint("operation/"), "lexical/lexDb/createEntries", entityId: created, after: empty),
        ]);

        Assert.Equal("created:0", aliases[created.Value]);
        Assert.Equal("created:0", aliases[created.ToGuid().ToString("D")]);
        Assert.Equal("created:0", aliases[created.ToGuid().ToString("D").ToUpperInvariant()]);
        Assert.False(aliases.ContainsKey(edited.Value));
    }
}
