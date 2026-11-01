using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using System.Text.Json;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

/// <summary>
/// A Proposal that was readable when it was tried and has since become unreadable, met through the real commands:
/// the window shows why in its own words and every page stays usable.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "Integration")]
public sealed class UnreadableProposalRealClientTests(PristineProjectFixture pristine)
{
    private const string CouldNotRead =
        "Motif could not read these changes, so it did not use them. Nothing was changed or deleted.";

    [Fact]
    public async Task OpeningAnUnreadableProposalShowsWhyAndLeavesTheWindowUsable()
    {
        using var project = new WalkthroughProject(pristine);
        var unreadable = SeedUnreadableDraft(project.FwDataPath, "became-unreadable");
        var readable = CreateReadableProposal(project.FwDataPath, "still-readable");
        var context = WorkspaceContextTests.NewContext(RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath));
        await context.OpenProjectAsync(project.FwDataPath);

        await context.OpenProposalAsync(unreadable, CancellationToken.None);

        Assert.NotNull(context.Lens);
        Assert.Null(context.Lens.Trial);
        Assert.Equal(RefusalCodes.ProposalUnreadable, context.Lens.ReadRefusal?.Code);
        Assert.Equal(CouldNotRead, WindowRefusal.From(context.Lens.ReadRefusal!).Sentence);

        await context.LeaveProposalAsync();
        Assert.Null(context.Lens);
        await context.OpenProposalAsync(readable, CancellationToken.None);
        Assert.Equal(readable, context.Lens?.ProposalId);
        Assert.Null(context.Lens?.ReadRefusal);
    }

    [Fact]
    public async Task TryingAProposalThatBecameUnreadableWhileOpenIsRefusedAndTheLensSaysWhy()
    {
        using var project = new WalkthroughProject(pristine);
        var id = SeedUnreadableDraft(project.FwDataPath, "try-unreadable", corrupt: false);
        var context = WorkspaceContextTests.NewContext(RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath));
        await context.OpenProjectAsync(project.FwDataPath);
        await context.OpenProposalAsync(id, CancellationToken.None);
        Assert.Null(context.Lens?.ReadRefusal);
        CorruptDraft(project.FwDataPath, "try-unreadable");

        var outcome = await context.TryAgainAsync(id, CancellationToken.None);

        Assert.Equal(RefusalCodes.ProposalUnreadable, outcome.Refusal?.Code);
        Assert.Equal(RefusalCodes.ProposalUnreadable, context.Lens?.ReadRefusal?.Code);
        Assert.Equal(CouldNotRead, WindowRefusal.From(context.Lens!.ReadRefusal!).Sentence);
    }

    [Fact]
    public async Task QueueingAndComparingAnUnreadableProposalAreRefusedInTheWindowsWords()
    {
        using var project = new WalkthroughProject(pristine);
        var unreadable = SeedUnreadableDraft(project.FwDataPath, "compare-unreadable");
        var client = RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath);

        var queued = await client.EnqueueTrialAsync(new EnqueueTrialRequest(project.FwDataPath,
            MotifProductVersion.CurrentText, unreadable, Words: ["motifa"]), CancellationToken.None);
        var compared = await client.ProduceProposalDifferenceAsync(new ProposalDifferenceProduceRequest(
            project.FwDataPath, unreadable, project.TextId.ToString("D")), null, CancellationToken.None);

        foreach (var refusal in new[] { queued.Refusal, compared.Refusal })
        {
            Assert.Equal(RefusalCodes.ProposalUnreadable, refusal?.Code);
            Assert.Equal(CouldNotRead, WindowRefusal.From(refusal!).Sentence);
        }
        using var database = ProjectMotifDatabase.Open(project.FwDataPath);
        Assert.Single(new JobRepository(database).ListByProjectAndKind(
            ProjectWorkspaceKey.Compute(new ProjectLocator(project.FwDataPath,
                Path.GetFileNameWithoutExtension(project.FwDataPath))), TrialJobHandler.TrialKind));
    }

    [Fact]
    public async Task ApplyingAnUnreadableProposalIsRefusedAndNothingIsApplied()
    {
        using var project = new WalkthroughProject(pristine);
        var unreadable = SeedUnreadableDraft(project.FwDataPath, "apply-unreadable");
        var client = RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath);

        var applied = await client.ApplyProposalAsync(
            new ApplyProposalRequest(project.FwDataPath, unreadable, null), CancellationToken.None);

        Assert.Equal(RefusalCodes.ProposalUnreadable, applied.Refusal?.Code);
        Assert.Equal(CouldNotRead, WindowRefusal.From(applied.Refusal!).Sentence);
    }

    private string CreateReadableProposal(string projectPath, string draftName)
    {
        var created = ProposalCommands.New(new NewDraftRequest(projectPath, MotifProductVersion.CurrentText,
            draftName, draftName));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        var gloss = ProposalCommands.AddSetGloss(new AddSetGlossRequest(projectPath, MotifProductVersion.CurrentText,
            draftName, CanonicalId.FromGuid(pristine.Seed.FirstSenseId).Value, "en", "unreadable test gloss"));
        Assert.True(gloss.Succeeded, gloss.Refusal?.Message);
        DraftRationale.Author(projectPath, draftName, draftName, "Why this unreadable test gloss is right.");
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(projectPath, MotifProductVersion.CurrentText,
            draftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        return finalized.Value!.ProposalId;
    }

    // A Draft with a Trial recorded while readable, then rewritten with an operation kind the product lacks.
    private string SeedUnreadableDraft(string projectPath, string draftName, bool corrupt = true)
    {
        var created = ProposalCommands.New(new NewDraftRequest(projectPath, MotifProductVersion.CurrentText,
            draftName, draftName));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        var gloss = ProposalCommands.AddSetGloss(new AddSetGlossRequest(projectPath, MotifProductVersion.CurrentText,
            draftName, CanonicalId.FromGuid(pristine.Seed.FirstSenseId).Value, "en", "unreadable test gloss"));
        Assert.True(gloss.Succeeded, gloss.Refusal?.Message);
        var fullPath = Path.GetFullPath(projectPath);
        var locator = new ProjectLocator(fullPath, Path.GetFileNameWithoutExtension(fullPath));
        using var database = ProjectMotifDatabase.Open(projectPath);
        var proposals = new ProposalRepository(database);
        var draft = proposals.GetDraft(draftName);
        var input = JsonSerializer.Serialize(new TrialJobInput(JobCommands.CanonicalProposalJson(draft), null,
            ["motifa"]), MotifJson.CreateOptions());
        var jobs = new JobRepository(database);
        var queued = jobs.Create("job/seeded-" + draftName, ProjectWorkspaceKey.Compute(locator),
            TrialJobHandler.TrialKind, input, DateTimeOffset.UtcNow.ToString("O"));
        jobs.Transition(queued, JobStatus.Running);
        if (corrupt) CorruptDraft(projectPath, draftName);
        return draft.ProposalId.Value;
    }

    private static void CorruptDraft(string projectPath, string draftName)
    {
        using var database = ProjectMotifDatabase.Open(projectPath);
        var proposals = new ProposalRepository(database);
        var draft = proposals.GetDraft(draftName);
        Assert.True(proposals.TrySaveDraft(draftName, draft.ProposalJson!,
            draft.ProposalJson!.Replace("lexical/lexSense/setGloss", "lexical/lexSense/nope", StringComparison.Ordinal)));
    }
}
