using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed partial class ProposalDifferenceCommandsTests
{
    private static readonly string[] WordTables =
        ["ProposalDifferenceCases", "ProposalDifferenceOccurrences", "ProposalDifferenceBuckets"];

    [Theory]
    [InlineData("rejected")]
    [InlineData("withdrawn")]
    [InlineData("superseded")]
    [InlineData("applied")]
    public void DecidingTheProposalDropsWordDetailAndKeepsTheSummaryIdentical(string status)
    {
        using var fixture = new Fixture(_root);
        var produced = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;

        new ProposalRepository(fixture.Database).SetStatus(CanonicalId.Parse(fixture.ProposalId), status, null);

        foreach (var table in WordTables) Assert.Equal(0, fixture.Count(table, produced.DifferenceId));
        Assert.Equal(1, fixture.Count("ProposalDifferences", produced.DifferenceId));
        var read = ProposalDifferenceCommands.Show(fixture.ShowRequest());
        Assert.True(read.Succeeded, read.Refusal?.Message);
        Assert.False(read.Value!.WordDetailKept);
        Assert.Empty(read.Value.Words);
        Assert.Equal(0, read.Value.MatchingWords);
        Assert.Null(read.Value.NextOffset);
        Assert.Equal(Json(produced), Json(read.Value.Summary));
    }

    [Fact]
    public void DeferringDropsWordDetailAndDecidingFromDeferredLeavesOnlyTheSummary()
    {
        using var fixture = new Fixture(_root);
        var produced = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;
        var proposals = new ProposalRepository(fixture.Database);
        var id = CanonicalId.Parse(fixture.ProposalId);
        Assert.Equal(2, fixture.Count("ProposalDifferenceCases", produced.DifferenceId));

        Assert.True(proposals.Transition(id, ["proposed"], "deferred").Moved);

        foreach (var table in WordTables) Assert.Equal(0, fixture.Count(table, produced.DifferenceId));
        Assert.False(ProposalDifferenceCommands.Show(fixture.ShowRequest()).Value!.WordDetailKept);
        Assert.True(proposals.Transition(id, ["deferred"], "rejected").Moved);
        Assert.False(ProposalDifferenceCommands.Show(fixture.ShowRequest()).Value!.WordDetailKept);
    }

    [Fact]
    public void DeferringTheProposalKeepsTheSummaryIdenticalAndMarksItAsHavingNoWords()
    {
        using var fixture = new Fixture(_root);
        var produced = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;

        new ProposalRepository(fixture.Database).SetStatus(CanonicalId.Parse(fixture.ProposalId), "deferred", null);

        var read = ProposalDifferenceCommands.Show(fixture.ShowRequest());
        Assert.True(read.Succeeded, read.Refusal?.Message);
        Assert.False(read.Value!.WordDetailKept);
        Assert.Empty(read.Value.Words);
        Assert.Equal(0, read.Value.MatchingWords);
        Assert.Equal(Json(produced), Json(read.Value.Summary));
        Assert.Equal(1, fixture.Count("ProposalDifferences", produced.DifferenceId));
    }

    [Fact]
    public void ADeferredProposalMakesTheDifferenceAgainAndGetsItsWordsBack()
    {
        using var fixture = new Fixture(_root);
        var first = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;
        new ProposalRepository(fixture.Database).SetStatus(CanonicalId.Parse(fixture.ProposalId), "deferred", null);

        var again = ProposalDifferenceCommands.Produce(fixture.ProduceRequest());

        Assert.True(again.Succeeded, again.Refusal?.Message);
        Assert.NotEqual(first.DifferenceId, again.Value!.Summary.DifferenceId);
        Assert.True(again.Value.WordDetailKept);
        Assert.Equal(2, again.Value.MatchingWords);
        Assert.Equal(1, fixture.DifferenceRows());
    }

    [Fact]
    public void RemakingInTheDeferredStateKeepsItsWordsUntilTheProposalNextChangesStatus()
    {
        using var fixture = new Fixture(_root);
        ProposalDifferenceCommands.Produce(fixture.ProduceRequest());
        var proposals = new ProposalRepository(fixture.Database);
        var id = CanonicalId.Parse(fixture.ProposalId);
        proposals.SetStatus(id, "deferred", null);
        var remade = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;

        Assert.Equal(2, fixture.Count("ProposalDifferenceCases", remade.DifferenceId));
        Assert.True(proposals.Transition(id, ["deferred"], "withdrawn").Moved);

        foreach (var table in WordTables) Assert.Equal(0, fixture.Count(table, remade.DifferenceId));
    }

    [Fact]
    public void AFilteredReadAfterTheDropSucceedsWithNoWords()
    {
        using var fixture = new Fixture(_root);
        ProposalDifferenceCommands.Produce(fixture.ProduceRequest());
        new ProposalRepository(fixture.Database).SetStatus(CanonicalId.Parse(fixture.ProposalId), "withdrawn", null);

        var read = ProposalDifferenceCommands.Show(fixture.ShowRequest() with { CouldBeBad = true, Offset = 5, Limit = 10 });

        Assert.True(read.Succeeded, read.Refusal?.Message);
        Assert.False(read.Value!.WordDetailKept);
        Assert.Empty(read.Value.Words);
    }

    [Fact]
    public void ProducingAgainForTheSameTrialTextBaselineAndPolicyReturnsTheExistingDifference()
    {
        using var fixture = new Fixture(_root);
        var first = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!;

        var second = ProposalDifferenceCommands.Produce(fixture.ProduceRequest());

        Assert.True(second.Succeeded, second.Refusal?.Message);
        Assert.Equal(first.Summary.DifferenceId, second.Value!.Summary.DifferenceId);
        Assert.Equal(first.Summary.CreatedUtc, second.Value.Summary.CreatedUtc);
        Assert.True(second.Value.WordDetailKept);
        Assert.Equal(2, second.Value.MatchingWords);
        Assert.Equal(1, fixture.DifferenceRows());
    }

    [Fact]
    public void AnotherTrialOfTheSameProposalReplacesTheOldDifferenceForTheText()
    {
        using var fixture = new Fixture(_root);
        var first = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;
        var secondJob = fixture.AddTrial("trial-2");

        var second = ProposalDifferenceCommands.Produce(fixture.ProduceRequest() with { JobId = secondJob });

        Assert.True(second.Succeeded, second.Refusal?.Message);
        Assert.NotEqual(first.DifferenceId, second.Value!.Summary.DifferenceId);
        Assert.Equal(1, fixture.DifferenceRows());
        Assert.Equal(0, fixture.Count("ProposalDifferenceCases", first.DifferenceId));
    }

    [Fact]
    public void ADecidedProposalWithNoSavedDifferenceSaysSoWithoutSuggestingAnotherComparison()
    {
        using var fixture = new Fixture(_root);
        var undecided = ProposalDifferenceCommands.Show(fixture.ShowRequest());
        new ProposalRepository(fixture.Database).SetStatus(CanonicalId.Parse(fixture.ProposalId), "rejected", null);

        var decided = ProposalDifferenceCommands.Show(fixture.ShowRequest());

        Assert.Equal("difference.none", undecided.Refusal?.Code);
        Assert.Contains("difference produce", undecided.Refusal!.Message, StringComparison.Ordinal);
        Assert.Equal("difference.none", decided.Refusal?.Code);
        Assert.Contains("decided", decided.Refusal!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("produce", decided.Refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("compare again", decided.Refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PublishingAfterTheProposalIsDecidedStoresNothingAndKeepsTheSavedSummary()
    {
        using var fixture = new Fixture(_root);
        var first = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;
        var repository = new ProposalDifferenceRepository(fixture.Database);
        var built = new NewProposalDifference(first with { DifferenceId = "built-before-the-decision" }, []);
        new ProposalRepository(fixture.Database).SetStatus(CanonicalId.Parse(fixture.ProposalId), "rejected", null);

        Assert.Throws<ProposalDecidedException>(() => repository.Publish(built));

        Assert.Equal(first.DifferenceId, Assert.Single(fixture.DifferenceIds()));
        Assert.False(repository.GetSaved(first.DifferenceId)!.WordDetailKept);
        foreach (var table in WordTables) Assert.Equal(0, fixture.Count(table, first.DifferenceId));
    }

    [Fact]
    public void ADecidedProposalReturnsItsSavedSummaryAndRefusesToCompareAnythingNew()
    {
        using var fixture = new Fixture(_root);
        var first = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;
        new ProposalRepository(fixture.Database).SetStatus(CanonicalId.Parse(fixture.ProposalId), "rejected", null);

        var again = ProposalDifferenceCommands.Produce(fixture.ProduceRequest());
        var secondJob = fixture.AddTrial("trial-2");
        var other = ProposalDifferenceCommands.Produce(fixture.ProduceRequest() with { JobId = secondJob });

        Assert.True(again.Succeeded, again.Refusal?.Message);
        Assert.Equal(first.DifferenceId, again.Value!.Summary.DifferenceId);
        Assert.False(again.Value.WordDetailKept);
        Assert.Equal("difference.proposal-decided", other.Refusal?.Code);
        Assert.Equal(first.DifferenceId, Assert.Single(fixture.DifferenceIds()));
    }

    [Fact]
    public void ADecidedProposalWithNoSavedDifferenceRefusesToProduceOne()
    {
        using var fixture = new Fixture(_root);
        new ProposalRepository(fixture.Database).SetStatus(CanonicalId.Parse(fixture.ProposalId), "withdrawn", null);

        var produced = ProposalDifferenceCommands.Produce(fixture.ProduceRequest());

        Assert.Equal("difference.proposal-decided", produced.Refusal?.Code);
        Assert.Equal(0, fixture.DifferenceRows());
    }

    [Fact]
    public void AReopenedProposalCanBeComparedAgainAndKeepsWordDetailAgain()
    {
        using var fixture = new Fixture(_root);
        var first = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;
        var proposals = new ProposalRepository(fixture.Database);
        var id = CanonicalId.Parse(fixture.ProposalId);
        proposals.SetStatus(id, "rejected", null);
        proposals.SetStatus(id, "proposed", null);

        var again = ProposalDifferenceCommands.Produce(fixture.ProduceRequest());

        Assert.True(again.Succeeded, again.Refusal?.Message);
        Assert.True(again.Value!.WordDetailKept);
        Assert.Equal(2, again.Value.MatchingWords);
        Assert.Equal(1, fixture.DifferenceRows());
        Assert.NotEqual(first.DifferenceId, again.Value.Summary.DifferenceId);
    }

    [Fact]
    public void DroppingTheDetailOfAThousandsOfWordsTextLeavesOnlyTheSmallSummary()
    {
        using var fixture = new Fixture(_root, extraWords: 3000);
        var produced = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;
        Assert.Equal(3002, fixture.Count("ProposalDifferenceCases", produced.DifferenceId));

        new ProposalRepository(fixture.Database).SetStatus(CanonicalId.Parse(fixture.ProposalId), "rejected", null);

        foreach (var table in WordTables) Assert.Equal(0, fixture.Count(table, produced.DifferenceId));
        Assert.InRange(fixture.SummaryBytes(produced.DifferenceId), 1, 8 * 1024);
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("withdrawn")]
    public void AClosedProposalWithARealDifferenceDeletesCleanlyAndKeepsAStandaloneBeforeSide(string status)
    {
        using var fixture = new Fixture(_root);
        var produced = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;
        new ProposalRepository(fixture.Database).SetStatus(CanonicalId.Parse(fixture.ProposalId), status, null);

        var deleted = ProposalLifecycleCommand.Delete(fixture.Database, fixture.Project, fixture.ProposalId);

        Assert.True(deleted.Succeeded, deleted.Refusal?.Message);
        Assert.Equal(0, fixture.DifferenceRows());
        Assert.Equal(0, fixture.Count("ProposalDifferences", produced.DifferenceId));
        Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM Proposals;"));
        Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM Assessments WHERE AssessmentId = 'after';"));
        Assert.Equal(1, fixture.Scalar("SELECT COUNT(*) FROM Assessments WHERE AssessmentId = 'before';"));
    }

    [Fact]
    public void ArchivingAProposalWhoseDifferenceStillHoldsWordDetailRemovesItWithoutAForeignKeyError()
    {
        using var fixture = new Fixture(_root);
        var produced = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;
        fixture.Sql("UPDATE Proposals SET Status = 'rejected';");
        new JobRepository(fixture.Database).DeleteArchived(Assert.Single(fixture.TrialJobIds()));

        new ProposalRepository(fixture.Database).DeleteArchived(CanonicalId.Parse(fixture.ProposalId));

        foreach (var table in new[] { "ProposalDifferences", "ProposalDifferenceCases", "ProposalDifferenceOccurrences",
            "ProposalDifferenceBuckets" })
            Assert.Equal(0, fixture.Count(table, produced.DifferenceId));
    }

    private static string Json(ProposalDifferenceSummary summary) =>
        System.Text.Json.JsonSerializer.Serialize(summary, MotifJson.CreateOptions());

    private sealed partial class Fixture
    {
        public ProposalDifferenceShowRequest ShowRequest() => new(ProjectPath, ProposalId);

        public long Scalar(string sql)
        {
            using var connection = Database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(command.ExecuteScalar());
        }

        public List<string> TrialJobIds()
        {
            using var connection = Database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT JobId FROM Jobs;";
            using var reader = command.ExecuteReader();
            var ids = new List<string>();
            while (reader.Read()) ids.Add(reader.GetString(0));
            return ids;
        }

        public int DifferenceRows() => DifferenceIds().Count;

        public List<string> DifferenceIds()
        {
            using var connection = Database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT DifferenceId FROM ProposalDifferences WHERE ProposalId = $proposal;";
            command.Parameters.AddWithValue("$proposal", ProposalId);
            using var reader = command.ExecuteReader();
            var ids = new List<string>();
            while (reader.Read()) ids.Add(reader.GetString(0));
            return ids;
        }
    }
}
