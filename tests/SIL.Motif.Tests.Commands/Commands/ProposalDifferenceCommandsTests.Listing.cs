using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using SIL.Motif.Commands;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed partial class ProposalDifferenceCommandsTests
{
    private const int FewWords = 90;

    [Fact]
    public void UnchangedWordsAreCountedInTheSummaryAndHaveNoRows()
    {
        using var fixture = new Fixture(_root, extraWords: FewWords);

        var produced = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!;

        Assert.Equal(FewWords + 2, produced.Summary.Words);
        Assert.Equal(fixture.Count("ProposalDifferenceCases", produced.Summary.DifferenceId), produced.Summary.ListedWordCount);
        Assert.Equal(produced.Summary.ListedWordCount, produced.WordsListed);
        Assert.Equal(produced.Summary.ListedWordCount, produced.MatchingWords);
        // Every third extra word stays approved and unchanged: counted, with no row.
        Assert.Equal(0, fixture.CountWord(produced.Summary.DifferenceId, "w00002"));
        Assert.Equal(1, fixture.CountWord(produced.Summary.DifferenceId, "w00000"));
        Assert.Equal(FewWords / 3, produced.Summary.Words - produced.Summary.ListedWordCount);
        var unchangedCell = Assert.Single(produced.Summary.Matrix, cell => cell.BeforeState == cell.AfterState && cell.Words > 2);
        Assert.Equal(FewWords / 3, unchangedCell.Words);
        Assert.Equal(0, unchangedCell.WordsListed);
        // Places exist only for listed words: one each, and sita has two.
        Assert.Equal(produced.Summary.ListedWordCount + 1, fixture.Count("ProposalDifferenceOccurrences", produced.Summary.DifferenceId));
        var hidden = ProposalDifferenceCommands.Show(fixture.ShowRequest() with { Word = "w00002" });
        Assert.Equal(0, hidden.Value!.MatchingWords);
    }

    [Fact]
    public void EachCellAndCategoryListsAtMostTheBoundHighestPlaceCountFirstThenSpelling()
    {
        // Every seventh word has three places, so the order within a group is not just the spelling.
        using var fixture = new Fixture(_root, extraWords: 3000, placesOfExtraWord: i => i % 7 == 0 ? 3 : 1);

        var produced = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!;

        var gained = Assert.Single(produced.Summary.Matrix, cell => cell.BeforeState == "no-parse" && cell.AfterState == "approved-unique");
        Assert.True(gained.Words > ProposalDifferenceListing.MostWordsPerGroup);
        Assert.Equal(ProposalDifferenceListing.MostWordsPerGroup, gained.WordsListed);
        var listed = ProposalDifferenceCommands.Show(fixture.ShowRequest() with
        {
            BeforeState = "no-parse", AfterState = "approved-unique", Limit = 200,
        });
        Assert.Equal(ProposalDifferenceListing.MostWordsPerGroup, listed.Value!.MatchingWords);
        // The gained words are 0, 3, 6, and so on; three-place ones come first, then the lowest spellings.
        var gainedWords = Enumerable.Range(0, 3000).Where(i => i % 3 == 0)
            .Select(i => (Word: "w" + i.ToString("D5"), Places: i % 7 == 0 ? 3 : 1)).ToArray();
        var expected = gainedWords.OrderByDescending(item => item.Places).ThenBy(item => item.Word, StringComparer.Ordinal)
            .Take(ProposalDifferenceListing.MostWordsPerGroup).Select(item => item.Word).Order(StringComparer.Ordinal).ToArray();
        var offsets = Enumerable.Range(0, ProposalDifferenceListing.MostWordsPerGroup / 200 + 1).Select(page => page * 200);
        var stored = offsets.SelectMany(offset => ProposalDifferenceCommands.Show(fixture.ShowRequest() with
        {
            BeforeState = "no-parse", AfterState = "approved-unique", Limit = 200, Offset = offset,
        }).Value!.Words).Select(word => word.Word).ToArray();
        Assert.Equal(expected, stored);
        // A word that is listed keeps every one of its places.
        var threePlaces = ProposalDifferenceCommands.Show(fixture.ShowRequest() with { Word = expected.First(word =>
            int.Parse(word[1..]) % 7 == 0) });
        Assert.Equal(3, threePlaces.Value!.Occurrences!.Total);
    }

    [Fact]
    public void ListingTheSameInputAgainChoosesTheSameWords()
    {
        using var fixture = new Fixture(_root, extraWords: 2000, variedExtraWords: true);
        var first = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!;
        var firstWords = AllListedWords(fixture, first.Summary.DifferenceId);
        fixture.Sql($"UPDATE ProposalDifferences SET SummaryJson = SummaryJson || ' ' WHERE DifferenceId = '{first.Summary.DifferenceId}';");

        var second = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!;

        Assert.NotEqual(first.Summary.DifferenceId, second.Summary.DifferenceId);
        Assert.Equal(firstWords, AllListedWords(fixture, second.Summary.DifferenceId));
        Assert.Equal(first.Summary.Matrix.Select(cell => cell with { }), second.Summary.Matrix);
    }

    [Fact]
    public void EveryCountSaysHowManyOfItsWordsAreListedAndTheListedWordsAreCounted()
    {
        using var fixture = new Fixture(_root, extraWords: 6000, variedExtraWords: true);

        var summary = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;

        Assert.Equal(summary.Words, summary.Matrix.Sum(cell => cell.Words));
        foreach (var cell in summary.Matrix)
        {
            Assert.InRange(cell.WordsListed, 0, Math.Min(cell.Words, ProposalDifferenceListing.MostWordsPerGroup * 2));
            var rows = ProposalDifferenceCommands.Show(fixture.ShowRequest() with
            {
                BeforeState = cell.BeforeState, AfterState = cell.AfterState, Limit = 1,
            });
            Assert.Equal(cell.WordsListed, rows.Value!.MatchingWords);
        }
        foreach (var bucket in summary.Buckets)
        {
            var rows = ProposalDifferenceCommands.Show(fixture.ShowRequest() with
            {
                Bucket = ProposalDifferenceRepository.BucketName(bucket.Bucket), Limit = 1,
            });
            Assert.Equal(bucket.WordsListed, rows.Value!.MatchingWords);
            Assert.True(bucket.WordsListed >= Math.Min(bucket.Words, ProposalDifferenceListing.MostWordsPerGroup));
        }
        Assert.True(summary.ListedWordCount < summary.Words);
    }

    [Fact]
    public void ASummarySavedBeforeTheBoundReadsAsHavingListedEveryWord()
    {
        using var fixture = new Fixture(_root);
        var id = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary.DifferenceId;
        var json = fixture.SummaryJson(id);
        var old = JsonNode.Parse(json)!.AsObject();
        old.Remove("WordsListed");
        foreach (var group in new[] { "Buckets", "Matrix", "Limitations" })
            foreach (var item in old[group]!.AsArray()) item!.AsObject().Remove("Listed");
        var oldJson = old.ToJsonString();
        fixture.Sql($"UPDATE ProposalDifferences SET SummaryJson = '{oldJson.Replace("'", "''")}', " +
            $"SummaryDigest = '{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(oldJson))).ToLowerInvariant()}' " +
            $"WHERE DifferenceId = '{id}';");

        var shown = ProposalDifferenceCommands.Show(fixture.ShowRequest() with { DifferenceId = id });

        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        Assert.Null(shown.Value!.Summary.WordsListed);
        Assert.Equal(shown.Value.Summary.Words, shown.Value.Summary.ListedWordCount);
        Assert.All(shown.Value.Summary.Matrix, cell => Assert.Equal(cell.Words, cell.WordsListed));
        Assert.All(shown.Value.Summary.Buckets, bucket => Assert.Equal(bucket.Words, bucket.WordsListed));
    }

    [Fact]
    public void TheSummaryOfADifferenceThatFillsNearlyEveryCellCategoryAndLimitationStaysUnderEightKilobytes()
    {
        using var fixture = new Fixture(_root, extraWords: 6000, variedExtraWords: true, limitedExtraWords: true);

        var summary = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;

        var groups = summary.Matrix.Count(cell => cell.WordsListed > 0) + summary.Buckets.Count + summary.Limitations.Count;
        Assert.True(groups >= 30, groups.ToString());
        Assert.True(fixture.SummaryJson(summary.DifferenceId).Length < 8 * 1024);
    }

    [Fact]
    public void TheSummaryNamesTheBoundsSoAChangeToThemIsVisible()
    {
        Assert.Equal(100, ProposalDifferenceListing.MostWordsPerGroup);
        Assert.Equal(10, ProposalDifferenceListing.MostPlacesPerWord);
    }

    [Fact]
    public void AWordWithMorePlacesThanTheBoundListsTheFirstInReadingOrderAndCountsTheRest()
    {
        using var fixture = new Fixture(_root, extraWords: 30, placesOfExtraWord: i => i == 0 ? 25 : 1);
        var produced = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!;
        var id = produced.Summary.DifferenceId;

        Assert.Equal(ProposalDifferenceListing.MostPlacesPerWord, fixture.CountWord(id, "w00000", "ProposalDifferenceOccurrences"));
        var first = ProposalDifferenceCommands.Show(fixture.ShowRequest() with
        {
            Word = "w00000", OccurrenceLimit = 4,
        }).Value!;
        var word = Assert.Single(first.Words);
        Assert.Equal(25, word.Occurrences);
        Assert.Equal(25, first.Occurrences!.Total);
        Assert.Equal(10, first.Occurrences.Listed);
        Assert.Equal(10, first.Occurrences.PlacesListed);
        Assert.Equal(4, first.Occurrences.Places.Count);
        Assert.Equal(4, first.Occurrences.NextOffset);

        var all = new List<ProposalDifferenceOccurrencePlace>(first.Occurrences.Places);
        int? next = first.Occurrences.NextOffset;
        var pages = 1;
        while (next is { } offset)
        {
            var page = ProposalDifferenceCommands.Show(fixture.ShowRequest() with
            {
                Word = "w00000", OccurrenceLimit = 4, OccurrenceOffset = offset,
            }).Value!.Occurrences!;
            Assert.NotEmpty(page.Places);
            all.AddRange(page.Places);
            next = page.NextOffset;
            pages++;
        }
        Assert.Equal(3, pages);
        Assert.Equal(10, all.Count);
        Assert.Equal(all.OrderBy(place => place.LineNumber).ThenBy(place => place.OccurrenceIndex), all);
        // The word's 25 places sit at positions 10 to 34 of line 1; the stored ten are the first ten.
        Assert.Equal(Enumerable.Range(10, 10), all.Select(place => place.OccurrenceIndex));
        Assert.All(all, place => Assert.Equal(1, place.LineNumber));
        Assert.Equal(all.Count, all.Select(place => (place.LineNumber, place.OccurrenceIndex)).Distinct().Count());

        var beyond = ProposalDifferenceCommands.Show(fixture.ShowRequest() with
        {
            Word = "w00000", OccurrenceOffset = 10,
        }).Value!.Occurrences!;
        Assert.Empty(beyond.Places);
        Assert.Null(beyond.NextOffset);
        Assert.Equal(25, beyond.Total);

        var whole = ProposalDifferenceCommands.Show(fixture.ShowRequest() with { Word = "w00001" }).Value!.Occurrences!;
        Assert.Equal(1, whole.Total);
        Assert.Null(whole.Listed);
        Assert.Equal(whole.Total, whole.PlacesListed);
    }

    [Fact]
    public void ADifferenceSavedBeforeThePlaceBoundReadsAsHavingListedEveryPlace()
    {
        using var fixture = new Fixture(_root, extraWords: 30, placesOfExtraWord: i => i == 0 ? 7 : 1);
        var id = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary.DifferenceId;

        var page = ProposalDifferenceCommands.Show(fixture.ShowRequest() with { Word = "w00000" }).Value!.Occurrences!;

        Assert.Equal(7, page.Total);
        Assert.Equal(7, fixture.CountWord(id, "w00000", "ProposalDifferenceOccurrences"));
        Assert.Null(page.Listed);
        Assert.Equal(7, page.PlacesListed);
        Assert.Null(page.NextOffset);
        Assert.DoesNotContain("Listed", System.Text.Json.JsonSerializer.Serialize(page));
    }

    private static string[] AllListedWords(Fixture fixture, string differenceId)
    {
        var words = new List<string>();
        for (var offset = 0; ; offset += 200)
        {
            var page = ProposalDifferenceCommands.Show(fixture.ShowRequest() with { DifferenceId = differenceId, Limit = 200, Offset = offset });
            words.AddRange(page.Value!.Words.Select(word => word.Word));
            if (page.Value.NextOffset is null) return [.. words];
        }
    }
}
