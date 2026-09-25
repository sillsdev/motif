using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(LcmCacheTestCollection.Name)]
public sealed class PendingChangesTests
{
    private readonly string _path;

    public PendingChangesTests(PristineProjectFixture pristine)
    {
        using var scratch = pristine.NewScratch();
        _path = scratch.ProjectId.Path;
    }

    [Fact]
    public void ASecondChangeForTheSameSlotIsRefusedWithoutLosingTheFirst()
    {
        var loader = new FwDataProjectLoader();
        Guid wordformId = Guid.Empty;
        using (var cache = loader.LoadCache(_path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                wordformId = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("pending-word", cache.DefaultVernWs)).Guid);
            loader.Save(cache);
        }
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_path),
            Path.Combine(Path.GetDirectoryName(_path)!, "pending-managed")).Succeeded);

        var initial = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));
        Assert.True(initial.Succeeded, initial.Refusal?.Message);
        var wordId = CanonicalId.FromGuid(wordformId).Value;
        var firstId = CanonicalId.Mint().Value;
        var secondId = CanonicalId.Mint().Value;
        var first = PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0",
            initial.Value!.Revision, new ChangeIntent(firstId, "incorrect-spelling", wordId, "pending-word")));
        Assert.True(first.Succeeded, first.Refusal?.Message);
        var second = PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0",
            first.Value!.Revision, new ChangeIntent(secondId, "incorrect-spelling", wordId, "pending-word")));
        Assert.Equal("change.slot-occupied", second.Refusal?.Code);
        Assert.Equal(secondId, second.Refusal?.Facts["changeId"]);
        Assert.Equal(firstId, second.Refusal?.Facts["existingChangeId"]);
        Assert.All(first.Value.FitSummary, fit => Assert.True(fit.StillFits,
            string.Join(" ", fit.Reasons)));
        Assert.Single(PendingChanges.Load(new PendingChangesRequest(_path, "1.0")).Value!.Changes);

        var stale = PendingChanges.Remove(new RemovePendingChangeRequest(_path, "1.0",
            initial.Value.Revision, firstId));
        Assert.Equal("change.revision-conflict", stale.Refusal?.Code);
        Assert.Equal(firstId, stale.Refusal?.Facts["changeId"]);
        Assert.Single(PendingChanges.Load(new PendingChangesRequest(_path, "1.0")).Value!.Changes);

        var json = JsonSerializer.Serialize(first.Value);
        Assert.Equal(json, JsonSerializer.Serialize(JsonSerializer.Deserialize<PendingChangesSnapshot>(json)));

        using (var cache = loader.LoadCache(_path))
        {
            var wordform = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(wordformId);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => wordform.Delete());
            loader.Save(cache);
        }
        var afterDeletion = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));
        Assert.True(afterDeletion.Succeeded, afterDeletion.Refusal?.Message);
        Assert.All(afterDeletion.Value!.FitSummary, fit =>
        {
            Assert.False(fit.StillFits);
            Assert.Contains(fit.Reasons, reason => reason.Contains("deleted", StringComparison.Ordinal));
        });

        using (var database = ProjectMotifDatabase.Open(_path))
        {
            var repository = new ProposalRepository(database);
            var stored = JsonNode.Parse(repository.GetDraft(PendingChanges.DraftName).ProposalJson!)!;
            stored["composerProvenance"]!.AsArray().Clear();
            stored["operations"]![0]!["extensions"]!["changeFit"] = null;
            repository.SaveDraft(PendingChanges.DraftName, stored.ToJsonString());
        }
        var unmapped = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));
        Assert.True(unmapped.Succeeded, unmapped.Refusal?.Message);
        Assert.Single(unmapped.Value!.Changes);
        Assert.All(unmapped.Value.FitSummary, fit =>
            Assert.Contains(fit.Reasons, reason => reason.Contains("mapping or fingerprint", StringComparison.Ordinal)));

        using (var database = ProjectMotifDatabase.Open(_path))
        {
            var repository = new ProposalRepository(database);
            var stored = JsonNode.Parse(repository.GetDraft(PendingChanges.DraftName).ProposalJson!)!;
            stored["composerProvenance"]!.AsArray().Add(new JsonObject
            {
                ["changeId"] = firstId, ["wordformId"] = wordId,
                ["word"] = "pending-word", ["kind"] = "incorrect-spelling",
                ["operationIds"] = new JsonArray(stored["operations"]![0]!["operationId"]!.GetValue<string>()),
            });
            stored["operations"]![0]!["extensions"]!.AsObject().Remove("changeId");
            repository.SaveDraft(PendingChanges.DraftName, stored.ToJsonString());
        }
        var orphaned = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));
        Assert.Single(orphaned.Value!.Changes);
        var removedOrphan = PendingChanges.Remove(new RemovePendingChangeRequest(_path, "1.0",
            orphaned.Value.Revision, firstId));
        Assert.True(removedOrphan.Succeeded, removedOrphan.Refusal?.Message);
        Assert.Empty(removedOrphan.Value!.Changes);
    }

    [Fact]
    public void AWordWithoutUniqueWordformIdentityIsRefusedWithFacts()
    {
        var loader = new FwDataProjectLoader();
        using (var cache = loader.LoadCache(_path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var factory = cache.ServiceLocator.GetInstance<IWfiWordformFactory>();
                factory.Create(TsStringUtils.MakeString("same-form", cache.DefaultVernWs));
                factory.Create(TsStringUtils.MakeString("same-form", cache.DefaultVernWs));
            });
            loader.Save(cache);
        }
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_path),
            Path.Combine(Path.GetDirectoryName(_path)!, "ambiguous-managed")).Succeeded);
        var initial = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));
        var changeId = CanonicalId.Mint().Value;

        var outcome = PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0",
            initial.Value!.Revision, new ChangeIntent(changeId, "incorrect-spelling", "", "same-form")));

        Assert.Equal("change.wordform-ambiguous", outcome.Refusal?.Code);
        Assert.Equal(changeId, outcome.Refusal?.Facts["changeId"]);
        Assert.Equal("same-form", outcome.Refusal?.Facts["word"]);
    }

    [Fact]
    public void WordformLookupAcceptsCanonicallyEquivalentUnicode()
    {
        var loader = new FwDataProjectLoader();
        using (var cache = loader.LoadCache(_path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("a\u0301", cache.DefaultVernWs)));
            loader.Save(cache);
        }
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_path),
            Path.Combine(Path.GetDirectoryName(_path)!, "unicode-managed")).Succeeded);
        var current = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));

        var put = PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0", current.Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling", "", "\u00e1")));

        Assert.True(put.Succeeded, put.Refusal?.Message);
        Assert.True(Assert.Single(put.Value!.FitSummary).StillFits);
    }
}
