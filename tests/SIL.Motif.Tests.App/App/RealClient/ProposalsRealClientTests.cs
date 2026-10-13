using System.Text.Json;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "Integration")]
public sealed class ProposalsRealClientTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task ReopeningADeclinedProposalPersistsAsADraftForANewClient()
    {
        using var project = new WalkthroughProject(pristine);
        var proposalId = CreateDeclinedProposal(project.FwDataPath, "reopen-from-page");
        var firstPage = await OpenPageAsync(RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath),
            project.FwDataPath);

        await firstPage.Page.ShowSetAsideCommand.ExecuteAsync(null);
        var declined = Assert.Single(firstPage.Page.Groups.Single(group => group.Group == ProposalGroup.SetAside).Cards);
        Assert.True(declined.CanReopen);

        await firstPage.Page.ReopenCommand.ExecuteAsync(declined);

        var reopened = Assert.Single(firstPage.Page.Groups.SelectMany(group => group.Cards),
            card => card.ProposalId == proposalId);
        Assert.True(reopened.Card.IsDraft, "The Proposals page reread did not show the reopened Proposal as a Draft.");
        // A reopened Declined Proposal stays declined, with its Draft attached, until that Draft is Finalized.
        Assert.Equal("rejected", reopened.Card.Status);

        var secondPage = await OpenPageAsync(RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath),
            project.FwDataPath);
        var persisted = Assert.Single(secondPage.Page.Groups.SelectMany(group => group.Cards),
            card => card.ProposalId == proposalId);

        Assert.True(persisted.Card.IsDraft,
            "Reopening from the Proposals page did not persist as a Draft for a new client.");
        Assert.Equal("rejected", persisted.Card.Status);
    }

    [Fact]
    public async Task DeletingAClosedProposalRemovesItAndItsTrialsFromTheStore()
    {
        using var project = new WalkthroughProject(pristine);
        var proposalId = CreateDeclinedProposal(project.FwDataPath, "delete-from-page");
        const string trialJobId = "job/proposals-page-delete";
        const string trialAssessmentId = "assessment/proposals-page-delete";
        SeedTrial(project.FwDataPath, proposalId, trialJobId, trialAssessmentId);
        using (var database = ProjectMotifDatabase.Open(project.FwDataPath))
            Assert.NotNull(new JobRepository(database).Get(trialJobId));

        var page = await OpenPageAsync(RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath),
            project.FwDataPath);
        await page.Page.ShowSetAsideCommand.ExecuteAsync(null);
        var declined = Assert.Single(page.Page.Groups.Single(group => group.Group == ProposalGroup.SetAside).Cards);
        Assert.True(declined.CanDelete);
        Assert.NotNull(declined.Card.Trial);

        await page.Page.DeleteCommand.ExecuteAsync(declined);

        Assert.DoesNotContain(page.Page.Groups.SelectMany(group => group.Cards),
            card => card.ProposalId == proposalId);
        using (var database = ProjectMotifDatabase.Open(project.FwDataPath))
        {
            Assert.Null(new JobRepository(database).Get(trialJobId));
            Assert.Throws<KeyNotFoundException>(() => new AssessmentRepository(database).Get(trialAssessmentId));
            Assert.Throws<KeyNotFoundException>(() => new ProposalRepository(database).Get(CanonicalId.Parse(proposalId)));
        }

        var reread = await OpenPageAsync(RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath),
            project.FwDataPath);
        Assert.DoesNotContain(reread.Page.Groups.SelectMany(group => group.Cards),
            card => card.ProposalId == proposalId);
    }

    [Fact]
    public async Task AnAppliedProposalOffersNoDeleteFromTheProposalsPage()
    {
        using var project = new WalkthroughProject(pristine);
        var proposalId = CreateFinalizedProposal(project.FwDataPath, "applied-on-page");
        using (var database = ProjectMotifDatabase.Open(project.FwDataPath))
            new ProposalRepository(database).SetStatus(CanonicalId.Parse(proposalId), "applied", null);

        var page = await OpenPageAsync(RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath),
            project.FwDataPath);
        await page.Page.ShowSetAsideCommand.ExecuteAsync(null);
        var applied = Assert.Single(page.Page.Groups.Single(group => group.Group == ProposalGroup.SetAside).Cards);

        Assert.Equal("applied", applied.Card.Status);
        Assert.False(applied.CanDelete);
    }

    private static async Task<(WorkspaceContext Context, ProposalsPageModel Page)> OpenPageAsync(
        ICommandClient client, string projectPath)
    {
        var context = WorkspaceContextTests.NewContext(client);
        var page = new ProposalsPageModel(context);
        await context.OpenProjectAsync(projectPath);
        return (context, page);
    }

    private string CreateDeclinedProposal(string projectPath, string draftName)
    {
        var proposalId = CreateFinalizedProposal(projectPath, draftName);
        var declined = ProposalLifecycleCommand.Decline(new DeclineProposalRequest(projectPath, proposalId));
        Assert.True(declined.Succeeded, declined.Refusal?.Message);
        return proposalId;
    }

    private string CreateFinalizedProposal(string projectPath, string draftName)
    {
        var created = ProposalCommands.New(new NewDraftRequest(projectPath, MotifProductVersion.CurrentText,
            draftName, draftName));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        var gloss = ProposalCommands.AddSetGloss(new AddSetGlossRequest(projectPath, MotifProductVersion.CurrentText,
            draftName, CanonicalId.FromGuid(pristine.Seed.FirstSenseId).Value, "en", "page test gloss"));
        Assert.True(gloss.Succeeded, gloss.Refusal?.Message);
        DraftRationale.Author(projectPath, draftName, draftName, "Why this page test gloss is right.");
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(projectPath, MotifProductVersion.CurrentText,
            draftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        return finalized.Value!.ProposalId;
    }

    private static void SeedTrial(string projectPath, string proposalId, string jobId, string assessmentId)
    {
        var fullPath = Path.GetFullPath(projectPath);
        var locator = new ProjectLocator(fullPath, Path.GetFileNameWithoutExtension(fullPath));
        using var database = ProjectMotifDatabase.Open(projectPath);
        var proposal = new ProposalRepository(database).Get(CanonicalId.Parse(proposalId));
        var baselineToken = new BaselineToken("project-id", "sha256:" + new string('1', 64), "projection-v1",
            "2026-09-24T10:00:00Z", "sha256:" + new string('a', 64));
        new AssessmentRepository(database).Record(new NewAssessmentRecord(assessmentId, proposal.ProposalId,
            proposal.IntentDigest, "pangloss", AssessmentKinds.ParseTime, "{}", "sha256:scope", "whitespace", "1",
            JsonSerializer.Serialize(baselineToken, MotifJson.CreateOptions()), Selection.Create("Default", ["motifa"]),
            "sha256:outcome", "sha256:semantic", "sha256:grammar", "fingerprint", "pipeline", 0,
            [new AssessedWord("motifa", "analysed", [])], SavedUtc: "2026-09-24T11:00:00Z"));
        var input = JsonSerializer.Serialize(new TrialJobInput(proposal.ProposalJson!, null, AllWords: true),
            MotifJson.CreateOptions());
        var jobs = new JobRepository(database);
        var queued = jobs.Create(jobId, ProjectWorkspaceKey.Compute(locator), TrialJobHandler.TrialKind, input,
            DateTimeOffset.UtcNow.ToString("O"));
        var running = jobs.Transition(queued, JobStatus.Running);
        var completion = JsonSerializer.Serialize(new
        {
            baselineToken,
            assessmentIds = new[] { assessmentId },
            allWords = true,
        }, MotifJson.CreateOptions());
        jobs.Transition(running.JobId, JobStatus.Completed, running.Version, completion);
    }
}
