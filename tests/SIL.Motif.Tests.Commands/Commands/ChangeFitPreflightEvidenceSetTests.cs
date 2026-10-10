using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class ChangeFitPreflightEvidenceSetTests : IDisposable
{
    private readonly string _path;
    private readonly string _jobId = "fit-trial-" + Guid.NewGuid().ToString("N");

    public ChangeFitPreflightEvidenceSetTests(PristineProjectFixture pristine)
    {
        using var scratch = pristine.NewScratch();
        _path = scratch.ProjectId.Path;
    }

    public void Dispose()
    {
        var root = TrialScratchPath.ForJob(_jobId);
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private (Guid Wordform, Proposal Proposal, BaselineToken Token) StageSpellingChange()
    {
        Guid wordform = default;
        var loader = new FwDataProjectLoader();
        using (var cache = loader.LoadCache(_path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                wordform = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("only-in-copy", cache.DefaultVernWs)).Guid);
            loader.Save(cache);
        }
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_path),
            Path.Combine(Path.GetDirectoryName(_path)!, "fit-managed"));
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var proposalId = ProposalCommands.New(new NewDraftRequest(_path, "1.0", "fit-proposal", "A Proposal"))
            .Value!.ProposalId;
        var loaded = PendingChanges.Load(new PendingChangesRequest(_path, "1.0", proposalId));
        var put = PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0", loaded.Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling", CanonicalId.FromGuid(wordform).Value,
                "only-in-copy"), proposalId));
        Assert.True(put.Succeeded, put.Refusal?.Message);
        using var database = ProjectMotifDatabase.Open(_path);
        var json = new ProposalRepository(database).GetDraft("fit-proposal").ProposalJson!;
        var draft = JsonSerializer.Deserialize<DraftDocument>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return (wordform, ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(draft)), captured.Value!.Token);
    }

    private EvidenceSet TrialSet(BaselineToken token) => new(token, null, [], new Dictionary<string, string>(), null, [],
        new EvidenceOrigin(EvidenceOriginKind.Trial, "p", "d", _jobId));

    private void CopyProjectToScratch()
    {
        var root = TrialScratchPath.ForJob(_jobId);
        Directory.CreateDirectory(root);
        var folder = Path.GetDirectoryName(_path)!;
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
                     .Where(file => file.EndsWith(".fwdata", StringComparison.Ordinal)))
            File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
    }

    [Fact]
    public void AParseThatOnlyExistsInTheTrialFitsAgainstTheScratch()
    {
        var (wordform, proposal, token) = StageSpellingChange();
        CopyProjectToScratch();
        var loader = new FwDataProjectLoader();
        using (var cache = loader.LoadCache(_path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(wordform).Delete());
            loader.Save(cache);
        }

        var fits = ChangeFitPreflight.Check(proposal, TrialSet(token), null);

        Assert.All(fits, fit => Assert.True(fit.StillFits, fit.Reason));
        using var live = loader.LoadScratchCache(_path);
        Assert.All(ChangeFitPreflight.Check(live, proposal, token, requireSameBaseline: false),
            fit => Assert.False(fit.StillFits));
    }

    [Fact]
    public void WithoutTheScratchTheChangeIsUncertainAndSaysTryAgain()
    {
        var (_, proposal, token) = StageSpellingChange();

        var fit = Assert.Single(ChangeFitPreflight.Check(proposal, TrialSet(token), null));

        Assert.Equal(ChangeFitStatus.Uncertain, fit.Status);
        Assert.Equal(ChangeFitReasons.TrialScratchGone, fit.Uncertainty?.Reason);
        Assert.Equal(ChangeFitReasonKind.TrialScratchGone, ChangeFitReasons.KindOf(fit.Reason));
    }
}
