using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Contract.Parsing;
using Xunit;

namespace SIL.Motif.Tests.Jobs;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class TrialDraftRevisionTests
{
    private const string ProductVersion = "1.0";
    private readonly string _fwDataPath;

    public TrialDraftRevisionTests(PristineProjectFixture pristine)
    {
        using var scratch = pristine.NewScratch();
        _fwDataPath = scratch.ProjectId.Path;
    }

    [Fact]
    public void ATrialOfADraftThatChangedSinceTheCallerReadItQueuesNothing()
    {
        var draftId = NewDraftWithOneGloss("stale-trial");

        var queued = JobCommands.EnqueueTrial(new EnqueueTrialRequest(_fwDataPath, ProductVersion, draftId,
            Words: ["word"], ExpectedDraftRevision: "sha256:stale"));

        Assert.Equal("draft.revision-conflict", queued.Refusal?.Code);
        Assert.Equal(0L, CountTrialJobs());
    }

    [Fact]
    public void ATrialOfADraftAtTheExpectedRevisionIsQueued()
    {
        var draftId = NewDraftWithOneGloss("current-trial");
        string revision;
        using (var database = ProjectMotifDatabase.Open(_fwDataPath))
            revision = DraftRevision.Compute(new ProposalRepository(database).GetDraft("current-trial").ProposalJson);

        var queued = JobCommands.EnqueueTrial(new EnqueueTrialRequest(_fwDataPath, ProductVersion, draftId,
            Words: ["word"], ExpectedDraftRevision: revision));

        Assert.True(queued.Succeeded, queued.Refusal?.Message);
        Assert.Equal(1L, CountTrialJobs());
    }

