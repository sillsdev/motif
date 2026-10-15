using System.Reflection;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class ShowFinalizeRaceTests(PristineProjectFixture pristine)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NamedDraftShowIsCoherentWhenAnotherConnectionFinalizesAfterItsRead(bool reopened)
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        var version = MotifProductVersion.CurrentText;
        const string name = "proposal/short";
        var created = ProposalCommands.New(new NewDraftRequest(path, version, name, "Race probe"));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(path, version, name,
            CanonicalId.FromGuid(pristine.Seed.FirstSenseId).Value, NewLangProjFixture.AnalysisTag,
            "coherent")).Succeeded);
        Assert.True(DraftRationale.Write(path, version, name, "Read before Finalize").Succeeded);
        string? priorDigest = null;
        if (reopened)
        {
            var first = ProposalCommands.Finalize(new FinalizeRequest(path, version, name));
            Assert.True(first.Succeeded, first.Refusal?.Message);
            priorDigest = first.Value!.IntentDigest;
            Assert.True(ProposalCommands.Reopen(new ReopenRequest(path, version, name, created.Value!.ProposalId)).Succeeded);
        }

        using var database = ProjectMotifDatabase.Open(path);
        var repository = new ProposalRepository(database);
        var proxy = DispatchProxy.Create<IProposalRepository, FinalizeAfterRead>();
        var controlled = (FinalizeAfterRead)proxy;
        controlled.Repository = repository;
        controlled.Finalize = () =>
        {
            var finalized = ProposalCommands.Finalize(new FinalizeRequest(path, version, name));
            Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        };

        var shown = ProposalCommands.BuildProposalDetail(proxy, name);

        Assert.True(controlled.DidFinalize);
        Assert.True(shown.Succeeded, shown.Refusal?.Code + ": " + shown.Refusal?.Message);
        Assert.Equal(name, shown.Value!.DraftName);
        Assert.Equal(created.Value!.ProposalId, shown.Value.ProposalId);
        Assert.Equal(priorDigest, shown.Value.CurrentIntentDigest);
        Assert.NotEmpty(shown.Value.Operations);
        var committed = repository.GetFinalized(CanonicalId.Parse(created.Value.ProposalId));
        Assert.Null(committed.Record.DraftName);
        Assert.NotNull(committed.Record.IntentDigest);
        var after = ProposalCommands.Show(new ShowProposalRequest(path, version, name));
        Assert.False(after.Succeeded);
        Assert.Equal("proposal.not-found", after.Refusal!.Code);
    }

    public class FinalizeAfterRead : DispatchProxy
    {
        public IProposalRepository Repository { get; set; } = null!;
        public Action Finalize { get; set; } = null!;
        public bool DidFinalize { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var result = targetMethod!.Invoke(Repository, args);
            if (!DidFinalize && targetMethod.Name.StartsWith("GetDraft", StringComparison.Ordinal))
            {
                DidFinalize = true;
                Finalize();
            }
            return result;
        }
    }
}
