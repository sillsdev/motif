using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed partial class ProposalDifferenceCommandsTests
{
    [Fact]
    public void AskingForOneWordReturnsItsPlacesInTextOrderAPageAtATime()
    {
        using var fixture = new Fixture(_root);
        var id = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary.DifferenceId;

        var first = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(fixture.ProjectPath,
            DifferenceId: id, Word: "sita", OccurrenceLimit: 1)).Value!;
        var second = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(fixture.ProjectPath,
            DifferenceId: id, Word: "sita", OccurrenceOffset: 1, OccurrenceLimit: 1)).Value!;

        Assert.Equal(["sita"], first.Words.Select(word => word.Word));
        Assert.Equal("sita", first.Occurrences!.Word);
        Assert.Equal(2, first.Occurrences.Total);
        var one = Assert.Single(first.Occurrences.Places);
        Assert.Equal(fixture.TextId.ToString("D"), one.TextId);
        Assert.Equal(1, first.Occurrences.NextOffset);
        var two = Assert.Single(second.Occurrences!.Places);
        Assert.Null(second.Occurrences.NextOffset);
        Assert.True((one.LineNumber, one.OccurrenceIndex) != (two.LineNumber, two.OccurrenceIndex));
        Assert.True((one.LineNumber, one.OccurrenceIndex).CompareTo((two.LineNumber, two.OccurrenceIndex)) < 0);
    }

    [Fact]
    public void NoWordMeansNoPlacesAndAnUnknownWordHasNone()
    {
        using var fixture = new Fixture(_root);
        var id = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary.DifferenceId;

        var all = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(fixture.ProjectPath, DifferenceId: id)).Value!;
        var unknown = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(fixture.ProjectPath,
            DifferenceId: id, Word: "nothing")).Value!;

        Assert.Null(all.Occurrences);
        Assert.Empty(unknown.Words);
        Assert.Equal(0, unknown.Occurrences!.Total);
        Assert.Empty(unknown.Occurrences.Places);
    }

    [Fact]
    public void ADecidedProposalKeepsTheSummaryAndReturnsNoPlaces()
    {
        using var fixture = new Fixture(_root);
        var summary = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;
        new ProposalRepository(fixture.Database).SetStatus(CanonicalId.Parse(fixture.ProposalId), "rejected", null);

        var read = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(fixture.ProjectPath,
            DifferenceId: summary.DifferenceId, Word: "sita")).Value!;

        Assert.False(read.WordDetailKept);
        Assert.Null(read.Occurrences);
        Assert.Empty(read.Words);
        Assert.Equal(Json(summary), Json(read.Summary));
    }

    [Fact]
    public void ThePlaceOffsetAndLimitAreCheckedLikeTheWordPage()
    {
        using var fixture = new Fixture(_root);
        ProposalDifferenceCommands.Produce(fixture.ProduceRequest());

        foreach (var request in new[]
        {
            new ProposalDifferenceShowRequest(fixture.ProjectPath, ProposalId: fixture.ProposalId, Word: "sita", OccurrenceLimit: 0),
            new ProposalDifferenceShowRequest(fixture.ProjectPath, ProposalId: fixture.ProposalId, Word: "sita", OccurrenceLimit: 201),
            new ProposalDifferenceShowRequest(fixture.ProjectPath, ProposalId: fixture.ProposalId, Word: "sita", OccurrenceOffset: -1),
        })
            Assert.Equal(RefusalCodes.DifferenceInvalidPage, ProposalDifferenceCommands.Show(request).Refusal!.Code);
    }

    [Fact]
    public void TheLimitationFilterKeepsWordsWhoseEitherSideWasHeldBackAndCountsMatchTheSummary()
    {
        using var fixture = new Fixture(_root, cappedAfterMutu: true);
        var summary = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;

        var capped = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(fixture.ProjectPath,
            ProposalId: fixture.ProposalId, Limitation: "capped")).Value!;
        var timedOut = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(fixture.ProjectPath,
            ProposalId: fixture.ProposalId, Limitation: "timed-out")).Value!;

        Assert.Equal(["mutu"], capped.Words.Select(word => word.Word));
        Assert.Equal(summary.Limitations.Single(item => item.Limitation == HumanAgreementLimitation.Capped).Words,
            capped.MatchingWords);
        Assert.Empty(timedOut.Words);
        Assert.Equal(0, timedOut.MatchingWords);
    }

    [Fact]
    public void ALimitationFilterCombinesWithTheOtherFilters()
    {
        using var fixture = new Fixture(_root, cappedAfterMutu: true);
        ProposalDifferenceCommands.Produce(fixture.ProduceRequest());

        var both = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(fixture.ProjectPath,
            ProposalId: fixture.ProposalId, Limitation: "capped", Bucket: "delta.improved.approved-match")).Value!;

        Assert.Empty(both.Words);
    }

    [Fact]
    public void AnUnknownLimitationCodeIsRefusedWithANamedCode()
    {
        using var fixture = new Fixture(_root);

        var refusal = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(fixture.ProjectPath,
            ProposalId: fixture.ProposalId, Limitation: "nonsense")).Refusal!;

        Assert.Equal(RefusalCodes.DifferenceInvalidLimitation, refusal.Code);
        Assert.Equal(FailureReason.InvalidArgument, refusal.Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyWordIsRefusedInsteadOfBeingTreatedAsNoWord(string word)
    {
        using var fixture = new Fixture(_root);

        var refusal = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(fixture.ProjectPath,
            ProposalId: fixture.ProposalId, Word: word)).Refusal!;

        Assert.Equal(RefusalCodes.DifferenceInvalidWord, refusal.Code);
        Assert.Equal(FailureReason.InvalidArgument, refusal.Reason);
    }

    [Theory]
    [InlineData(1, null)]
    [InlineData(null, 10)]
    public void PlaceOffsetOrLimitWithoutAWordIsRefusedInsteadOfIgnored(int? offset, int? limit)
    {
        using var fixture = new Fixture(_root);

        var refusal = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(fixture.ProjectPath,
            ProposalId: fixture.ProposalId, OccurrenceOffset: offset, OccurrenceLimit: limit)).Refusal!;

        Assert.Equal(RefusalCodes.DifferenceOccurrenceNeedsWord, refusal.Code);
        Assert.Equal(FailureReason.InvalidArgument, refusal.Reason);
    }

    [Fact]
    public void ACodeQuotedInOtherTextDoesNotMakeAWordMatchTheLimitationFilter()
    {
        using var fixture = new Fixture(_root, cappedAfterMutu: true);
        var summary = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;
        var real = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(fixture.ProjectPath,
            DifferenceId: summary.DifferenceId, Limitation: "capped")).Value!;
        var all = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(fixture.ProjectPath,
            DifferenceId: summary.DifferenceId, Limit: 200)).Value!.Words;
        var plain = all.First(word => !word.Facts.Before.Limitations.Concat(word.Facts.After.Limitations).Any());
        // The quoted code sits in a field that is not a limitation list, so a plain text search would match it.
        var lookalike = plain.Facts with
        {
            Before = plain.Facts.Before with { MatchedUnknownAnalysisIds = ["capped"] },
        };
        var repository = new ProposalDifferenceRepository(fixture.Database);
        repository.Publish(new NewProposalDifference(summary, [.. all.Select(word =>
            new NewProposalDifferenceWord(word.Word, word.Word == plain.Word ? lookalike : word.Facts, [])).Append(
            new NewProposalDifferenceWord("zzlookalike", lookalike, []))]));

        var found = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(fixture.ProjectPath,
            DifferenceId: summary.DifferenceId, Limitation: "capped", Limit: 200)).Value!;

        Assert.True(real.MatchingWords > 0);
        Assert.Equal(real.MatchingWords, found.MatchingWords);
        Assert.DoesNotContain(found.Words, word => word.Word is "zzlookalike" || word.Word == plain.Word);
    }

    [Fact]
    public void ThePublishedFactsAreTheSameBytesAFreshSerializerWouldWrite()
    {
        using var fixture = new Fixture(_root, cappedAfterMutu: true);
        var summary = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;
        var words = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(fixture.ProjectPath,
            DifferenceId: summary.DifferenceId, Limit: 200)).Value!.Words;

        using var connection = fixture.Database.OpenConnection();
        foreach (var word in words)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT FactsJson FROM ProposalDifferenceCases WHERE DifferenceId = $id AND CaseKey = $key;";
            command.Parameters.AddWithValue("$id", summary.DifferenceId);
            command.Parameters.AddWithValue("$key", word.Word);
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(word.Facts, SIL.Motif.Contract.MotifJson.CreateOptions()),
                (string)command.ExecuteScalar()!);
        }
    }
}
