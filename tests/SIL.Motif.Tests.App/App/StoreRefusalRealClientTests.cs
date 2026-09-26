using Avalonia.Input;
using Microsoft.Data.Sqlite;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Checks store refusals and recovery through the App's real command client.</summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class StoreRefusalRealClientTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _managedRoot = Path.Combine(Path.GetTempPath(), "Motif.StoreRefusal", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AnOldSchemaStoreIsRefusedAndOnceDeletedIsRecreated()
    {
        var projectPath = pristine.CopyProjectFile();
        var storePath = StorePath(projectPath);
        var projectBytes = File.ReadAllBytes(projectPath);
        var client = RealCommandClient.Create(_managedRoot);
        var initialRead = await client.GetCurrentBaselineAsync(new CurrentBaselineRequest(projectPath), CancellationToken.None);
        Assert.True(initialRead.Succeeded, initialRead.Refusal?.Message);
        Execute(storePath, $"PRAGMA user_version = {MotifSchema.CurrentSchema - 1};");

        var workspace = NewWorkspace(client);
        await workspace.SetProjectAsync(projectPath);

        var refusal = Assert.IsType<WindowRefusal>(workspace.Baseline.ShownRefusal);
        Assert.Equal(RefusalCodes.StoreOtherVersion, refusal.Code);
        Assert.Contains(storePath, refusal.Sentence, StringComparison.Ordinal);
        Assert.Null(workspace.Baseline.Token);
        Assert.False(workspace.Context.Evidence.Baseline?.HasBaseline);

        await workspace.Baseline.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(RefusalCodes.StoreOtherVersion, workspace.Baseline.ShownRefusal?.Code);
        Assert.Null(workspace.Baseline.Token);
        Assert.False(workspace.Context.Evidence.Baseline?.HasBaseline);

        workspace.DeleteRefusedStoreCommand.Execute(workspace.Baseline.ShownRefusal);
        Assert.True(workspace.IsConfirmingStoreDeletion);
        await workspace.ConfirmStoreDeletionCommand.ExecuteAsync(null);

        Assert.False(workspace.IsConfirmingStoreDeletion);
        Assert.Null(workspace.OpenRefusal);
        Assert.Null(workspace.Baseline.ShownRefusal);
        Assert.Null(workspace.Baseline.Token);
        Assert.True(File.Exists(storePath));
        Assert.Equal(MotifSchema.CurrentSchema, UserVersion(storePath));
        Assert.Equal(projectBytes, File.ReadAllBytes(projectPath));
    }

    [Fact]
    public async Task ACorruptStoreIsRefusedAndLeftByteForByte()
    {
        var projectPath = pristine.CopyProjectFile();
        var storePath = StorePath(projectPath);
        var bytes = Enumerable.Repeat((byte)'x', 4096).ToArray();
        File.WriteAllBytes(storePath, bytes);
        var client = RealCommandClient.Create(_managedRoot);

        var outcome = await client.GetCurrentBaselineAsync(new CurrentBaselineRequest(projectPath), CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal(RefusalCodes.StoreInconsistent, outcome.Refusal?.Code);
        Assert.Equal(bytes, File.ReadAllBytes(storePath));
    }

    private static WorkspaceShellViewModel NewWorkspace(ICommandClient client)
    {
        var selection = new SelectionViewModel(client);
        return new WorkspaceShellViewModel(new ProjectViewModel(client, new NoProjectPicker()),
            new BaselineViewModel(client), selection, new AssessViewModel(client, selection),
            new NoFolderPicker(), new NoDragSource(), client);
    }

    private static string StorePath(string projectPath)
    {
        var fullPath = Path.GetFullPath(projectPath);
        var project = new ProjectLocator(fullPath, Path.GetFileNameWithoutExtension(fullPath));
        return ProjectDatabaseCatalog.DatabasePathFor(project);
    }

    private static void Execute(string databasePath, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static int UserVersion(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void Dispose()
    {
        try { Directory.Delete(_managedRoot, recursive: true); }
        catch (IOException) { }
    }

    private sealed class NoProjectPicker : IProjectPicker
    {
        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class NoFolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class NoDragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) =>
            Task.FromResult(DragDropEffects.None);
    }
}
