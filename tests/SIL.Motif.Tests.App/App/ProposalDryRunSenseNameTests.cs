using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Model.Snapshot;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>The Dry Run sentence names the sense it changes, as "palit sense 2", when the Baseline holds it.</summary>
public sealed class ProposalDryRunSenseNameTests
{
    private static readonly string Sense = ProposalEntryContextTests.SenseTwo;

    private static async Task<(WorkspaceContext Context, OpenProposalDocuments Documents, ProposalDocumentViewModel Document)>
        OpenAsync(FakeCommandClient fake, params EffectView[] effects)
    {
        var dryRun = new DryRunProjection("one", "intent/one", "", effects, "effect", "footprint") { EvidenceCurrent = true };
        var trial = new TrialRecord("job/one", "one", "intent/one", WorkspaceContextTests.Assessment().Baseline.Token,
            dryRun, new Dictionary<string, string>(), [], false, null, JobStatus.Completed, DateTimeOffset.UtcNow,
            TrialFreshness.Current);
        fake.ReadTrialCompletesWith(trial);
        fake.Review = new ProposalReviewResponse("one", "intent/one", "revision/one",
            [new GrammarChangeEntry("entry", 0, "A change", ["op/one"], [], "content", false,
                "composer", "A change", [], [], [])], trial, new(true, false, false, null, [], false), [], 0);
        ProposalStatusFakes.GiveItASavedDifference(fake);
        fake.PendingChangesIs(new PendingChangesSnapshot("one", "revision/one", [], []));
        fake.ShowProposalHandler = (request, _) => Task.FromResult(CommandOutcome<ProposalDetailProjection>.Success(
            new(request.ProposalId, "proposed", "Clarify the gloss", null, "intent/one", [])));
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        var documents = new OpenProposalDocuments(context);
        var document = documents.Open("one");
        document.Card = new ProposalCardViewModel(new("one", "Clarify the gloss", "proposed",
            ProposalGroup.NotAppliedYet, false, [], TrialStanding.Current, null, []));
        await document.RefreshAsync();
        return (context, documents, document);
    }

    private static EffectView GlossOfSense(string before, string after) =>
        new(Sense, SnapshotFields.LexSenseGloss, [new EffectChange("en", before, after)]);

    [UiThreadFact]
    public async Task TheDryRunRowAndTheApplyConfirmationNameTheSense()
    {
        var fake = new FakeCommandClient();
        fake.LexicalEntries[Sense] = ProposalEntryContextTests.PalitAskedBySense(Sense);
        var (context, documents, document) = await OpenAsync(fake, GlossOfSense("buy", "buy (demo)"));
        try
        {
            Assert.Equal("Dry Run ✓ · Will change 1 field: palit sense 2: Gloss (en): buy → buy (demo)",
                document.DryRunCheckText);
            await document.PrepareApplyCommand.ExecuteAsync(null);
            Assert.Equal("Will change 1 field: palit sense 2: Gloss (en): buy → buy (demo)", document.ApplyWillChangeText);
            Assert.Contains(Sense, Assert.Single(fake.LexicalEntryRequests).Ids);
        }
        finally { await documents.StopAsync(); await context.StopProjectWorkAsync(); }
    }

    [UiThreadFact]
    public async Task ASenseTheBaselineDoesNotHoldKeepsTheOriginalSentence()
    {
        var (context, documents, document) = await OpenAsync(new FakeCommandClient(), GlossOfSense("buy", "buy (demo)"));
        try
        {
            Assert.Equal("Dry Run ✓ · Will change 1 field: Gloss (en): buy → buy (demo)", document.DryRunCheckText);
        }
        finally { await documents.StopAsync(); await context.StopProjectWorkAsync(); }
    }

    [UiThreadFact]
    public async Task TheSharedEffectNamesTheSenseToo()
    {
        var many = new EffectView(Sense, SnapshotFields.LexSenseGloss, [new EffectChange("en", "buy", "buy (demo)")])
        {
            OperationIds = ["op/one", "op/two"],
        };
        var fake = new FakeCommandClient();
        fake.LexicalEntries[Sense] = ProposalEntryContextTests.PalitAskedBySense(Sense);
        var (context, documents, document) = await OpenAsync(fake, many);
        try
        {
            Assert.Equal("Several changes contribute to the final result for palit sense 2." + Environment.NewLine +
                "Gloss (en): buy → buy (demo)", Assert.Single(document.SharedEffectSummaries));
        }
        finally { await documents.StopAsync(); await context.StopProjectWorkAsync(); }
    }
}
