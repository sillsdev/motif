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
            """{"kind":"word","form":"abc","attempts":3,"passes":1,"elapsed_ns":12500000}"""));

        await statistics.LoadCommand.ExecuteAsync(null);

        var request = Assert.Single(fake.StatsRequests);
        Assert.Equal(ProjectPath, request.ProjectPath);
        Assert.Null(request.ProposalId);
        Assert.Equal(StatsOutputKind.JsonRows, request.Output);
        Assert.Equal(["--group", "word"], request.ForwardedArguments);

        var row = Assert.Single(statistics.Rows);
        Assert.Equal("word", row.Kind);
        Assert.Null(row.Object);
        Assert.Equal("abc", row.Word);
        Assert.Equal(3, row.Attempts);
        Assert.Equal(1, row.Passes);
        Assert.Equal(12.5, row.ElapsedMs);
        Assert.False(statistics.IsStale);
        Assert.Null(statistics.Refusal);
    }

    [Fact]
    public async Task AnUnknownJsonPropertyIsRetainedInTheRowsDetailsMapRatherThanDropped()
    {
        var (fake, statistics) = NewViewModel();
        fake.StatsCompletesWith(RowsResponse(
            """{"kind":"word","form":"abc","attempts":3,"confidence":0.87,"note":"new-field"}"""));

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
            """{"kind":"word","form":"alpha","attempts":9}""",
            """{"kind":"word","form":"beta","attempts":10}"""));

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
            """{"kind":"word","form":"alpha"}""",
            """{"kind":"word","form":"beta"}"""));

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
            """{"kind":"word","form":"a","attempts":10}""",
            """{"kind":"word","form":"b","attempts":9}"""));

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
            """{"kind":"word","form":"10"}""",
            """{"kind":"word","form":"9"}"""));

        await statistics.LoadCommand.ExecuteAsync(null);
        statistics.SortBy("word");

        // Lexicographic order: "10" sorts before "9" because '1' precedes '9'; a numeric sort would reverse this.
        Assert.Equal(["10", "9"], statistics.Rows.Select(row => row.Word));
    }

    [Fact]
    public async Task ARefusalLeavesTheLastSuccessfulResultVisibleButMarkedStale()
    {
        var (fake, statistics) = NewViewModel();
        fake.StatsCompletesWith(RowsResponse("""{"kind":"word","form":"abc"}"""));
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
        fake.StatsCompletesWith(RowsResponse("""{"kind":"word","form":"abc"}"""));
        await statistics.LoadCommand.ExecuteAsync(null);

        fake.StatsRefusesWith(new Refusal("stats.no-assessment", FailureReason.NotFound, "Gone for now."));
        await statistics.LoadCommand.ExecuteAsync(null);
        Assert.True(statistics.IsStale);

        fake.StatsCompletesWith(RowsResponse("""{"kind":"word","form":"fresh"}"""));
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
    [Fact]
    public async Task CapturedStatisticsRetainMetadataAndProjectRealWordAndObjectColumns()
    {
        var (fake, statistics) = NewViewModel();
        fake.StatsCompletesWith(RowsResponse(
            """{"engine":"hc","filters":{"by_kind":false,"direction":null,"exclude_censored":false,"kind":null,"object":null,"sort":null,"stratum":null,"top":null,"word":null},"grammar_hash":"acc80fced0c2a72bb26b2a14c02280c96f2eeb0b9a19d2fb13d9a155ca172d37","meta":true,"orientation":"word","totals":{"attempts":0,"rows":3,"time_ns":322800},"unmeasured":{}}""",
            """{"attempts":0,"capped":false,"elapsed_ns":122200,"form":"motifa","passes":1,"timed_out":false}"""));
        await statistics.LoadCommand.ExecuteAsync(null);
        Assert.Equal("word", Assert.Single(statistics.Metadata).GetProperty("orientation").GetString());
        var word = Assert.Single(statistics.Rows);
        Assert.Equal("motifa", word.Word);
        Assert.Equal(0.1222, word.ElapsedMs);
        Assert.Equal(1, word.Passes);
        Assert.Equal("Search completed", word.CompletionStatus);

        fake.StatsCompletesWith(RowsResponse(
            """{"engine":"hc","filters":{"by_kind":false,"direction":null,"exclude_censored":false,"kind":null,"object":null,"sort":null,"stratum":null,"top":null,"word":null},"grammar_hash":"acc80fced0c2a72bb26b2a14c02280c96f2eeb0b9a19d2fb13d9a155ca172d37","meta":true,"orientation":"object","totals":{"attempts_by_kind":{"lex_entry":2,"root_index":9},"attributed_pct":4.368029739776952,"rows":5,"rows_shown":5,"run_elapsed_ns":322800,"time_ns":14100,"uses":2},"unmeasured":{}}""",
            """{"amp":null,"attempts":1,"identity_quality":"authored","kind":"lex_entry","label":"lex_entry: first seeded gloss","no_root":null,"not_applied":null,"outputs":null,"surface_mismatch":0,"time_ns":7600,"uses":1,"work":null}"""));
        await statistics.LoadCommand.ExecuteAsync(null);
        Assert.Equal("object", Assert.Single(statistics.Metadata).GetProperty("orientation").GetString());
        var item = Assert.Single(statistics.Rows);
        Assert.Equal("lex_entry: first seeded gloss", item.Object);
        Assert.Equal("lex_entry", item.Kind);
        Assert.Equal(0.0076, item.ElapsedMs);
        Assert.Null(item.Passes);
        Assert.Null(item.CompletionStatus);
        Assert.Equal("authored", item.Details["identity_quality"].GetString());
        Assert.Equal(1, item.Details["uses"].GetInt32());
    }

    [Theory]
    [InlineData(true, false, "step limit")]
    [InlineData(false, true, "time limit")]
    [InlineData(true, true, "step and time limits")]
    public async Task PartialFindingsRemainIncompleteAndLaterWordsCanComplete(bool capped, bool timedOut, string reason)
    {
        var (fake, statistics) = NewViewModel();
        fake.StatsCompletesWith(RowsResponse(
            JsonSerializer.Serialize(new { form = "partial", passes = 1, capped, timed_out = timedOut }),
            """{"form":"later","passes":0,"capped":false,"timed_out":false}"""));
        await statistics.LoadCommand.ExecuteAsync(null);
        Assert.True(statistics.Rows[0].IsIncomplete);
        Assert.Equal($"INCOMPLETE — parsing did not finish ({reason})", statistics.Rows[0].CompletionStatus);
        Assert.False(statistics.Rows[1].IsIncomplete);
        Assert.Equal("Search completed", statistics.Rows[1].CompletionStatus);
    }

    [Theory]
    [InlineData("{\"form\":\"unknown\",\"passes\":1}")]
    [InlineData("{\"form\":\"unknown\",\"capped\":false}")]
    [InlineData("{\"form\":\"unknown\",\"timed_out\":false}")]
    public void MissingCompletionFlagsNeverClaimSearchCompleted(string json)
    {
        Assert.Equal("Completion unavailable", new StatsRowViewModel(Row(json)).CompletionStatus);
    }

    [Fact]
    public async Task ExactAssessmentIsForwardedUnlessAProposalIsSelected()
    {
        var (fake, statistics) = NewViewModel();
        statistics.AssessmentId = "assessment/displayed";
        fake.StatsCompletesWith(RowsResponse());
        await statistics.LoadCommand.ExecuteAsync(null);
        Assert.Equal("assessment/displayed", Assert.Single(fake.StatsRequests).AssessmentId);
        statistics.ProposalId = "proposal/selected";
        await statistics.LoadCommand.ExecuteAsync(null);
        Assert.Null(fake.StatsRequests[1].AssessmentId);
        Assert.Equal("proposal/selected", fake.StatsRequests[1].ProposalId);
        statistics.Reset();
        Assert.Null(statistics.AssessmentId);
        Assert.Null(statistics.ProposalId);
        Assert.Empty(statistics.Metadata);
    }
}
