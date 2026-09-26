using Avalonia.Input;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Host.LcmUtils;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(LcmCacheTestCollection.Name)]
public sealed class PendingChangesWindowActivationTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task ActivatingTheWindowWithPendingChangesWhileHeldDoesNotFault()
    {
        string fwDataPath;
        Guid wordformId = Guid.Empty;
        using (var cache = pristine.NewScratch())
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                wordformId = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("activation-word", cache.DefaultVernWs)).Guid);
            new FwDataProjectLoader().Save(cache);
            fwDataPath = cache.ProjectId.Path;
        }
        var managedRoot = Path.Combine(Path.GetTempPath(), "SIL.Motif.PendingChangesWindowActivationTests",
            Guid.NewGuid().ToString("N"));
        var baseline = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), managedRoot);
        Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
        var commands = RealCommandClient.Create(managedRoot);
        var loaded = await commands.LoadPendingChangesAsync(new PendingChangesRequest(fwDataPath,
            SIL.Motif.Host.MotifProductVersion.CurrentText), CancellationToken.None);
        Assert.True(loaded.Succeeded, loaded.Refusal?.Message);
        var put = await commands.PutPendingChangeAsync(new PutPendingChangeRequest(fwDataPath,
            SIL.Motif.Host.MotifProductVersion.CurrentText, loaded.Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                CanonicalId.FromGuid(wordformId).Value, "activation-word")), CancellationToken.None);
        Assert.True(put.Succeeded, put.Refusal?.Message);

        var selection = new SelectionViewModel(commands);
        var workspace = new WorkspaceShellViewModel(
            new ProjectViewModel(commands, new TestProjectPicker()), new BaselineViewModel(commands), selection,
            new AssessViewModel(commands, selection), new TestFolderPicker(), new TestDragSource(), commands);
        try
        {
            var openProjectCommand = workspace.Context.OpenProjectCommand;
            Assert.NotNull(openProjectCommand);
            await openProjectCommand.ExecuteAsync(fwDataPath);
            using var held = new FileStream(fwDataPath + ".lock", FileMode.Create,
                FileAccess.ReadWrite, FileShare.None);

            await workspace.CheckFreshnessAsync();

            Assert.False(workspace.Context.Changes.HasError);
            Assert.Single(workspace.Context.Changes.Items);
        }
        finally
        {
            await workspace.DisposeAsync();
            try { Directory.Delete(managedRoot, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class TestProjectPicker : IProjectPicker
    {
        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class TestFolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class TestDragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(PointerPressedEventArgs trigger,
            IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) =>
            Task.FromResult(DragDropEffects.None);
    }
}
