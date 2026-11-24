using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ProposalDocumentDifferenceTests
{
    [UiThreadTheory]
    [InlineData("saved", false, false)]
    [InlineData("words", false, false)]
    [InlineData("places", false, false)]
    [InlineData("saved", false, true)]
    [InlineData("words", false, true)]
    [InlineData("places", false, true)]
    [InlineData("saved", true, false)]
    [InlineData("words", true, false)]
    [InlineData("places", true, false)]
    [InlineData("saved", true, true)]
    [InlineData("words", true, true)]
    [InlineData("places", true, true)]
    public async Task ClosingAndSwitchingProjectsCancelAndJoinEveryDifferenceReader(
        string reader, bool switchProject, bool ignoresCancellation)
    {
        var harness = new Harness();
        await harness.OpenAsync();
        var one = harness.Page.SelectedDocument!;
        await ReadOlderPagesAsync(one.Difference, "one", 3);
        harness.Page.SelectedProposal = harness.Page.Groups.SelectMany(group => group.Cards)
            .Single(card => card.ProposalId == "two");
        await harness.Page.DetailReading;
        var two = harness.Page.SelectedDocument!;
        await ReadOlderPagesAsync(two.Difference, "two", 4);
        var oldChip = one.Difference.Words[1];
        var savedChoice = one.Difference.SavedChoices.Single(choice => choice.DifferenceId == "one/newest");
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Fixture.BeforeRead = async (request, token) =>
        {
            if (request.ProposalId != "one" || !(reader switch
                {
                    "saved" => request.DifferenceId == "one/newest",
                    "words" => request.Word is null && request.Offset == 50,
                    _ => request.Word is not null && request.OccurrenceOffset == 50,
                })) return;
            entered.TrySetResult(token);
            if (ignoresCancellation) await release.Task;
            else await release.Task.WaitAsync(token);
        };
        Task reading = Task.CompletedTask;
        Task stopping = Task.CompletedTask;
        try
        {
            if (reader == "saved") one.Difference.SelectedSaved = savedChoice;
            else reading = ((IAsyncRelayCommand)(reader == "words"
                ? one.Difference.ShowMoreWordsCommand : one.Difference.ShowMorePlacesCommand)).ExecuteAsync(null);
            var token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            stopping = switchProject ? harness.Context.OpenProjectAsync("other.fwdata")
                : harness.Page.CloseDocumentCommand.ExecuteAsync(one);
            await Wait.UntilAsync(() => token.IsCancellationRequested || stopping.IsCompleted,
                "shutdown to cancel the reader or reveal an early return");
            var cancelled = token.IsCancellationRequested;
            var joined = !stopping.IsCompleted;
            release.TrySetResult();
            await stopping.WaitAsync(TimeSpan.FromSeconds(10));
            await reading;
            await Wait.UntilAsync(() => !one.Difference.IsReadingMore, "the delayed reader to leave");

            Assert.True(cancelled);
            if (ignoresCancellation) Assert.True(joined);
            Assert.Equal(ProposalDifferenceState.None, one.Difference.State);
            Assert.Empty(one.Difference.Words);
            Assert.Empty(one.Difference.Places);
            var requests = harness.Fake.ProposalDifferenceRequests.Count;
            oldChip.ChooseCommand.Execute(null);
            one.Difference.SelectedSaved = savedChoice;
            one.Difference.ShowMoreWordsCommand.Execute(null);
            one.Difference.ShowMorePlacesCommand.Execute(null);
            Assert.Equal(requests, harness.Fake.ProposalDifferenceRequests.Count);

            if (switchProject)
            {
                await harness.Page.DetailReading;
                var current = harness.Page.SelectedDocument!;
                Assert.NotSame(one, current);
                Assert.Equal("other.fwdata", current.ProjectPath);
                Assert.Equal(ProposalDifferenceState.None, two.Difference.State);
                Assert.Equal("one/newest", current.Difference.Summary?.DifferenceId);
            }
            else
            {
                Assert.Same(two, harness.Page.SelectedDocument);
                Assert.Same(two, Assert.Single(harness.Page.Documents.Items));
                AssertPages(two.Difference, "two", "word04");
                await ((IAsyncRelayCommand)two.Difference.ShowMoreWordsCommand).ExecuteAsync(null);
                await ((IAsyncRelayCommand)two.Difference.ShowMorePlacesCommand).ExecuteAsync(null);
                Assert.Equal(60, two.Difference.Words.Count);
                Assert.Equal(60, two.Difference.Places.Count);
            }
        }
        finally
        {
            release.TrySetResult();
            await stopping;
            await reading;
            await harness.Context.StopProjectWorkAsync();
        }
    }

    [UiThreadTheory]
    [InlineData("intent")]
    [InlineData("trial")]
    public async Task RefreshingAChangedEvidenceIdentityReplacesTheOldNavigation(string changed)
    {
        var harness = new Harness();
        await harness.OpenAsync();
        var document = harness.Page.SelectedDocument!;
        await ReadOlderPagesAsync(document.Difference, "one", 3);
        if (changed == "intent") harness.Intent = "changed";
        else harness.TrialId = "another-trial";

        await document.RefreshAsync();

        Assert.Equal("one/newest", document.Difference.SelectedSaved?.DifferenceId);
        Assert.Equal(25, document.Difference.Words.Count);
        Assert.Empty(document.Difference.Places);
        Assert.Null(document.Difference.SelectedWord);
        await harness.Context.StopProjectWorkAsync();
    }

    [UiThreadFact]
    public async Task ReturningThroughThePageAndRefreshingStandingsRetainsEachDocumentsSavedDifferenceAndPages()
    {
        var harness = new Harness();
        await harness.OpenAsync();
        var one = harness.Page.SelectedDocument!;
        await ReadOlderPagesAsync(one.Difference, "one", 3);
        harness.Page.SelectedProposal = harness.Page.Groups.SelectMany(group => group.Cards)
            .Single(card => card.ProposalId == "two");
        await harness.Page.DetailReading;
        var two = harness.Page.SelectedDocument!;
        await ReadOlderPagesAsync(two.Difference, "two", 4);
        var reads = harness.Fake.ProposalDifferenceRequests.Count;

        harness.Page.SelectDocumentCommand.Execute(one);
        await harness.Page.DetailReading;
        Assert.Same(one, harness.Page.SelectedDocument);
        AssertPages(one.Difference, "one", "word03");
        AssertPages(two.Difference, "two", "word04");
        Assert.Equal(reads, harness.Fake.ProposalDifferenceRequests.Count);

        await harness.Context.PublishBaselineCapturedAsync(openFirstRunSetup: false);
        await Task.WhenAll(harness.Page.Documents.Items.Select(document => document.Reading));
        await harness.Page.DetailReading;
        AssertPages(one.Difference, "one", "word03");
        AssertPages(two.Difference, "two", "word04");
        Assert.True(harness.Fake.ProposalDifferenceRequests.Count > reads);
        await harness.Context.StopProjectWorkAsync();
    }

    [UiThreadFact]
    public async Task AnAbsentDifferenceMadeElsewhereAppearsOnRefresh()
    {
        var harness = new Harness { Absent = true };
        await harness.OpenAsync();
        var document = harness.Page.SelectedDocument!;
        Assert.Equal(ProposalDifferenceState.Absent, document.Difference.State);

        harness.Absent = false;
        await document.RefreshAsync();

        Assert.Equal(ProposalDifferenceState.Shown, document.Difference.State);
        Assert.Equal("one/newest", document.Difference.Summary?.DifferenceId);
        await harness.Context.StopProjectWorkAsync();
    }

    [UiThreadFact]
    public async Task ASavedListGainsANewerEntryOnRefreshWhileTheChosenOneAndItsPagesStay()
    {
        var harness = new Harness();
        await harness.OpenAsync();
        var document = harness.Page.SelectedDocument!;
        await ReadOlderPagesAsync(document.Difference, "one", 3);
        harness.Extra.Add("one/newer");
        var reads = harness.Fake.ProposalDifferenceRequests.Count;

        harness.Page.SelectDocumentCommand.Execute(document);
        await harness.Page.DetailReading;
        Assert.Equal(reads, harness.Fake.ProposalDifferenceRequests.Count);
        await document.RefreshAsync();

        Assert.Contains(document.Difference.SavedChoices, choice => choice.DifferenceId == "one/newer");
        AssertPages(document.Difference, "one", "word03");
        await harness.Context.StopProjectWorkAsync();
    }

    [UiThreadTheory]
    [InlineData("offer")]
    [InlineData("baseline")]
    public async Task ARefreshWithAChangedOfferOrBaselineReplacesTheOldNavigation(string changed)
    {
        static WorkspaceBaseline BaselineOf(char digest) => new(true, "", "", "", "", null)
        {
            Token = new Contract.Baselines.BaselineToken("project-1", "sha256:" + new string(digest, 64), "1",
                "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64)),
        };
        var harness = new Harness();
        await harness.OpenAsync();
        var document = harness.Page.SelectedDocument!;
        harness.Context.Baseline = BaselineOf('a');
        await document.RefreshAsync();
        await ReadOlderPagesAsync(document.Difference, "one", 3);
        if (changed == "offer") harness.Status = "deferred";
        else harness.Context.Baseline = BaselineOf('c');

        await document.RefreshAsync();

        Assert.Equal("one/newest", document.Difference.SelectedSaved?.DifferenceId);
        Assert.Equal(25, document.Difference.Words.Count);
        Assert.Empty(document.Difference.Places);
        await harness.Context.StopProjectWorkAsync();
    }

    private static async Task ReadOlderPagesAsync(ProposalDifferenceViewModel difference, string proposal, int word)
    {
        difference.SelectedSaved = difference.SavedChoices.Single(choice => choice.DifferenceId == proposal + "/older");
        await Wait.UntilAsync(() => !difference.IsReadingMore, "the saved Difference read to finish");
        await ((IAsyncRelayCommand)difference.ShowMoreWordsCommand).ExecuteAsync(null);
        difference.Words[word].ChooseCommand.Execute(null);
        await Wait.UntilAsync(() => !difference.IsReadingMore, "the first occurrence page to finish");
        await ((IAsyncRelayCommand)difference.ShowMorePlacesCommand).ExecuteAsync(null);
        AssertPages(difference, proposal, "word" + word.ToString("D2"));
    }

    private static void AssertPages(ProposalDifferenceViewModel difference, string proposal, string word)
    {
        Assert.Equal(proposal + "/older", difference.SelectedSaved?.DifferenceId);
        Assert.Equal(proposal + "/older", difference.Summary?.DifferenceId);
        Assert.Equal(50, difference.Words.Count);
        Assert.Equal(word, difference.SelectedWord?.Word);
        Assert.Equal(50, difference.Places.Count);
        Assert.True(difference.CanShowMoreWords);
        Assert.True(difference.CanShowMorePlaces);
    }

    private sealed class Harness
    {
        public FakeCommandClient Fake { get; } = new();
        public WorkspaceContext Context { get; }
        public ProposalsPageModel Page { get; }
        public ProposalDifferenceFixture Fixture { get; } = new(60) { PlacesPerWord = 60 };
        public string Intent { get; set; } = "intent";
        public string TrialId { get; set; } = "trial";
        public string Status { get; set; } = "proposed";
        public bool Absent { get; set; }
        public List<string> Extra { get; } = [];

        public Harness()
        {
            ProposalCard Card(string id) => new(id, id, "proposed", ProposalGroup.WaitingForYou,
                false, [], TrialStanding.Current, null, []);
            Fake.ProposalStandingsCompleteWith(new([Card("one"), Card("two")], 0));
            Fake.ShowProposalHandler = (request, _) => Task.FromResult(CommandOutcome<ProposalDetailProjection>.Success(
                new(request.ProposalId, Status, request.ProposalId, null, Intent, [])));
            Fake.ProposalReviewHandler = (request, _) => Task.FromResult(CommandOutcome<ProposalReviewResponse>.Success(
                new(request.ProposalId!, Intent, Intent, [], new TrialRecord(TrialId, request.ProposalId!, Intent,
                    WorkspaceContextTests.Assessment().Baseline.Token, null, new Dictionary<string, string>(), [],
                    true, null, JobStatus.Completed, null, TrialFreshness.Current), null, [], 0)));
            Fake.OnProposalDifference(ReadAsync);
            Context = WorkspaceContextTests.NewContext(Fake);
            Page = new ProposalsPageModel(Context);
        }

        public async Task OpenAsync()
        {
            await Context.OpenProjectAsync("project.fwdata");
            await Page.DetailReading;
        }

        private async Task<CommandOutcome<ProposalDifferenceResponse>> ReadAsync(
            ProposalDifferenceShowRequest request, CancellationToken cancellationToken)
        {
            if (Absent && request.DifferenceId is null)
                return CommandOutcome<ProposalDifferenceResponse>.Refused(new Refusal(
                    RefusalCodes.DifferenceNone, FailureReason.Refused, "none"));
            var read = await Fixture.Read(request, cancellationToken);
            var response = read.Value!;
            var saved = Extra.Select(id => id.Split('/')[1]).Concat(new[] { "newest", "older" }).Select(age => new ProposalDifferenceSaved(
                request.ProposalId + "/" + age, "text/one", "Text", ProposalDifferenceReferences.Human,
                "2026-10-31T00:00:00Z", true)).ToArray();
            return CommandOutcome<ProposalDifferenceResponse>.Success(response with
            {
                Summary = response.Summary with
                {
                    DifferenceId = request.DifferenceId ?? request.ProposalId + "/newest",
                    TrialJobId = TrialId,
                },
                Saved = saved,
                SavedTotal = saved.Length,
            });
        }
    }
}
