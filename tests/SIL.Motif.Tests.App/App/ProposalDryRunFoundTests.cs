using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>The Dry Run row says what the Dry Run found, and the Apply confirmation repeats it.</summary>
public sealed class ProposalDryRunFoundTests
{
    private static EffectView Gloss(string before, string after) => new("sense/" + before, SIL.Motif.Model.Snapshot.SnapshotFields.LexSenseGloss,
        [new EffectChange("en", before, after)]);

    private static async Task<(WorkspaceContext Context, OpenProposalDocuments Documents, ProposalDocumentViewModel Document)>
        OpenAsync(params EffectView[] effects)
    {
        var fake = new FakeCommandClient();
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

    [Fact]
    public async Task TheDryRunRowNamesTheOneFieldItWillChange()
    {
        var (context, documents, document) = await OpenAsync(Gloss("buy", "buy (demo)"));
        try
        {
            Assert.True(document.DryRunChecked);
            Assert.Equal("Dry Run ✓ · Will change 1 field: Gloss (en): buy → buy (demo)", document.DryRunCheckText);
        }
        finally { await documents.StopAsync(); await context.StopProjectWorkAsync(); }
    }

    [Fact]
    public async Task TheDryRunRowCountsManyFieldsAndShowsTheFirstThree()
    {
        var (context, documents, document) = await OpenAsync(Gloss("a", "b"), Gloss("c", "d"), Gloss("e", "f"),
            Gloss("g", "h"), Gloss("i", "j"));
        try
        {
            Assert.StartsWith("Dry Run ✓ · Will change 5 fields: Gloss (en): a → b; Gloss (en): c → d; " +
                "Gloss (en): e → f; and 2 more", document.DryRunCheckText, StringComparison.Ordinal);
        }
        finally { await documents.StopAsync(); await context.StopProjectWorkAsync(); }
    }

    [Fact]
    public async Task TwoLanguagesOfOneFieldCountAsOneField()
    {
        var both = new EffectView("sense/two", SIL.Motif.Model.Snapshot.SnapshotFields.LexSenseGloss,
            [new EffectChange("en", "a", "b"), new EffectChange("fr", "c", "d")]);
        var (context, documents, document) = await OpenAsync(both);
        try
        {
            Assert.StartsWith("Dry Run ✓ · Will change 1 field: Gloss (en): a → b; Gloss (fr): c → d",
                document.DryRunCheckText, StringComparison.Ordinal);
        }
        finally { await documents.StopAsync(); await context.StopProjectWorkAsync(); }
    }

    [Fact]
    public async Task ADryRunWithNoEffectsSaysItWillChangeNothing()
    {
        var (context, documents, document) = await OpenAsync();
        try
        {
            Assert.Equal("Dry Run ✓ · Will change nothing in the project.", document.DryRunCheckText);
        }
        finally { await documents.StopAsync(); await context.StopProjectWorkAsync(); }
    }

    [Fact]
    public async Task TheApplyConfirmationRepeatsTheDryRunSentence()
    {
        var (context, documents, document) = await OpenAsync(Gloss("buy", "buy (demo)"));
        try
        {
            await document.PrepareApplyCommand.ExecuteAsync(null);

            Assert.True(document.IsConfirmingApply);
            Assert.Equal("Will change 1 field: Gloss (en): buy → buy (demo)", document.ApplyWillChangeText);
        }
        finally { await documents.StopAsync(); await context.StopProjectWorkAsync(); }
    }
}
