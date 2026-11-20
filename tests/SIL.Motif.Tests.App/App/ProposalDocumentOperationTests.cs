using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Operations;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ProposalDocumentOperationTests
{
    [Fact]
    public async Task TwoChangesToTheSameGlossKeepTheirAuthoredValues()
    {
        const string sense = "sense/one";
        var first = new ProposalOperationView("op/first", LexicalSenseOperationKinds.SetGloss, sense, null,
            [], """{"ws":"en","text":"hello"}""");
        var last = new ProposalOperationView("op/last", LexicalSenseOperationKinds.SetGloss, sense, null,
            [first.OperationId], """{"ws":"en","text":"greeting"}""");
        var effect = new EffectView(sense, SnapshotFields.LexSenseGloss, [new("en", "salutation", "greeting")])
        {
            OperationIds = [first.OperationId, last.OperationId],
        };
        var detail = new ProposalDetailProjection("one", "proposed", "Clarify the gloss", null, "intent/one",
            [first, last]);
        var dryRun = new DryRunProjection("one", "intent/one", "", [effect], "effect", "footprint");
        var trial = new TrialRecord("job/one", "one", "intent/one", WorkspaceContextTests.Assessment().Baseline.Token,
            dryRun, new Dictionary<string, string>(), [], false, null, JobStatus.Completed, DateTimeOffset.UtcNow,
            TrialFreshness.Current);
        var fake = new FakeCommandClient
        {
            ShowProposalHandler = (_, _) => Task.FromResult(CommandOutcome<ProposalDetailProjection>.Success(detail)),
            Review = new("one", "intent/one", "revision/one", [], trial, null, [], 0),
        };
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        var documents = new OpenProposalDocuments(context);
        try
        {
            var document = documents.Open("one");
            await document.RefreshAsync();

            Assert.Collection(document.OperationRows,
                row => Assert.Equal("Proposed value (en): hello", row.Values),
                row => Assert.Equal("Proposed value (en): greeting", row.Values));
            Assert.Equal($"Several changes contribute to the final result for {sense}.{Environment.NewLine}" +
                "Gloss (en): salutation → greeting", Assert.Single(document.SharedEffectSummaries));
        }
        finally
        {
            await documents.StopAsync();
            await context.StopProjectWorkAsync();
        }
    }

    [Fact]
    public async Task AnAppliedProposalKeepsTheOldToNewValueAfterTheBaselineMoved()
    {
        const string sense = "sense/one";
        var op = new ProposalOperationView("op/one", LexicalSenseOperationKinds.SetGloss, sense, null,
            [], """{"ws":"en","text":"buy (demo)"}""");
        var effect = new EffectView(sense, SnapshotFields.LexSenseGloss, [new("en", "buy", "buy (demo)")])
        {
            OperationIds = [op.OperationId],
        };
        var detail = new ProposalDetailProjection("one", "applied", "Change the gloss", null, "intent/one", [op]);
        var dryRun = new DryRunProjection("one", "intent/one", "", [effect], "effect", "footprint");
        var trial = new TrialRecord("job/one", "one", "intent/one", WorkspaceContextTests.Assessment().Baseline.Token,
            dryRun, new Dictionary<string, string>(), [], false, null, JobStatus.Completed, DateTimeOffset.UtcNow,
            TrialFreshness.BaselineMoved);
        var fake = new FakeCommandClient
        {
            ShowProposalHandler = (_, _) => Task.FromResult(CommandOutcome<ProposalDetailProjection>.Success(detail)),
            Review = new("one", "intent/one", "revision/one", [], trial, null, [], 0),
        };
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        var documents = new OpenProposalDocuments(context);
        try
        {
            var document = documents.Open("one");
            await document.RefreshAsync();

            Assert.Equal("Gloss (en): buy → buy (demo)", Assert.Single(document.OperationRows).Values);
        }
        finally
        {
            await documents.StopAsync();
            await context.StopProjectWorkAsync();
        }
    }

    [Fact]
    public void TheReceiptTimeReadsInLocalTimeLikeEveryOtherTimeOnThePage()
    {
        var expected = new DateTimeOffset(2026, 11, 19, 21, 5, 5, TimeSpan.Zero).ToLocalTime()
            .ToString("g", System.Globalization.CultureInfo.CurrentCulture);

        Assert.Equal(expected, ProposalDocumentViewModel.LocalRecordedTime("20261119T210505Z"));
        Assert.Equal(expected, ProposalDocumentViewModel.LocalRecordedTime("2026-11-19T21:05:05Z"));
        Assert.Equal("not a time", ProposalDocumentViewModel.LocalRecordedTime("not a time"));
    }

    [Fact]
    public void TheDifferenceTickSaysHowManyWordsWereComparedAndHowManyChanged()
    {
        var summary = new ProposalDifferenceFixture().Summary with
        {
            TextTitle = "CEB approved words", Words = 1718,
            Matrix = [new("Approved", "Approved", 1700, 1700), new("Approved", "Unparsed", 18, 20)],
        };

        Assert.Equal("CEB approved words · " + 1718.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) +
            " words · 18 changed approval", ProposalDocumentViewModel.DifferenceDetail(summary));
    }

    [Fact]
    public async Task AnUntriedProposalSaysWhyApplyIsGreyInsteadOfLeavingAGap()
    {
        var op = new ProposalOperationView("op/one", LexicalSenseOperationKinds.SetGloss, "sense/one", null,
            [], """{"ws":"en","text":"sales (demo)"}""");
        var detail = new ProposalDetailProjection("one", "proposed", "Change the gloss", null, "intent/one", [op]);
        var fake = new FakeCommandClient
        {
            ShowProposalHandler = (_, _) => Task.FromResult(CommandOutcome<ProposalDetailProjection>.Success(detail)),
        };
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        var documents = new OpenProposalDocuments(context);
        try
        {
            var document = documents.Open("one");
            await document.RefreshAsync();

            Assert.False(document.TriedChecked);
            Assert.Equal("Finish the steps above, then apply.", document.ApplyDisplayReason);
        }
        finally
        {
            await documents.StopAsync();
            await context.StopProjectWorkAsync();
        }
    }
}
