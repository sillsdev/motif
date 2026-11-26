using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class ProjectStoreOperationFactsTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-project-store-operation-facts-" + Guid.NewGuid().ToString("N"));

    public ProjectStoreOperationFactsTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void ExistingReadRefusesAnotherSchemaVersionWithItsStorePathAndPreservesStoredFiles(int schemaOffset)
    {
        var projectPath = CreateProject("schema-version");
        var created = ProjectStoreCommand.Run(projectPath, "1.0",
            (_, _) => CommandOutcome<string>.Success("created"));
        Assert.True(created.Succeeded);
        var project = new ProjectLocator(projectPath, Path.GetFileNameWithoutExtension(projectPath));
        var storePath = ProjectDatabaseCatalog.DatabasePathFor(project);
        using (var connection = new SqliteConnection($"Data Source={storePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA user_version = {MotifSchema.CurrentSchema + schemaOffset};";
            command.ExecuteNonQuery();
        }
        string[] StoredFiles() => Directory.GetFiles(_root).Where(path =>
            !path.EndsWith("-wal", StringComparison.Ordinal) && !path.EndsWith("-shm", StringComparison.Ordinal)).ToArray();
        var before = StoredFiles().ToDictionary(path => path, File.ReadAllBytes);
        var invoked = false;

        var outcome = ProjectStoreCommand.ReadExisting(projectPath,
            CommandOutcome<string>.Success("absent"), (_, _) =>
            {
                invoked = true;
                return CommandOutcome<string>.Success("read");
            });

        Assert.False(invoked);
        Assert.False(outcome.Succeeded);
        Assert.Equal("store.other-version", outcome.Refusal!.Code);
        Assert.Equal(FailureReason.Refused, outcome.Refusal.Reason);
        Assert.Equal(storePath, outcome.Refusal.Facts[RefusalFactNames.StorePath]);
        Assert.Equal(projectPath, outcome.Refusal.Facts["fwDataPath"]);
        if (schemaOffset < 0) AssertUpgradeAdvice(outcome.Refusal.Message, MotifSchema.CurrentSchema + schemaOffset);
        Assert.Equal(before.Keys.Order(), StoredFiles().Order());
        foreach (var file in before)
            Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
    }

    [Fact]
    public void ExistingReadOfAnOlderStoreOnTheConnectionPathAdvisesUpgradeToo()
    {
        var projectPath = CreateProject("older-connection");
        Assert.True(ProjectStoreCommand.Run(projectPath, "1.0", (_, _) => CommandOutcome<string>.Success("created")).Succeeded);
        var storePath = ProjectDatabaseCatalog.DatabasePathFor(
            new ProjectLocator(projectPath, Path.GetFileNameWithoutExtension(projectPath)));
        using (var connection = new SqliteConnection($"Data Source={storePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA user_version = {MotifSchema.CurrentSchema - 1};";
            command.ExecuteNonQuery();
        }

        var outcome = ProjectStoreCommand.ReadExisting(projectPath, CommandOutcome<string>.Success("absent"),
            (_, _, _) => CommandOutcome<string>.Success("read"));

        Assert.Equal("store.other-version", outcome.Refusal!.Code);
        Assert.Equal("true", outcome.Refusal.Facts[RefusalFactNames.StoreUpgradable]);
        AssertUpgradeAdvice(outcome.Refusal.Message, MotifSchema.CurrentSchema - 1);
    }

    internal static void AssertUpgradeAdvice(string message, int olderSchema)
    {
        Assert.Contains($"schema {olderSchema}", message);
        Assert.Contains($"schema {MotifSchema.CurrentSchema}", message);
        Assert.Contains("backing it up first", message);
        Assert.Contains("motif store upgrade", message);
        Assert.Contains("--source", message);
        Assert.Contains("--out", message);
        Assert.DoesNotContain("recreate", message);
    }

    [Fact]
    public void AnnotatedIOExceptionKeepsOperationIoRefusalAndAddsItsDiagnostics()
    {
        const int expectedHResult = unchecked((int)0x80070020);
        var exception = new IOException("controlled publication failure", expectedHResult);
        exception.Data["baselinePublicationPhase"] = "archive-extraction";
        var projectPath = CreateProject("io");

        var outcome = ProjectStoreCommand.Run<string>(
            projectPath, "1.0", (_, _) => throw exception);

        Assert.False(outcome.Succeeded);
        var refusal = outcome.Refusal!;
        Assert.Equal("project.operation-io", refusal.Code);
        Assert.Equal(FailureReason.Refused, refusal.Reason);
        Assert.Equal(exception.Message, refusal.Facts["exceptionDetail"]);
        Assert.Equal(projectPath, refusal.Facts["fwDataPath"]);
        Assert.Equal("archive-extraction", refusal.Facts["baselinePublicationPhase"]);
        Assert.Equal("0x80070020", refusal.Facts["exceptionHResult"]);
        Assert.Equal(typeof(IOException).FullName, refusal.Facts["exceptionType"]);
        Assert.Equal(5, refusal.Facts.Count);
    }

    [Fact]
    public void AnnotatedUnauthorizedAccessKeepsOperationIoRefusalAndAddsItsDiagnostics()
    {
        const int expectedHResult = unchecked((int)0x80070005);
        var exception = new UnauthorizedAccessException("controlled access failure");
        Assert.Equal(expectedHResult, exception.HResult);
        exception.Data["baselinePublicationPhase"] = "incoming-reclamation";
        var projectPath = CreateProject("unauthorized");

        var outcome = ProjectStoreCommand.Run<string>(
            projectPath, "1.0", (_, _) => throw exception);

        Assert.False(outcome.Succeeded);
        var refusal = outcome.Refusal!;
        Assert.Equal("project.operation-io", refusal.Code);
        Assert.Equal(FailureReason.Refused, refusal.Reason);
        Assert.Equal(exception.Message, refusal.Facts["exceptionDetail"]);
        Assert.Equal(projectPath, refusal.Facts["fwDataPath"]);
        Assert.Equal("incoming-reclamation", refusal.Facts["baselinePublicationPhase"]);
        Assert.Equal("0x80070005", refusal.Facts["exceptionHResult"]);
        Assert.Equal(typeof(UnauthorizedAccessException).FullName, refusal.Facts["exceptionType"]);
        Assert.Equal(5, refusal.Facts.Count);
    }

    [Fact]
    public void UnannotatedIOExceptionRetainsTheOriginalOperationIoFacts()
    {
        var exception = new IOException("ordinary operation failure");
        var projectPath = CreateProject("unannotated");

        var outcome = ProjectStoreCommand.Run<string>(
            projectPath, "1.0", (_, _) => throw exception);

        Assert.False(outcome.Succeeded);
        var refusal = outcome.Refusal!;
        Assert.Equal("project.operation-io", refusal.Code);
        Assert.Equal(FailureReason.Refused, refusal.Reason);
        Assert.Equal(exception.Message, refusal.Facts["exceptionDetail"]);
        Assert.Equal(2, refusal.Facts.Count);
        Assert.Equal(projectPath, refusal.Facts["fwDataPath"]);
    }

    [Fact]
    public void AStoreWithoutItsUseLockIsRefusedWithTheMissingLockNamed()
    {
        var projectPath = CreateProject("no-lock");
        Assert.True(ProjectStoreCommand.Run(projectPath, "1.0", (_, _) => CommandOutcome<string>.Success("created")).Succeeded);
        var project = new ProjectLocator(projectPath, Path.GetFileNameWithoutExtension(projectPath));
        var lockPath = ProjectDatabaseCatalog.DatabasePathFor(project) + ".use.lock";
        File.Delete(lockPath);

        var outcome = ProjectStoreCommand.ReadExisting(projectPath,
            CommandOutcome<string>.Success("absent"), (_, _) => CommandOutcome<string>.Success("read"));

        Assert.False(outcome.Succeeded);
        Assert.Equal("project.operation-io", outcome.Refusal!.Code);
        Assert.Equal(lockPath, outcome.Refusal.Facts[RefusalFactNames.StoreLockMissing]);
    }

    private string CreateProject(string name)
    {
        var path = Path.Combine(_root, name + ".fwdata");
        File.WriteAllText(path, "private test project");
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