    [Fact]
    public void DryRunAndTrialFreezeTheDraftContentBeforeFurtherEdits()
    {
        var id = NewDraftWithOneGloss("frozen");
        var dryRun = JobCommands.EnqueueDryRun(new EnqueueDryRunRequest(_fwDataPath, ProductVersion, id));
        var trial = JobCommands.EnqueueTrial(new EnqueueTrialRequest(_fwDataPath, ProductVersion, id, Words: ["word"]));
        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);
        Assert.True(trial.Succeeded, trial.Refusal?.Message);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(_fwDataPath, ProductVersion,
            "frozen", CanonicalId.Mint().Value, "en", "another gloss")).Succeeded);
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        var jobs = new JobRepository(database);
        var frozenDryRun = DryRunJobInput.Parse(jobs.Get(dryRun.Value!.JobId)!.InputJson).Proposal.Validate();
        var frozenTrial = ProposalJsonParser.Parse(TrialJobInput.Parse(jobs.Get(trial.Value!.JobId)!.InputJson).ProposalJson);
        Assert.Single(frozenDryRun.Operations);
        Assert.Single(frozenTrial.Operations);
        Assert.Equal(2, ProposalJsonParser.Parse(JobCommands.CanonicalProposalJson(
            new ProposalRepository(database).GetDraft("frozen"))).Operations.Count);
    }

    [Fact]
    public void ADryRunQueuedAfterThePublishedBaselineWasMovedAsideBindsNoSourceAndWaitsForACapture()
    {
        var id = NewDraftWithOneGloss("moved-aside");
        var root = Path.Combine(Path.GetTempPath(), "motif-dryrun-root-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_fwDataPath), root).Succeeded);
            var bound = JobCommands.EnqueueDryRun(new EnqueueDryRunRequest(_fwDataPath, ProductVersion, id));
            Assert.True(bound.Succeeded, bound.Refusal?.Message);
            using var database = ProjectMotifDatabase.Open(_fwDataPath);
            var jobs = new JobRepository(database);
            Assert.NotNull(DryRunJobInput.Parse(jobs.Get(bound.Value!.JobId)!.InputJson).SourceBaseline);

            Directory.Delete(Path.Combine(root, "baselines"), recursive: true);
            var unbound = JobCommands.EnqueueDryRun(new EnqueueDryRunRequest(_fwDataPath, ProductVersion, id));

            Assert.True(unbound.Succeeded, unbound.Refusal?.Message);
            Assert.Null(DryRunJobInput.Parse(jobs.Get(unbound.Value!.JobId)!.InputJson).SourceBaseline);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void ATrialWithNoWordsNamedOfAGrammarChangeMeasuresTheDefaultSelection()
    {
        var id = NewDraftWithOneGloss("grammar-scope");

        var queued = JobCommands.EnqueueTrial(new EnqueueTrialRequest(_fwDataPath, ProductVersion, id));

        Assert.True(queued.Succeeded, queued.Refusal?.Message);
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        var input = TrialJobInput.Parse(new JobRepository(database).Get(queued.Value!.JobId)!.InputJson);
        Assert.Null(input.Words);
        Assert.False(input.AllWords);
    }

    [Fact]
    public void ATrialKeepsTheWordsItsCallerNamedEvenForAGrammarChange()
    {
        var id = NewDraftWithOneGloss("named-scope");

        var queued = JobCommands.EnqueueTrial(new EnqueueTrialRequest(_fwDataPath, ProductVersion, id,
            Words: ["word"]));

        Assert.True(queued.Succeeded, queued.Refusal?.Message);
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        Assert.Equal(["word"],
            TrialJobInput.Parse(new JobRepository(database).Get(queued.Value!.JobId)!.InputJson).Words);
    }

    [Fact]
    public void ATrialWhoseWordListOutgrowsAJobInputKeepsItBesideTheStore()
    {
        var id = NewDraftWithOneGloss("long-word-list");
        var words = Enumerable.Range(0, 120_000).Select(index => $"neno{index:D6}").ToArray();

        var queued = JobCommands.EnqueueTrial(new EnqueueTrialRequest(_fwDataPath, ProductVersion, id, Words: words));

        Assert.True(queued.Succeeded, queued.Refusal?.Message);
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        var input = TrialJobInput.Parse(new JobRepository(database).Get(queued.Value!.JobId)!.InputJson);
        Assert.Null(input.Words);
        var list = Assert.IsType<JobWordList>(input.WordList);
        Assert.Equal(words.Length, list.Count);
        Assert.Equal(words, JobWordLists.Read(database, list));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AnOversizedProposalIsRefusedWithSplitAdviceBeforeQueueing(bool trial, bool longWords)
    {
        var created = ProposalCommands.New(new(_fwDataPath, ProductVersion, "oversized", null));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        var added = ProposalCommands.AddSetGloss(new(_fwDataPath, ProductVersion, "oversized",
            CanonicalId.Mint().Value, "en", new string('x', 1024 * 1024)));
        Assert.True(added.Succeeded, added.Refusal?.Message);
        var words = longWords
            ? Enumerable.Range(0, 120_000).Select(index => $"neno{index:D6}").ToArray()
            : null;

        var queued = trial
            ? JobCommands.EnqueueTrial(new(_fwDataPath, ProductVersion, created.Value!.ProposalId, Words: words))
            : JobCommands.EnqueueDryRun(new(_fwDataPath, ProductVersion, created.Value!.ProposalId));

        Assert.False(queued.Succeeded);
        Assert.Equal("job.input-too-large", queued.Refusal!.Code);
        Assert.Contains("Split", queued.Refusal.Message);
        Assert.Contains("Proposal", queued.Refusal.Message);
        Assert.Equal(trial ? "trial" : "dry-run", queued.Refusal.Facts["kind"]);
        Assert.Equal("1048576", queued.Refusal.Facts["limitUtf8Bytes"]);
        Assert.True(int.Parse(queued.Refusal.Facts["inputUtf8Bytes"],
            System.Globalization.CultureInfo.InvariantCulture) > 1024 * 1024);
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        Assert.Empty(new JobRepository(database).ListActive());
        Assert.False(Directory.Exists(database.FullPath + ".job-inputs"));
    }

    private string NewDraftWithOneGloss(string draftName)
    {
        var created = ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, draftName, null));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        var added = ProposalCommands.AddSetGloss(new AddSetGlossRequest(
            _fwDataPath, ProductVersion, draftName, CanonicalId.Mint().Value, "en", "a gloss"));
        Assert.True(added.Succeeded, added.Refusal?.Message);
        return created.Value!.ProposalId;
    }

    private long CountTrialJobs()
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Jobs WHERE Kind = $kind;";
        command.Parameters.AddWithValue("$kind", JobCommands.TrialKind);
        return (long)command.ExecuteScalar()!;
    }
}
