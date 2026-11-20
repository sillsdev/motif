using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Model.AppliedLog;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Receipts;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class ProposalShowAppliedTests(PristineProjectFixture pristine)
{
    private static string Version => MotifProductVersion.CurrentText;

    private (string Path, string Id, Guid Sense) Finalized(string name)
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        var sense = pristine.Seed.FirstSenseId;
        var created = ProposalCommands.New(new NewDraftRequest(path, Version, name, name));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(path, Version, name,
            CanonicalId.FromGuid(sense).Value, "en", "after gloss")).Succeeded);
        DraftRationale.Author(path, name, name, "Why " + name + " is right.");
        Assert.True(ProposalCommands.Finalize(new FinalizeRequest(path, Version, name)).Succeeded);
        return (path, created.Value!.ProposalId, sense);
    }

    private static void RecordApply(string path, string id, Guid sense, string mode)
    {
        using var database = ProjectMotifDatabase.Open(path);
        var repository = new ProposalRepository(database);
        var proposal = CanonicalId.Parse(id);
        var digest = repository.Get(proposal).IntentDigest!;
        var effect = new ExpectedEffect(CanonicalId.FromGuid(sense), "lexical/sense/gloss",
            new Dictionary<string, string> { ["en"] = "before gloss" },
            new Dictionary<string, string> { ["en"] = "after gloss" });
        repository.RecordAppliedReceipt(new Receipt(proposal, digest, false, "baseline", "applied", [effect], "sha256:effect",
            new AppliedLogEntry(proposal.ToGuid(), 1, "2026-11-19T10:00:00Z", "tester", digest[7..], "My message", mode)));
    }

    [Fact]
    public void AnAppliedProposalShowsWhatItChangedBeforeAndAfterAsRecordedAtApply()
    {
        var (path, id, sense) = Finalized("applied-record");
        RecordApply(path, id, sense, "person");

        var shown = ProposalCommands.Show(new ShowProposalRequest(path, Version, id));

        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        Assert.Equal("applied", shown.Value!.Status);
        var record = shown.Value.AppliedRecord!;
        Assert.Equal(("2026-11-19T10:00:00Z", "person", "My message", "tester"),
            (record.AppliedUtc, record.Mode, record.Message, record.User));
        var effect = Assert.Single(record.Effects);
        Assert.Equal((CanonicalId.FromGuid(sense).Value, "lexical/sense/gloss"), (effect.Target, effect.Field));
        Assert.Equal("before gloss", effect.Before["en"]);
        Assert.Equal("after gloss", effect.After["en"]);
        Assert.Empty(record.Evidence);
    }

    [Fact]
    public void AProposalThatIsNotAppliedCarriesNoAppliedRecord()
    {
        var (path, id, _) = Finalized("not-applied");

        Assert.Null(ProposalCommands.Show(new ShowProposalRequest(path, Version, id)).Value!.AppliedRecord);
    }

    [Fact]
    public void AnAppliedProposalWithNoReceiptStillShowsWithoutARecord()
    {
        var (path, id, _) = Finalized("no-receipt");
        using (var database = ProjectMotifDatabase.Open(path))
            new ProposalRepository(database).SetStatus(CanonicalId.Parse(id), "applied", null);

        var shown = ProposalCommands.Show(new ShowProposalRequest(path, Version, id));

        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        Assert.Null(shown.Value!.AppliedRecord);
    }

    [Fact]
    public void TheEvidenceSummaryFrozenAtApplyIsReadBack()
    {
        var (path, id, sense) = Finalized("applied-evidence");
        using (var database = ProjectMotifDatabase.Open(path))
        {
            var pin = new ProposalDifferenceSidePin(null, null, "sha256:grammar", "Default", "sha256:selection", "whitespace", "1");
            new ProposalDifferenceRepository(database).Publish(new NewProposalDifference(new ProposalDifferenceSummary(
                "difference/1", id, "trial-1", "before", "after", "text/1", "Genesis", "baseline/1",
                new ProposalDifferencePolicy(), 7, 12, 0, 1, 2, 3, [], [], "pangloss", null, pin, pin, [], 0, [],
                "2026-10-13T00:00:00Z"), []));
        }
        RecordApply(path, id, sense, "autonomous");

        var record = ProposalCommands.Show(new ShowProposalRequest(path, Version, id)).Value!.AppliedRecord!;

        Assert.Equal("autonomous", record.Mode);
        var evidence = Assert.Single(record.Evidence);
        Assert.Equal(("Genesis", 7, 12, 3, 2), (evidence.TextTitle, evidence.Words, evidence.Occurrences,
            evidence.ImprovedWords, evidence.CouldBeBadWords));
    }
}
