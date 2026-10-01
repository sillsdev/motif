using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
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
