using System.Text.Json;
using System.Text.RegularExpressions;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

/// <summary>
/// A Proposal with a completed Trial, read through the product's command client: the current saved values, the applied
/// record, and a Rationale query shown at the Baseline and at the Proposal.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "Integration")]
public sealed class ProposalQueryEndToEndTests(PristineProjectFixture pristine)
{
    private const string Query = "```motif-query\nid: alternates\ntitle: Alternate forms\n" +
        "run: motif query forms --where kind=alternate --sort baseline.form\n```\n\n```motif-show\nquery: alternates\nshow: table\n```\n\n" +
        "```motif-query\nid: stems\ntitle: Stems\nrun: motif query forms --where kind=lexeme\n```\n\n" +
        "```motif-show\nquery: stems\nshow: table\n```";

    [UiThreadFact]
    public async Task AProposalWithACompletedTrialShowsItsCurrentValuesAndBothSidesOfItsQuery()
    {
        var path = GroupCorpusFixture.Create(pristine);
        var root = Path.Combine(Path.GetDirectoryName(path)!, "proposal-query-managed");
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        var senseId = CanonicalId.FromGuid(Guid.Parse(
            Regex.Match(File.ReadAllText(path), "<rt class=\"LexSense\" guid=\"([0-9a-fA-F-]+)\"").Groups[1].Value)).Value;
        var proposalId = Finalized(path, "query-sides", senseId);
        var jobId = "trial/query-" + Guid.NewGuid().ToString("N");
        var scratch = TrialScratchPath.ForJob(jobId);
        try
        {
            SeedTrial(path, proposalId, jobId, scratch);
            var client = RealCommandClient.Create(root);
            var context = WorkspaceContextTests.NewContext(client);
            await context.OpenProjectAsync(path);
            var documents = new OpenProposalDocuments(context);
            try
            {
                var document = documents.Open(proposalId);
                await document.RefreshAsync();
                await document.Queries.Running;

                var operation = Assert.Single(document.Detail!.Operations);
                var current = Assert.Single(operation.Current!);
                Assert.NotEmpty(current.Value.Values);
                Assert.DoesNotContain("eat (demo)", current.Value.Values);
                Assert.Null(document.Detail.AppliedRecord);
                Assert.Contains("motif-query", document.RationaleEditor!.Markdown);
                Assert.StartsWith("Sense 1 of", Assert.Single(document.OperationRows).Target, StringComparison.Ordinal);

                Assert.Equal(["alternates", "stems"], document.Queries.Blocks.Select(item => item.Id));
                Assert.All(document.Queries.Blocks, item => Assert.True(item.Notice.Length == 0, item.Notice));
                var block = document.Queries.Blocks[0];
                var display = Assert.Single(block.Displays);
                Assert.True(display.ShowsProposal);
                Assert.False(display.HasProposalPending);
                var kaonan = Assert.Single(display.Rows, row => row.BaselineWord == "kaonan");
                Assert.True(kaonan.ProposalIsWord);
                Assert.Contains("kaonan", display.Rows.Select(row => row.ProposalWord));

                // baha is only in the Trial copy: a Proposal-only row shows that side did not fall back to the Baseline.
                var stems = Assert.Single(document.Queries.Blocks[1].Displays);
                Assert.False(stems.HasProposalPending);
                var baha = Assert.Single(stems.Rows, row => row.ProposalWord == "baha");
                Assert.NotEqual("baha", baha.BaselineWord);
                Assert.DoesNotContain(stems.Rows, row => row.BaselineWord == "baha");
            }
            finally
            {
                await documents.StopAsync();
                await context.StopProjectWorkAsync();
            }
        }
        finally
        {
            if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
        }
    }

