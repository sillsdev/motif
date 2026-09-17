using Microsoft.Data.Sqlite;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// Covers the table that decides what a verb tells a caller when the paired store will not open.
/// </summary>
/// <remarks>
/// This is the module's real content: the exceptions come out of the store, and the reason a verb reports
/// decides whether a caller retries. Each case here is the store failing for one distinct cause.
/// </remarks>
public sealed class ProjectStoreCommandTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-store-command-" + Guid.NewGuid().ToString("N"));

    public ProjectStoreCommandTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void AnAbsentProjectFileIsAnInvocationErrorAndTheVerbNeverRuns()
    {
        var ran = false;

        var result = ProjectStoreCommand.Run<string>(Path.Combine(_root, "absent.fwdata"), "1.0",
            (_, _) => { ran = true; return CommandOutcome<string>.Success(string.Empty); });

        Assert.False(ran);
        Assert.False(result.Succeeded);
        Assert.Equal(FailureReason.InvalidArgument, result.Refusal!.Reason);
        Assert.Equal(1, FailureEnvelope.ExitCodeFor(result.Refusal.Reason));
        Assert.Contains("Project file not found", result.Refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileThatIsNotAMotifDatabaseIsStoreInconsistent()
    {
        var project = Project("garbled");
        File.WriteAllText(Path.ChangeExtension(project, ".motif.db"), "this is not a database");

        var result = ProjectStoreCommand.Run<string>(
            project, "1.0", (_, _) => CommandOutcome<string>.Success(string.Empty));

        // Exit 4: nothing the caller did, and nothing a retry fixes.
        Assert.False(result.Succeeded);
        Assert.Equal(FailureReason.StoreInconsistent, result.Refusal!.Reason);
        Assert.Equal(4, FailureEnvelope.ExitCodeFor(result.Refusal.Reason));
    }

    [Fact]
    public void AStoreThisBuildIsTooOldForIsRefusedRatherThanRetried()
    {
        var project = Project("newer");
        ProjectStoreCommand.Run<string>(project, "99.0", (_, _) => CommandOutcome<string>.Success(string.Empty));
        RequireWorkerVersion(Path.ChangeExtension(project, ".motif.db"), "99.0");

        var result = ProjectStoreCommand.Run<string>(
            project, "1.0", (_, _) => CommandOutcome<string>.Success(string.Empty));

        Assert.False(result.Succeeded);
        Assert.Equal(FailureReason.Refused, result.Refusal!.Reason);
        Assert.Equal(2, FailureEnvelope.ExitCodeFor(result.Refusal.Reason));
    }

    [Fact]
    public void AnUnavailableStoreIsAStableRefusalRatherThanBusy()
    {
        var project = Project("blocked");
        // A directory at the database path makes SQLite report a store I/O failure.
        Directory.CreateDirectory(Path.ChangeExtension(project, ".motif.db"));

        var result = ProjectStoreCommand.Run<string>(
            project, "1.0", (_, _) => CommandOutcome<string>.Success(string.Empty));

        Assert.False(result.Succeeded);
        Assert.Equal("project.store-io", result.Refusal!.Code);
        Assert.Equal(FailureReason.Refused, result.Refusal.Reason);
        Assert.Equal(2, FailureEnvelope.ExitCodeFor(result.Refusal.Reason));
    }

    [Fact]
    public void AContendedCreationLockIsBusyAndTheActionDoesNotRun()
    {
        var project = Project("contended");
        var storePath = Path.ChangeExtension(project, ".motif.db");
        using var owner = new FileStream(storePath + ".owner.lock",
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        var ran = false;

        var result = ProjectStoreCommand.Run<string>(project, "1.0", (_, _) =>
        {
            ran = true;
            return CommandOutcome<string>.Success(string.Empty);
        });

        Assert.False(ran);
        Assert.False(result.Succeeded);
        Assert.Equal("project.busy", result.Refusal!.Code);
        Assert.Equal(FailureReason.Busy, result.Refusal.Reason);
        Assert.Equal(3, FailureEnvelope.ExitCodeFor(result.Refusal.Reason));
        Assert.False(File.Exists(storePath));
    }

    [Fact]
    public void AnActionOutputIoFailureIsAStableRefusalRatherThanProjectBusy()
    {
        var project = Project("output-failure");

        var result = ProjectStoreCommand.Run<string>(project, "1.0",
            (_, _) => throw new UnauthorizedAccessException("The output directory is read-only."));

        Assert.False(result.Succeeded);
        Assert.Equal("project.operation-io", result.Refusal!.Code);
        Assert.Equal(FailureReason.Refused, result.Refusal.Reason);
        Assert.Equal("The output directory is read-only.", result.Refusal.Message);
        Assert.Equal(project, result.Refusal.Facts["fwDataPath"]);
        Assert.Equal(2, FailureEnvelope.ExitCodeFor(result.Refusal.Reason));
    }

    [Fact]
    public void AnActionDiskFailurePreservesItsRecoveryMessage()
    {
        var project = Project("disk-failure");

        var result = ProjectStoreCommand.Run<string>(project, "1.0",
            (_, _) => throw new IOException("The output volume is full."));

        Assert.False(result.Succeeded);
        Assert.Equal("project.operation-io", result.Refusal!.Code);
        Assert.Equal(FailureReason.Refused, result.Refusal.Reason);
        Assert.Equal("The output volume is full.", result.Refusal.Message);
    }

    [Fact]
    public void AVerbGetsAnOpenStoreAndItsOwnResultIsReturnedUntouched()
    {
        var project = Project("working");

        var result = ProjectStoreCommand.Run<string>(project, "1.0", (database, located) =>
        {
            Assert.NotNull(database);
            Assert.Equal("working", located.FieldWorksProjectIdentity);
            return CommandOutcome<string>.Success("the verb's own output");
        });

        Assert.True(result.Succeeded);
        Assert.Equal("the verb's own output", result.Value);
        Assert.True(File.Exists(Path.ChangeExtension(project, ".motif.db")));
    }

    [Fact]
    public void AMalformedProductVersionOpensTheStoreRatherThanRefusingTheVerb()
    {
        var result = ProjectStoreCommand.Run<string>(Project("loose"), "not-a-version",
            (_, _) => CommandOutcome<string>.Success(string.Empty));

        Assert.True(result.Succeeded);
    }

    private string Project(string name)
    {
        var path = Path.Combine(_root, name + ".fwdata");
        File.WriteAllText(path, "<languageproject/>");
        return path;
    }

    private static void RequireWorkerVersion(string databasePath, string version)
    {
        using var connection = new SqliteConnection("Data Source=" + databasePath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE MotifMetadata SET MinimumWorkerVersion = $version WHERE Id = 1;";
        command.Parameters.AddWithValue("$version", version);
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
