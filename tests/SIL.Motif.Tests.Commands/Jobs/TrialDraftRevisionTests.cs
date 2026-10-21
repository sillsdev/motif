using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Host.PanGloss;
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
    public void ATrialRecordsTheParserItsEnqueuerExpects()
    {
        var id = NewDraftWithOneGloss("pinned-trial");
        var parser = FakeParser.CopyReportingVersion(
            Path.Combine(Path.GetTempPath(), "motif-pin-" + Guid.NewGuid().ToString("N")), "0.8.8");

        var queued = JobCommands.EnqueueTrial(new EnqueueTrialRequest(_fwDataPath, ProductVersion, id,
            Words: ["word"]), parser);

        Assert.True(queued.Succeeded, queued.Refusal?.Message);
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        var inputJson = new JobRepository(database).Get(queued.Value!.JobId)!.InputJson;
        var expected = ParserIdentity.Read(parser)!;
        Assert.Equal(expected.Key, JobParserPin.KeyOf(inputJson));
        using var document = System.Text.Json.JsonDocument.Parse(inputJson);
        var pin = document.RootElement.GetProperty("parserIdentity");
        Assert.Equal("0.8.8", pin.GetProperty("version").GetString());
        Assert.Equal(expected.Sha256, pin.GetProperty("sha256").GetString());
        Assert.Equal(parser, pin.GetProperty("path").GetString());
        Assert.Equal(["word"], TrialJobInput.Parse(inputJson).Words);
    }

    [Fact]
    public void ATrialQueuedWithNoParserNamedIsPinnedToNone()
    {
        var id = NewDraftWithOneGloss("unpinned-trial");

        var queued = JobCommands.EnqueueTrial(new EnqueueTrialRequest(_fwDataPath, ProductVersion, id,
            Words: ["word"]));

        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        Assert.Null(JobParserPin.KeyOf(new JobRepository(database).Get(queued.Value!.JobId)!.InputJson));
    }

    [Fact]
    public void AParserThatCannotBeIdentifiedRefusesTheTrialInsteadOfLeavingItUnpinned()
    {
        var id = NewDraftWithOneGloss("unidentifiable-trial");
        var before = CountTrialJobs();
        var broken = Path.Combine(Path.GetTempPath(), "motif-broken-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(broken, "not an executable");

        var queued = JobCommands.EnqueueTrial(new EnqueueTrialRequest(_fwDataPath, ProductVersion, id,
            Words: ["word"]), broken);

        Assert.False(queued.Succeeded);
        Assert.Equal("job.parser-unidentifiable", queued.Refusal!.Code);
        Assert.Equal(before, CountTrialJobs());
    }

    [Fact]
    public void AJobFailedBecauseItsParserIsGoneShowsItsCodeAndWaitReturnsItAtOnce()
    {
        var id = NewDraftWithOneGloss("stranded-trial");
        var parser = FakeParser.CopyReportingVersion(
            Path.Combine(Path.GetTempPath(), "motif-stranded-" + Guid.NewGuid().ToString("N")), "0.8.8");
        var queued = JobCommands.EnqueueTrial(new EnqueueTrialRequest(_fwDataPath, ProductVersion, id,
            Words: ["word"]), parser);
        using (var database = ProjectMotifDatabase.Open(_fwDataPath))
        {
            var jobs = new JobRepository(database);
            var job = jobs.Get(queued.Value!.JobId)!;
            jobs.Transition(job.JobId, SIL.Motif.Contract.Jobs.JobStatus.Failed, job.Version,
                SIL.Motif.Contract.Jobs.JobFailureCategory.Infrastructure,
                JobParserPin.UnavailableResult(JobParserPin.Read(job.InputJson)!));
        }

        var started = System.Diagnostics.Stopwatch.StartNew();
        var waited = JobCommands.WaitForJob(new WaitForJobRequest(_fwDataPath, queued.Value!.JobId,
            ProductVersion, TimeSpan.FromSeconds(60)));

        Assert.True(started.Elapsed < TimeSpan.FromSeconds(10), "the wait took " + started.Elapsed);
        Assert.True(waited.Succeeded, waited.Refusal?.Message);
        Assert.Equal(SIL.Motif.Contract.Jobs.JobStatus.Failed, waited.Value!.Status);
        Assert.Equal("job.parser-unavailable", waited.Value.FailureCode);
        Assert.Contains("Run the Trial again", waited.Value.FailureDetail);
        Assert.Contains("0.8.8", waited.Value.FailureDetail);
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