    [UiThreadFact]
    public async Task AProposalWithNoTrialShowsItsSideAsPendingAtTheBaseline()
    {
        var path = GroupCorpusFixture.Create(pristine);
        var root = Path.Combine(Path.GetDirectoryName(path)!, "proposal-query-pending");
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        var senseId = CanonicalId.FromGuid(Guid.Parse(
            Regex.Match(File.ReadAllText(path), "<rt class=\"LexSense\" guid=\"([0-9a-fA-F-]+)\"").Groups[1].Value)).Value;
        var proposalId = Finalized(path, "query-pending", senseId);
        var context = WorkspaceContextTests.NewContext(RealCommandClient.Create(root));
        await context.OpenProjectAsync(path);
        var documents = new OpenProposalDocuments(context);
        try
        {
            var document = documents.Open(proposalId);
            await document.RefreshAsync();
            await document.Queries.Running;

            var display = Assert.Single(document.Queries.Blocks[0].Displays);
            Assert.True(display.HasProposalPending);
            Assert.Contains(display.Rows, row => row.BaselineWord == "kaonan");
        }
        finally
        {
            await documents.StopAsync();
            await context.StopProjectWorkAsync();
        }
    }

    private string Finalized(string path, string draft, string senseId)
    {
        Assert.True(ProposalCommands.New(new NewDraftRequest(path, MotifProductVersion.CurrentText, draft, draft)).Succeeded);
        var gloss = ProposalCommands.AddSetGloss(new AddSetGlossRequest(path, MotifProductVersion.CurrentText, draft, senseId, "en", "eat (demo)"));
        Assert.True(gloss.Succeeded, gloss.Refusal?.Message);
        DraftRationale.Author(path, draft, draft, "Why the gloss changes.");
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(path, MotifProductVersion.CurrentText, draft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        // The query blocks are written straight to the store, as a hand-edited text would carry them.
        using var database = ProjectMotifDatabase.Open(path);
        var repository = new ProposalRepository(database);
        var record = repository.Get(CanonicalId.Parse(finalized.Value!.ProposalId));
        Assert.True(repository.SetRationale(record.ProposalId, "Why the gloss changes.\n\n" + Query, new DiscussionAuthor(null, ""),
            new RationaleWriteExpectation(record.Status, record.DraftName, record.Rationale)));
        return finalized.Value.ProposalId;
    }

    // A completed Trial: its job, one parse Assessment, and a copy of the Baseline that gained the stem baha.
    private static void SeedTrial(string projectPath, string proposalId, string jobId, string scratch)
    {
        var locator = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        using var database = ProjectMotifDatabase.Open(projectPath);
        var proposal = new ProposalRepository(database).Get(CanonicalId.Parse(proposalId));
        var baselineToken = new BaselineToken("project-id", "sha256:" + new string('1', 64), "projection-v1",
            "2026-09-24T10:00:00Z", "sha256:" + new string('a', 64));
        var assessmentId = "assessment/query-" + Guid.NewGuid().ToString("N");
        new AssessmentRepository(database).Record(new NewAssessmentRecord(assessmentId, proposal.ProposalId,
            proposal.IntentDigest, "pangloss", AssessmentKinds.ParseTime, "{}", "sha256:scope",
            new AssessmentSubject.Words(Selection.Create("Default", ["kaonan"]), "whitespace", "1"),
            JsonSerializer.Serialize(baselineToken, MotifJson.CreateOptions()), "sha256:outcome", "sha256:semantic",
            "sha256:grammar", "fingerprint", "pipeline", 0,
            new AssessmentRecordMaterial.Words([new AssessedWord("kaonan", "analysed", [])], null, null, []),
            "2026-09-24T11:00:00Z"));
        var jobs = new JobRepository(database);
        var queued = jobs.Create(jobId, ProjectWorkspaceKey.Compute(locator), TrialJobHandler.TrialKind,
            JsonSerializer.Serialize(new TrialJobInput(proposal.ProposalJson!, null, AllWords: true), MotifJson.CreateOptions()),
            DateTimeOffset.UtcNow.ToString("O"));
        var running = jobs.Transition(queued, JobStatus.Running);
        jobs.Transition(running.JobId, JobStatus.Completed, running.Version, JsonSerializer.Serialize(new
        {
            baselineToken,
            assessmentIds = new[] { assessmentId },
            allWords = true,
        }, MotifJson.CreateOptions()));
        var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(locator))!;
        CopyDirectory(Path.GetDirectoryName(baseline.FwDataPath)!, scratch);
        GroupCorpusFixture.AddBahaStem(Path.Combine(scratch, Path.GetFileName(baseline.FwDataPath)));
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
        foreach (var directory in Directory.GetDirectories(from))
            CopyDirectory(directory, Path.Combine(to, Path.GetFileName(directory)));
    }
}
