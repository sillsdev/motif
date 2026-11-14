using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Scripture;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Projects;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Source edits staged in the pending changes show as rows, are fit-checked, and reach the project only when the
/// pending changes are applied: the round trip of the Motif Sources list on a scratch project.
/// </summary>
[Trait("MotifTestLevel", "Integration")]
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class SourceStagingTests(PristineProjectFixture pristine)
{
    [Fact]
    public void AStagedAddIsARowThatAppliesAndReadsBackAndASecondApplyHasNothingLeft()
    {
        var path = Project();

        var folder = Directory.CreateTempSubdirectory("source-staging-").FullName;
        var added = ProposalCommands.StageSourceAdd(new SourceAddRequest(path, "WEB", "World English Bible", "English", "MAT-JHN; PSA 1-50", Folder: folder));
        Assert.True(added.Succeeded, added.Refusal?.Message);
        Assert.Equal(folder, added.Value!.Folder);
        Assert.Empty(Read(path));

        var row = Assert.Single(Pending(path).Changes);
        Assert.Equal("source-add", row.Kind);
        Assert.Equal("WEB", row.Word);
        Assert.Equal("Add Source WEB", row.DisplayReading);

        var applied = Apply(path);
        Assert.True(applied.Succeeded, applied.Refusal?.Code + ": " + applied.Refusal?.Message);
        Assert.True(applied.Value!.Applied, applied.Value.Summary);
        Assert.Contains("Source", applied.Value.Summary);

        var entry = Assert.Single(Read(path));
        Assert.Equal(("WEB", "World English Bible", "English", "MAT-JHN; PSA 1-50", SourceStatus.Active),
            (entry.Id, entry.Name, entry.VersificationName, entry.ContextText, entry.Status));
        Assert.Empty(Pending(path).Changes);
        Assert.False(Apply(path).Value?.Applied ?? false);
    }

    [Fact]
    public void AnEditStagedAfterAnAddInTheSameDraftSeesTheStagedSourceAndEditsApplyInOrder()
    {
        var path = Project();
        Assert.True(ProposalCommands.StageSourceAdd(new SourceAddRequest(path, "WEB", "Bible", "English", "MAT")).Succeeded);

        Assert.True(ProposalCommands.StageSourceSetName(new SourceSetNameRequest(path, "web", "World English Bible")).Succeeded);
        Assert.True(ProposalCommands.StageSourceSetContext(new SourceSetContextRequest(path, "WEB", "MAT-MRK")).Succeeded);
        Assert.True(ProposalCommands.StageSourceSetName(new SourceSetNameRequest(path, "WEB", "WEB final")).Succeeded);

        Assert.Equal(["source-add", "source-set-context", "source-set-name"],
            Pending(path).Changes.Select(change => change.Kind).Order(StringComparer.Ordinal));
        Assert.True(Apply(path).Value!.Applied);
        var entry = Assert.Single(Read(path));
        Assert.Equal(("WEB final", "MAT-MRK"), (entry.Name, entry.ContextText));
    }

    [Fact]
    public void RemoveKeepsTheItemAndItsIdAndRestoreBringsItBack()
    {
        var path = Project();
        Assert.True(ProposalCommands.StageSourceAdd(new SourceAddRequest(path, "WEB", "Bible", "English", "MAT")).Succeeded);
        Assert.True(Apply(path).Value!.Applied);

        Assert.True(ProposalCommands.StageSourceRemove(new SourceRemoveRequest(path, "WEB")).Succeeded);
        Assert.True(Apply(path).Value!.Applied);
        Assert.Equal(SourceStatus.Removed, Assert.Single(Read(path)).Status);
        Assert.Equal(RefusalCodes.SourceIdRemoved,
            ProposalCommands.StageSourceAdd(new SourceAddRequest(path, "WEB", "Again", "English", "MAT")).Refusal!.Code);

        Assert.True(ProposalCommands.StageSourceRestore(new SourceRestoreRequest(path, "WEB")).Succeeded);
        Assert.True(Apply(path).Value!.Applied);
        Assert.Equal(SourceStatus.Active, Assert.Single(Read(path)).Status);
    }

    [Fact]
    public void ARefusedStagingLeavesTheDraftAlone()
    {
        var path = Project();
        Assert.True(ProposalCommands.StageSourceAdd(new SourceAddRequest(path, "WEB", "Bible", "English", "MAT")).Succeeded);

        Assert.Equal(RefusalCodes.SourceIdInUse,
            ProposalCommands.StageSourceAdd(new SourceAddRequest(path, "WEB", "Again", "English", "MAT")).Refusal!.Code);
        Assert.Equal(RefusalCodes.SourceNotFound,
            ProposalCommands.StageSourceSetName(new SourceSetNameRequest(path, "NOPE", "x")).Refusal!.Code);
        Assert.Equal(RefusalCodes.SourceVersificationUnknown,
            ProposalCommands.StageSourceSetVersification(new SourceSetVersificationRequest(path, "WEB", "No such")).Refusal!.Code);
        Assert.Equal(RefusalCodes.SourceContextInvalid,
            ProposalCommands.StageSourceSetContext(new SourceSetContextRequest(path, "WEB", "ZZZ 99")).Refusal!.Code);
        Assert.Equal(RefusalCodes.SourceFolderMissing,
            ProposalCommands.StageSourceAdd(new SourceAddRequest(path, "CEB", "Cebuano", "English", "NT", Folder: path + ".absent")).Refusal!.Code);

        Assert.Equal("source-add", Assert.Single(Pending(path).Changes).Kind);
    }

    [Fact]
    public void AStagedAddNoLongerFitsOnceTheIdIsTakenInTheProjectAndApplyWritesNothing()
    {
        var path = Project();
        Assert.True(ProposalCommands.StageSourceAdd(new SourceAddRequest(path, "WEB", "Bible", "English", "MAT")).Succeeded);
        using (var cache = new FwDataProjectLoader().LoadCache(path))
        {
            UndoableUnitOfWorkHelper.Do("test", "test", cache.ServiceLocator.GetInstance<IActionHandler>(), () =>
                OperationHandlerRegistry.Resolve(ScriptureSourceOperationKinds.Create, "test").ApplyAndCaptureEffect(
                    cache, new OperationEnvelope(CanonicalId.Mint(), ScriptureSourceOperationKinds.Create,
                        entityId: CanonicalId.Mint(), target: CanonicalId.FromGuid(cache.LangProject.Guid),
                        after: JsonSerializer.SerializeToElement(new
                        {
                            id = "Web", name = "By hand", versification = "English", context = "GEN", kind = "Paratext",
                        })), []));
            new FwDataProjectLoader().Save(cache);
        }
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), Root(path)).Succeeded);

        var applied = Apply(path);

        Assert.Equal(RefusalCodes.ApplyChangeNoLongerFits, applied.Refusal?.Code);
        Assert.Equal("By hand", Assert.Single(Read(path)).Name);
    }

    private string Project()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), Root(path)).Succeeded);
        return path;
    }

    private static PendingChangesSnapshot Pending(string path)
    {
        var loaded = PendingChanges.Load(new PendingChangesRequest(path, "1.0"));
        Assert.True(loaded.Succeeded, loaded.Refusal?.Message);
        return loaded.Value!;
    }

    private static IReadOnlyList<SourceListEntry> Read(string path)
    {
        using var cache = new FwDataProjectLoader().LoadCache(path);
        return SourceList.Read(cache);
    }

    private static CommandOutcome<ApplyPendingResult> Apply(string path)
    {
        var runner = new InProcessRunnerLauncher(IsolatedRunner.Options(Root(path)));
        try
        {
            var applied = PendingChangesWorkflow.Apply(new ApplyPendingRequest(path, null, null),
                dryRunTimeout: TimeSpan.FromMinutes(2), runnerLauncher: runner);
            if (applied.Succeeded && applied.Value!.Applied)
                Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), Root(path)).Succeeded);
            return applied;
        }
        finally
        {
            runner.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static string Root(string projectPath)
    {
        var root = Path.Combine(Path.GetDirectoryName(projectPath)!, "source-staging-root");
        Directory.CreateDirectory(root);
        return root;
    }
}
