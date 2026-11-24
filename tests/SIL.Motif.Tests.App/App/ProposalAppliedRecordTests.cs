using SIL.Motif.App;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>An Applied Proposal shows what Apply froze: the fields before and after, and what was known.</summary>
public sealed class ProposalAppliedRecordTests
{
    private static readonly string Sense = ProposalEntryContextTests.SenseTwo;

    private static AppliedRecordView Record(string mode = "person") => new("20261119T210505Z", mode, "Applied", "johnm",
        "intent/one",
        [
            new AppliedEffectView(Sense, "lexical/sense/gloss",
                new Dictionary<string, string> { ["en"] = "buy" }, new Dictionary<string, string> { ["en"] = "buy (demo)" }),
            new AppliedEffectView(Sense, "lexical/sense/gloss",
                new Dictionary<string, string>(), new Dictionary<string, string> { ["fr"] = "acheter" }),
        ],
        [new AppliedEvidenceView("difference/one", "text/one", "CEB approved words", 1718, 52000, 31, 4,
            "2026-11-19T20:00:00Z")]);

    private static ProposalDetailProjection Applied(AppliedRecordView? record, string status = "applied") =>
        ProposalEntryContextTests.GlossProposal(status) with { AppliedRecord = record };

    [UiThreadFact]
    public async Task AnAppliedProposalShowsWhenItWasAppliedWhatChangedAndWhatWasKnown()
    {
        var fake = new FakeCommandClient { ProposalDetail = Applied(Record()) };
        fake.LexicalEntries[Sense] = ProposalEntryContextTests.PalitAskedBySense(Sense);

        await using var opened = await ProposalEntryContextTests.Open(fake);

        var document = opened.Document;
        Assert.True(document.HasAppliedRecord);
        var time = SIL.Motif.App.ViewModels.ProposalDocumentViewModel.LocalRecordedTime("20261119T210505Z");
        Assert.Equal($"Applied by you on {time}.", document.AppliedHeading);
        Assert.Collection(document.AppliedEffects,
            line => Assert.Equal("palit sense 2: Gloss (en): buy → buy (demo)", line),
            line => Assert.Equal("palit sense 2: Gloss (fr): (empty) → acheter", line));
        Assert.True(document.HasAppliedEvidence);
        Assert.Equal("CEB approved words · " + 1718.ToString("N0") + " words · " + 52000.ToString("N0") +
            " places · 31 improved · 4 to check", Assert.Single(document.AppliedEvidence));
    }

    [UiThreadFact]
    public async Task AnAutonomousApplyNamesWhoMadeItAndTheIdStaysWhenTheBaselineLacksTheSense()
    {
        var fake = new FakeCommandClient { ProposalDetail = Applied(Record("autonomous")) };

        await using var opened = await ProposalEntryContextTests.Open(fake);

        Assert.Contains("by johnm, without a person pressing Apply here.", opened.Document.AppliedHeading);
        Assert.StartsWith(Sense + ": Gloss (en)", opened.Document.AppliedEffects[0]);
    }

    [UiThreadFact]
    public async Task AnAppliedProposalWithNoSavedDifferenceSaysSo()
    {
        var record = Record() with { Evidence = [] };
        var fake = new FakeCommandClient { ProposalDetail = Applied(record) };

        await using var opened = await ProposalEntryContextTests.Open(fake);

        Assert.True(opened.Document.HasAppliedRecord);
        Assert.False(opened.Document.HasAppliedEvidence);
        Assert.Equal("No Difference was saved before it was applied.", MotifAppStrings.ProposalAppliedNoEvidence);
    }

    [UiThreadFact]
    public async Task AWaitingProposalOrAnAppliedOneWithoutARecordShowsNoAppliedSection()
    {
        await using (var waiting = await ProposalEntryContextTests.Open(new FakeCommandClient
            { ProposalDetail = Applied(Record(), "proposed") }))
            Assert.False(waiting.Document.HasAppliedRecord);

        await using var legacy = await ProposalEntryContextTests.Open(new FakeCommandClient
            { ProposalDetail = Applied(null) });
        Assert.False(legacy.Document.HasAppliedRecord);
        Assert.Empty(legacy.Document.AppliedEffects);
    }
}
