using Microsoft.Data.Sqlite;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// A project's Motif store made by another version of Motif is refused under one stable code that names the
/// store file, so the window can say which file to delete and a delete button has a code to hang off.
/// </summary>
public sealed class StoreOtherVersionRefusalTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-store-version-" + Guid.NewGuid().ToString("N"));

    public StoreOtherVersionRefusalTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void AStoreAtAnOlderSchemaIsRefusedByCodeWithItsPathAndLeftUntouched()
    {
        var project = Project("older");
        var storePath = Path.ChangeExtension(project, ".motif.db");
        Open(project);
        Execute(storePath, $"PRAGMA user_version = {MotifSchema.CurrentSchema - 1};");
        var before = File.ReadAllBytes(storePath);

        var result = ProjectStoreCommand.Run<string>(
            project, "1.0", (_, _) => CommandOutcome<string>.Success(string.Empty));

        Assert.False(result.Succeeded);
        Assert.Equal(RefusalCodes.StoreOtherVersion, result.Refusal!.Code);
        Assert.Equal(FailureReason.Refused, result.Refusal.Reason);
        Assert.Equal(Path.GetFullPath(storePath), result.Refusal.Facts["storePath"]);
        Assert.Equal(project, result.Refusal.Facts["fwDataPath"]);
        Assert.Equal(before, File.ReadAllBytes(storePath));
    }

    [Fact]
    public void AStoreAtANewerSchemaIsRefusedUnderTheSameCode()
    {
        var project = Project("newer-schema");
        var storePath = Path.ChangeExtension(project, ".motif.db");
        Open(project);
        Execute(storePath, $"PRAGMA user_version = {MotifSchema.CurrentSchema + 1};");

        var result = ProjectStoreCommand.Run<string>(
            project, "1.0", (_, _) => CommandOutcome<string>.Success(string.Empty));

        Assert.Equal(RefusalCodes.StoreOtherVersion, result.Refusal!.Code);
        Assert.Equal(Path.GetFullPath(storePath), result.Refusal.Facts["storePath"]);
    }

    [Fact]
    public void AStoreANewerMotifRequiresIsRefusedUnderTheSameCode()
    {
        var project = Project("newer-worker");
        var storePath = Path.ChangeExtension(project, ".motif.db");
        Open(project);
        Execute(storePath, "UPDATE MotifMetadata SET MinimumWorkerVersion = '99.0' WHERE Id = 1;");

        var result = ProjectStoreCommand.Run<string>(
            project, "1.0", (_, _) => CommandOutcome<string>.Success(string.Empty));

        Assert.Equal(RefusalCodes.StoreOtherVersion, result.Refusal!.Code);
        Assert.Equal(Path.GetFullPath(storePath), result.Refusal.Facts["storePath"]);
    }

    private string Project(string name)
    {
        var path = Path.Combine(_root, name + ".fwdata");
        File.WriteAllText(path, "<languageproject/>");
        return path;
    }

    private static void Open(string project) =>
        Assert.True(ProjectStoreCommand.Run<string>(
            project, "1.0", (_, _) => CommandOutcome<string>.Success(string.Empty)).Succeeded);

    private static void Execute(string databasePath, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
