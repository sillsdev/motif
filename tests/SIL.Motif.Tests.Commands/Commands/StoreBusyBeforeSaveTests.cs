using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// A Draft step that cannot get the project database's write lock has saved nothing, so it is a Busy to retry,
/// never a malformed Draft carrying SQLite's own text.
/// </summary>
[Collection(LcmCacheParallelCollections.Group2)]
public sealed class StoreBusyBeforeSaveTests(PristineProjectFixture pristine)
{
    [Fact]
    public void ADraftEditWhileAnotherWriterHoldsTheStoreIsBusyBecauseNothingWasSaved()
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        var version = MotifProductVersion.CurrentText;
        const string name = "busy-draft";
        Assert.True(ProposalCommands.New(new NewDraftRequest(path, version, name, "Busy probe")).Succeeded);
        var sense = CanonicalId.FromGuid(pristine.Seed.FirstSenseId).Value;

        CommandOutcome<SetGlossAddedResponse> held;
        using (var database = ProjectMotifDatabase.Open(path))
        using (var connection = database.OpenConnection())
        using (var writer = connection.BeginTransaction())
        {
            held = ProposalCommands.AddSetGloss(new AddSetGlossRequest(path, version, name, sense,
                NewLangProjFixture.AnalysisTag, "while held"));
            writer.Rollback();
        }

        Assert.False(held.Succeeded);
        Assert.Equal("project.busy", held.Refusal!.Code);
        Assert.Equal(3, FailureEnvelope.ExitCodeFor(held.Refusal.Reason));
        Assert.DoesNotContain("SQLite", held.Refusal.Message, StringComparison.Ordinal);
        var retried = ProposalCommands.AddSetGloss(new AddSetGlossRequest(path, version, name, sense,
            NewLangProjFixture.AnalysisTag, "after release"));
        Assert.True(retried.Succeeded, retried.Refusal?.Message);
        Assert.Equal(1, retried.Value!.OperationCount);
    }
}
