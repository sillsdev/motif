using System.Text.Json;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins <see cref="StatisticsViewModel"/> against opaque FakePanGloss rows: the six group choices, that
/// sort and filter never call <see cref="FakeCommandClient.StatsAsync"/> a second time, numeric versus
/// string sort, unknown JSON properties retained in a row's details map, and that a refusal leaves the
/// last successful result visible but marked stale rather than blanking it.
/// </summary>
public sealed class StatisticsViewModelTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";

    private static JsonElement Row(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static StatsCommandResponse RowsResponse(params string[] rows) =>
        new("assessment/one", "grammar.json", "cache.sqlite", null, rows.Select(Row).ToList());

    private static (FakeCommandClient Fake, StatisticsViewModel Statistics) NewViewModel()
    {
        var fake = new FakeCommandClient();
        var statistics = new StatisticsViewModel(fake) { ProjectPath = ProjectPath };
        return (fake, statistics);
    }

    [Fact]
    public void TheSixGroupsAreExactlyHandoffWritersOwnList()
    {
        var statistics = new StatisticsViewModel(new FakeCommandClient());

        Assert.Equal(
            ["word", "object", "allomorph", "morpheme", "group", "never-fires"],
            statistics.Groups);
    }

    [Fact]
    public async Task LoadingForwardsTheSelectedGroupAndPopulatesRows()
    {
        var (fake, statistics) = NewViewModel();
        fake.StatsCompletesWith(RowsResponse(
            """{"kind":"word","object":"o1","word":"abc","attempts":3,"failures":1,"elapsed":12.5}"""));

        await statistics.LoadCommand.ExecuteAsync(null);

        var request = Assert.Single(fake.StatsRequests);
        Assert.Equal(ProjectPath, request.ProjectPath);
        Assert.Null(request.ProposalId);
        Assert.Equal(StatsOutputKind.JsonRows, request.Output);
        Assert.Equal(["--group", "word"], request.ForwardedArguments);

        var row = Assert.Single(statistics.Rows);
        Assert.Equal("word", row.Kind);
        Assert.Equal("o1", row.Object);
        Assert.Equal("abc", row.Word);
        Assert.Equal(3, row.Attempts);
        Assert.Equal(1, row.Failures);
        Assert.Equal(12.5, row.Elapsed);
        Assert.False(statistics.IsStale);
        Assert.Null(statistics.Refusal);
    }

    [Fact]
    public async Task AnUnknownJsonPropertyIsRetainedInTheRowsDetailsMapRatherThanDropped()
    {
        var (fake, statistics) = NewViewModel();
        fake.StatsCompletesWith(RowsResponse(
            """{"kind":"word","object":"o1","word":"abc","attempts":3,"confidence":0.87,"note":"new-field"}"""));

        await statistics.LoadCommand.ExecuteAsync(null);

        var row = Assert.Single(statistics.Rows);
        Assert.Equal(2, row.Details.Count);
        Assert.Equal(0.87, row.Details["confidence"].GetDouble());
        Assert.Equal("new-field", row.Details["note"].GetString());
        Assert.DoesNotContain("kind", row.Details.Keys);
    }

    [Fact]
    public async Task FilteringAndSortingNeverIssueASecondStatsRequest()
    {
        var (fake, statistics) = NewViewModel();
        fake.StatsCompletesWith(RowsResponse(
            """{"kind":"word","object":"o1","word":"alpha","attempts":9}""",
            """{"kind":"word","object":"o2","word":"beta","attempts":10}"""));

        await statistics.LoadCommand.ExecuteAsync(null);
        Assert.Single(fake.StatsRequests);

        statistics.FilterText = "alpha";
        statistics.SortBy("attempts");
        statistics.SortBy("attempts");

        Assert.Single(fake.StatsRequests);
    }

    [Fact]
    public async Task FilteringKeepsOnlyRowsWhoseRawTextContainsTheFilterText()
    {
        var (fake, statistics) = NewViewModel();
        fake.StatsCompletesWith(RowsResponse(
            """{"kind":"word","object":"o1","word":"alpha"}""",
            """{"kind":"word","object":"o2","word":"beta"}"""));

        await statistics.LoadCommand.ExecuteAsync(null);
        statistics.FilterText = "alpha";

        var row = Assert.Single(statistics.Rows);
        Assert.Equal("alpha", row.Word);
    }

    [Fact]
    public async Task SortingANumericColumnOrdersByValueRatherThanText()
    {
        var (fake, statistics) = NewViewModel();
        fake.StatsCompletesWith(RowsResponse(
            """{"kind":"word","object":"o1","word":"a","attempts":10}""",
            """{"kind":"word","object":"o2","word":"b","attempts":9}"""));

        await statistics.LoadCommand.ExecuteAsync(null);
        statistics.SortBy("attempts");

        Assert.Equal([9, 10], statistics.Rows.Select(row => row.Attempts));

        statistics.SortBy("attempts");
        Assert.Equal([10, 9], statistics.Rows.Select(row => row.Attempts));
    }

    [Fact]
    public async Task SortingATextColumnOrdersLexicographicallyRatherThanNumerically()
    {
        var (fake, statistics) = NewViewModel();
        fake.StatsCompletesWith(RowsResponse(
            """{"kind":"word","object":"o1","word":"10"}""",
            """{"kind":"word","object":"o2","word":"9"}"""));

        await statistics.LoadCommand.ExecuteAsync(null);
        statistics.SortBy("word");

        // Lexicographic order: "10" sorts before "9" because '1' precedes '9'; a numeric sort would reverse this.
        Assert.Equal(["10", "9"], statistics.Rows.Select(row => row.Word));
    }

    [Fact]
    public async Task ARefusalLeavesTheLastSuccessfulResultVisibleButMarkedStale()
    {
        var (fake, statistics) = NewViewModel();
        fake.StatsCompletesWith(RowsResponse("""{"kind":"word","object":"o1","word":"abc"}"""));
        await statistics.LoadCommand.ExecuteAsync(null);
        Assert.False(statistics.IsStale);

        var refusal = new Refusal("stats.no-assessment", FailureReason.NotFound, "No Assessment recorded yet.");
        fake.StatsRefusesWith(refusal);
        await statistics.LoadCommand.ExecuteAsync(null);

        var row = Assert.Single(statistics.Rows);
        Assert.Equal("abc", row.Word);
        Assert.True(statistics.IsStale);
        Assert.Same(refusal, statistics.Refusal);
    }

    [Fact]
    public async Task ANewSuccessfulRunClearsStaleResultsAndReplacesTheRows()
    {
        var (fake, statistics) = NewViewModel();
        fake.StatsCompletesWith(RowsResponse("""{"kind":"word","object":"o1","word":"abc"}"""));
        await statistics.LoadCommand.ExecuteAsync(null);

        fake.StatsRefusesWith(new Refusal("stats.no-assessment", FailureReason.NotFound, "Gone for now."));
        await statistics.LoadCommand.ExecuteAsync(null);
        Assert.True(statistics.IsStale);

        fake.StatsCompletesWith(RowsResponse("""{"kind":"word","object":"o2","word":"fresh"}"""));
        await statistics.LoadCommand.ExecuteAsync(null);

        var row = Assert.Single(statistics.Rows);
        Assert.Equal("fresh", row.Word);
        Assert.False(statistics.IsStale);
        Assert.Null(statistics.Refusal);
    }

    [Fact]
    public void LoadIsDisabledUntilAProjectIsChosen()
    {
        var statistics = new StatisticsViewModel(new FakeCommandClient());

        Assert.False(statistics.LoadCommand.CanExecute(null));

        statistics.ProjectPath = ProjectPath;

        Assert.True(statistics.LoadCommand.CanExecute(null));
    }
}
