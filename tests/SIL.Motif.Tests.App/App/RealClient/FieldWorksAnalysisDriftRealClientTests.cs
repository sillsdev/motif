using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.App;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

[Collection(LcmCacheTestCollection.Name)]
public sealed class FieldWorksAnalysisDriftRealClientTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task SavedAnalysisChangeBlocksApplyAndKeepsTheChange()
    {
        using var project = new WalkthroughProject(pristine);
        var word = "analysis-drift-word";
        Guid wordformId = Guid.Empty;
        Guid analysisId = Guid.Empty;
        new FieldWorksSimulator(project.FwDataPath).SaveEdit(cache =>
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var wordform = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString(word, cache.DefaultVernWs));
                wordformId = wordform.Guid;
                var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
                wordform.AnalysesOC.Add(analysis);
                analysisId = analysis.Guid;
                var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>()
                    .GetObject(pristine.Seed.FirstEntryId);
                var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
                analysis.MorphBundlesOS.Add(bundle);
                bundle.MorphRA = entry.LexemeFormOA;
                bundle.MsaRA = entry.MorphoSyntaxAnalysesOC.First();
            }));

        await using var runner = new InProcessRunnerLauncher(
            new JobRunnerLaunchOptions(project.ManagedRoot, FakeParser.ExecutablePath));
        var client = RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath, runner);
        var captured = await client.CaptureBaselineAsync(
            new BaselineCaptureRequest(project.FwDataPath), CancellationToken.None);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var initial = await client.LoadPendingChangesAsync(
            new PendingChangesRequest(project.FwDataPath, "1.0"), CancellationToken.None);
        Assert.True(initial.Succeeded, initial.Refusal?.Message);
        var added = await client.PutPendingChangeAsync(new PutPendingChangeRequest(
            project.FwDataPath, "1.0", initial.Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "approve",
                CanonicalId.FromGuid(wordformId).Value, word,
                StoredAnalysisId: CanonicalId.FromGuid(analysisId).Value)), CancellationToken.None);
        Assert.True(added.Succeeded, added.Refusal?.Message);
        var measured = await client.MeasurePendingAsync(new MeasurePendingRequest(
            project.FwDataPath, added.Value!.DraftId!, added.Value.Revision, [word]),
            new Progress<MeasureProgress>(), CancellationToken.None);
        Assert.True(measured.Succeeded, measured.Refusal?.Message);
        Assert.True(measured.Value!.EvidenceComplete);

        new FieldWorksSimulator(project.FwDataPath).SaveEdit(cache =>
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>().GetObject(analysisId);
                cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.disapproves);
            }));
        var refreshed = await client.CaptureBaselineAsync(
            new BaselineCaptureRequest(project.FwDataPath), CancellationToken.None);
        Assert.True(refreshed.Succeeded, refreshed.Refusal?.Message);
        var changed = await client.LoadPendingChangesAsync(
            new PendingChangesRequest(project.FwDataPath, "1.0"), CancellationToken.None);
        Assert.True(changed.Succeeded, changed.Refusal?.Message);
        Assert.False(Assert.Single(changed.Value!.FitSummary).StillFits);

        var applied = await client.ApplyPendingAsync(new ApplyPendingRequest(
            project.FwDataPath, changed.Value.DraftId!, changed.Value.Revision, "test-user"),
            CancellationToken.None);

        Assert.False(applied.Succeeded);
        Assert.NotNull(applied.Refusal);
        Assert.False(applied.Value?.Applied ?? false);
        var retained = await client.LoadPendingChangesAsync(
            new PendingChangesRequest(project.FwDataPath, "1.0"), CancellationToken.None);
        Assert.True(retained.Succeeded, retained.Refusal?.Message);
        Assert.Single(retained.Value!.Changes);
        await runner.WhenIdleAsync();
    }
}
