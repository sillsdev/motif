using Microsoft.Data.Sqlite;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
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
        Assert.Equal("kept", (string)Scalar(storePath, "SELECT CreatedUtc FROM MotifMetadata WHERE Id = 1;"));
    }

    [Fact]
    public void AStoreSomethingElseHoldsOpenIsRefusedAndKept()
    {
        var project = Project("held");
        var storePath = StorePathOf(project);
        Open(project);
        using var holder = MotifDatabase.OpenOwned(storePath, Locator(project), MotifSchema.CurrentSchema,
            new Version(1, 0));
        using var concurrentHolder = MotifDatabase.OpenOwned(storePath, Locator(project), MotifSchema.CurrentSchema,
            new Version(1, 0));
        Assert.Equal(storePath, concurrentHolder.FullPath);
        using var connection = holder.OpenConnection();
        Execute(connection, $"PRAGMA user_version = {MotifSchema.CurrentSchema - 1};");

        var outcome = ProjectStoreReset.DeleteRefused(new ProjectStoreResetRequest(project), "1.0");

        Assert.False(outcome.Succeeded);
        Assert.Equal(RefusalCodes.ProjectBusy, outcome.Refusal!.Code);
        Assert.Equal(storePath, outcome.Refusal.Facts[RefusalFactNames.StorePath]);
        Assert.True(File.Exists(storePath));
        Assert.Equal((long)(MotifSchema.CurrentSchema - 1), Scalar(storePath, "PRAGMA user_version;"));
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
    public async Task ADatabaseCreatedWhileTheDeleteWaitsIsKept()
    {
        var project = Project("created");
        var storePath = StorePathOf(project);
        using var deleteWaiting = new ManualResetEventSlim();
        Task<CommandOutcome<string>> creating;
        Task<bool> deleting;

        // A write lock on the still-empty file pauses a real creator inside creation, holding the creation lock.
        using (var writer = Connection(storePath))
        {
            Execute(writer, "BEGIN IMMEDIATE;");
            creating = Task.Run(() => ProjectStoreCommand.Run<string>(
                project, "1.0", (_, _) => CommandOutcome<string>.Success(string.Empty)));
            Assert.True(SpinWait.SpinUntil(() => File.Exists(storePath + ".owner.lock"), TimeSpan.FromSeconds(10)));

            deleting = Task.Run(() => MotifDatabase.DeleteIfOtherVersion(storePath, Locator(project),
                MotifSchema.CurrentSchema, new Version(1, 0), onWaitingForOwnership: deleteWaiting.Set));
            Assert.True(deleteWaiting.Wait(TimeSpan.FromSeconds(10)));
            Assert.False(deleting.IsCompleted);
            Execute(writer, "ROLLBACK;");
        }

        Assert.True((await creating.WaitAsync(TimeSpan.FromSeconds(30))).Succeeded);
        Assert.False(await deleting.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.True(File.Exists(storePath));
        Assert.Equal((long)MotifSchema.CurrentSchema, Scalar(storePath, "PRAGMA user_version;"));
        Open(project);
    }

    [Fact]
    public async Task ADeleteThatWinsTheLockIsFollowedByACleanRecreate()
    {
        var project = Project("won");
        var storePath = StorePathOf(project);
        Open(project);
        Execute(storePath, $"PRAGMA user_version = {MotifSchema.CurrentSchema - 1};");
        using var deleteWaiting = new ManualResetEventSlim();
        Task<bool> deleting;

        using (HoldCreationLock(storePath))
        {
            deleting = Task.Run(() => MotifDatabase.DeleteIfOtherVersion(storePath, Locator(project),
                MotifSchema.CurrentSchema, new Version(1, 0), onWaitingForOwnership: deleteWaiting.Set));
            Assert.True(deleteWaiting.Wait(TimeSpan.FromSeconds(10)));

            var racing = ProjectStoreCommand.Run<string>(
                project, "1.0", (_, _) => CommandOutcome<string>.Success(string.Empty));
            Assert.Equal(RefusalCodes.StoreOtherVersion, racing.Refusal!.Code);
            Assert.False(deleting.IsCompleted);
        }

        Assert.True(await deleting.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.False(File.Exists(storePath));
        Open(project);
        Assert.Equal((long)MotifSchema.CurrentSchema, Scalar(storePath, "PRAGMA user_version;"));
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
        FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1,
        OperatingSystem.IsWindows() ? FileOptions.DeleteOnClose : FileOptions.None);

    private static SqliteConnection Connection(string databasePath)
    {
        var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        return connection;
    }

    private static void Execute(string databasePath, string sql)
    {
        using var connection = Connection(databasePath);
        Execute(connection, sql);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static ProjectLocator Locator(string project)
    {
        var full = Path.GetFullPath(project);
        return new ProjectLocator(full, Path.GetFileNameWithoutExtension(full));
    }

    private static object Scalar(string databasePath, string sql)
    {
        using var connection = Connection(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar()!;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
