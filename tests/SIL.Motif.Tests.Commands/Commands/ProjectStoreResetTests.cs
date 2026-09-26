using Microsoft.Data.Sqlite;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Deleting a refused store removes exactly the file a store refused as made by another version of Motif, so the
/// next open recreates it; it deletes nothing beside it, and never a store this version can use.
/// </summary>
public sealed class ProjectStoreResetTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-store-reset-" + Guid.NewGuid().ToString("N"));

    public ProjectStoreResetTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ARefusedStoreIsDeletedAloneAndTheNextOpenRecreatesIt()
    {
        var project = Project("older");
        var storePath = StorePathOf(project);
        Open(project);
        Execute(storePath, $"PRAGMA user_version = {MotifSchema.CurrentSchema - 1};");
        var neighbour = Path.Combine(_root, "neighbour.motif.db");
        File.WriteAllText(neighbour, "another project's store");
        var projectBytes = File.ReadAllBytes(project);
        var before = Directory.GetFiles(_root).Where(path => path != storePath).Order().ToArray();

        var outcome = ProjectStoreReset.DeleteRefused(new ProjectStoreResetRequest(project), "1.0");

        Assert.True(outcome.Succeeded);
        Assert.True(outcome.Value!.Deleted);
        Assert.Equal(storePath, outcome.Value.StorePath);
        Assert.False(File.Exists(storePath));
        Assert.Equal(before, Directory.GetFiles(_root).Order().ToArray());
        Assert.Equal(projectBytes, File.ReadAllBytes(project));
        Open(project);
    }

    [Fact]
    public void AStoreANewerMotifRequiresIsDeletedToo()
    {
        var project = Project("newer-worker");
        var storePath = StorePathOf(project);
        Open(project);
        Execute(storePath, "UPDATE MotifMetadata SET MinimumWorkerVersion = '99.0' WHERE Id = 1;");

        var outcome = ProjectStoreReset.DeleteRefused(new ProjectStoreResetRequest(project), "1.0");

        Assert.True(outcome.Value!.Deleted);
        Assert.False(File.Exists(storePath));
    }

    [Fact]
    public void AStoreThisVersionCanUseIsKept()
    {
        var project = Project("current");
        var storePath = StorePathOf(project);
        Open(project);
        Execute(storePath, "UPDATE MotifMetadata SET CreatedUtc = 'kept' WHERE Id = 1;");

        var outcome = ProjectStoreReset.DeleteRefused(new ProjectStoreResetRequest(project), "1.0");

        Assert.True(outcome.Succeeded);
        Assert.False(outcome.Value!.Deleted);
        Assert.Equal(storePath, outcome.Value.StorePath);
        Assert.True(File.Exists(storePath));
        Assert.Equal("kept", Scalar(storePath, "SELECT CreatedUtc FROM MotifMetadata WHERE Id = 1;"));
    }

    [Fact]
    public void AStoreSomethingElseHoldsOpenIsRefusedAndKept()
    {
        var project = Project("held");
        var storePath = StorePathOf(project);
        Open(project);
        Execute(storePath, $"PRAGMA user_version = {MotifSchema.CurrentSchema - 1};");
        using var holder = Connection(storePath);
        using (var read = holder.CreateCommand())
        {
            read.CommandText = "PRAGMA user_version;";
            read.ExecuteScalar();
        }

        var outcome = ProjectStoreReset.DeleteRefused(new ProjectStoreResetRequest(project), "1.0");

        Assert.False(outcome.Succeeded);
        Assert.Equal(RefusalCodes.ProjectStoreIo, outcome.Refusal!.Code);
        Assert.Equal(storePath, outcome.Refusal.Facts[RefusalFactNames.StorePath]);
        Assert.True(File.Exists(storePath));
    }

    [Fact]
    public void NothingIsDeletedWhileAnotherOpenerHoldsTheStoresCreationLock()
    {
        var project = Project("locked");
        var storePath = StorePathOf(project);
        Open(project);
        Execute(storePath, $"PRAGMA user_version = {MotifSchema.CurrentSchema - 1};");
        using var creationLock = HoldCreationLock(storePath);

        var outcome = ProjectStoreReset.DeleteRefused(
            new ProjectStoreResetRequest(project), "1.0", TimeSpan.FromMilliseconds(100));

        Assert.Equal(RefusalCodes.ProjectBusy, outcome.Refusal!.Code);
        Assert.True(File.Exists(storePath));
    }

    [Fact]
    public async Task AStoreRecreatedWhileTheDeleteWaitsIsKept()
    {
        var project = Project("recreated");
        var storePath = StorePathOf(project);
        Open(project);
        Execute(storePath, "UPDATE MotifMetadata SET CreatedUtc = 'recreated' WHERE Id = 1;");
        var recreated = Path.Combine(_root, "recreated.snapshot");
        File.Copy(storePath, recreated);
        Execute(storePath, $"PRAGMA user_version = {MotifSchema.CurrentSchema - 1};");

        Task<CommandOutcome<ProjectStoreResetResponse>> deleting;
        using (HoldCreationLock(storePath))
        {
            deleting = Task.Run(() => ProjectStoreReset.DeleteRefused(new ProjectStoreResetRequest(project), "1.0"));
            File.Copy(recreated, storePath, overwrite: true);
        }
        var outcome = await deleting.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(outcome.Succeeded);
        Assert.False(outcome.Value!.Deleted);
        Assert.Equal("recreated", Scalar(storePath, "SELECT CreatedUtc FROM MotifMetadata WHERE Id = 1;"));
    }

    [Fact]
    public void ADamagedStoreIsRefusedAsItsOwnFailureAndLeftByteForByte()
    {
        var project = Project("damaged");
        var storePath = StorePathOf(project);
        File.WriteAllBytes(storePath, Enumerable.Repeat((byte)'x', 4096).ToArray());
        var before = File.ReadAllBytes(storePath);

        var outcome = ProjectStoreReset.DeleteRefused(new ProjectStoreResetRequest(project), "1.0");

        Assert.Equal(RefusalCodes.StoreInconsistent, outcome.Refusal!.Code);
        Assert.Equal(before, File.ReadAllBytes(storePath));
    }

    [Fact]
    public void AStoreRegisteredToAnotherProjectIsRefusedAndLeftByteForByte()
    {
        var project = Project("registered");
        var storePath = StorePathOf(project);
        Open(project);
        Execute(storePath, "UPDATE MotifMetadata SET FieldWorksProjectIdentity = 'someone-else' WHERE Id = 1;");
        var before = File.ReadAllBytes(storePath);

        var outcome = ProjectStoreReset.DeleteRefused(new ProjectStoreResetRequest(project), "1.0");

        Assert.Equal(RefusalCodes.StoreInconsistent, outcome.Refusal!.Code);
        Assert.Equal(before, File.ReadAllBytes(storePath));
    }

    [Fact]
    public void AMissingProjectIsRefusedAndNothingIsCreatedOrDeleted()
    {
        var project = Path.Combine(_root, "absent.fwdata");

        var outcome = ProjectStoreReset.DeleteRefused(new ProjectStoreResetRequest(project), "1.0");

        Assert.Equal(RefusalCodes.ProjectNotFound, outcome.Refusal!.Code);
        Assert.Equal(FailureReason.InvalidArgument, outcome.Refusal.Reason);
        Assert.Empty(Directory.GetFiles(_root));
    }

    private string Project(string name)
    {
        var path = Path.Combine(_root, name + ".fwdata");
        File.WriteAllText(path, "<languageproject/>");
        return path;
    }

    private static string StorePathOf(string project) => Path.GetFullPath(Path.ChangeExtension(project, ".motif.db"));

    private static void Open(string project) =>
        Assert.True(ProjectStoreCommand.Run<string>(
            project, "1.0", (_, _) => CommandOutcome<string>.Success(string.Empty)).Succeeded);

    // The lock a store's first opener takes while it creates the store; holding it stands in for that opener.
    private static FileStream HoldCreationLock(string storePath) => new(storePath + ".owner.lock",
        FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);

    private static SqliteConnection Connection(string databasePath)
    {
        var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        return connection;
    }

    private static void Execute(string databasePath, string sql)
    {
        using var connection = Connection(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string Scalar(string databasePath, string sql)
    {
        using var connection = Connection(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (string)command.ExecuteScalar()!;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
