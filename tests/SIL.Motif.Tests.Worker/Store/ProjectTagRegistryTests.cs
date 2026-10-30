using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SIL.Motif.Host.Store;
using Xunit;

namespace SIL.Motif.Tests.Store;

public sealed class ProjectTagRegistryTests : IDisposable
{
    private const int TaggedMachineSchema = 2;
    private const int OriginalMachineSchema = 1;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-project-tags-" + Guid.NewGuid().ToString("N"));

    public ProjectTagRegistryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void RecordingChoosesTheLanguageThenFolderAndKeepsAnExistingTag()
    {
        using var machine = MachineDatabase.Open(_root);
        var registry = new KnownProjectRegistry(machine);
        var seen = new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
        var first = Project("cebuano", "ceb");
        var second = Project("wolff", "ceb");

        registry.Record("first", first, seen);
        registry.Record("second", second, seen);

        Assert.Equal("ceb", ReadTag(machine, "first"));
        Assert.Equal("wolff", ReadTag(machine, "second"));

        WriteTag(machine, "second", "custom");
        File.WriteAllText(second, ProjectXml("eng"));
        registry.Record("second", second, seen.AddDays(1));

        Assert.Equal("custom", ReadTag(machine, "second"));
    }

    [Fact]
    public void RecordingRefusesInvalidOrTakenFolderFallbacksWithoutPersistingRows()
    {
        using var machine = MachineDatabase.Open(_root);
        var registry = new KnownProjectRegistry(machine);
        var seen = new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
        var languageOwner = Project("language-owner", "wol");
        registry.Record("language-owner", languageOwner, seen);

        var invalidFolder = Project("###", "wol");
        var invalid = Assert.Throws<ProjectTagException>(() => registry.Record("invalid-folder", invalidFolder, seen));
        Assert.Equal(ProjectTagProblem.Invalid, invalid.Problem);
        Assert.Null(registry.Find("invalid-folder"));

        var folderOwner = Project("folder-owner", "eng");
        registry.RecordWithTag("folder-owner", folderOwner, seen, "wolff");
        var takenFolder = Project("nested/wolff", "wol");
        var taken = Assert.Throws<ProjectTagException>(() => registry.Record("taken-folder", takenFolder, seen));
        Assert.Equal(ProjectTagProblem.Taken, taken.Problem);
        Assert.Null(registry.Find("taken-folder"));
    }

    [Fact]
    public async Task CompetingConnectionsGiveOneProjectTheTagAndLeaveTheLoserUnchanged()
    {
        using var firstMachine = MachineDatabase.Open(_root);
        using var secondMachine = MachineDatabase.Open(_root);
        var firstRegistry = new KnownProjectRegistry(firstMachine);
        var secondRegistry = new KnownProjectRegistry(secondMachine);
        var seen = new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
        var firstPath = Project("first", "eng");
        var secondPath = Project("second", "fra");
        firstRegistry.RecordWithTag("first", firstPath, seen, "first");
        firstRegistry.RecordWithTag("second", secondPath, seen, "second");

        using var barrier = new Barrier(2);
        var attempts = await Task.WhenAll(
            Task.Run(() => ClaimAfterBarrier(barrier, firstRegistry, "first", firstPath, seen.AddDays(1))),
            Task.Run(() => ClaimAfterBarrier(barrier, secondRegistry, "second", secondPath, seen.AddDays(2))));

        var winner = Assert.Single(attempts, attempt => attempt.Refusal is null);
        var loser = Assert.Single(attempts, attempt => attempt.Refusal is not null);
        Assert.Equal(ProjectTagProblem.Taken, loser.Refusal!.Problem);

        var first = firstRegistry.Find("first")!;
        var second = firstRegistry.Find("second")!;
        Assert.Equal(1, firstRegistry.List().Count(project => project.ProjectTag == "shared"));
        Assert.Equal("shared", firstRegistry.Find(winner.WorkspaceKey)!.ProjectTag);

        var loserBeforeTag = loser.WorkspaceKey == "first" ? "first" : "second";
        var loserBeforePath = loser.WorkspaceKey == "first" ? firstPath : secondPath;
        Assert.Equal(loserBeforeTag, loser.WorkspaceKey == "first" ? first.ProjectTag : second.ProjectTag);
        Assert.Equal(Path.GetFullPath(loserBeforePath), loser.WorkspaceKey == "first"
            ? first.FullFwDataPath : second.FullFwDataPath);
        Assert.Equal(seen, loser.WorkspaceKey == "first" ? first.LastSeenUtc : second.LastSeenUtc);
    }

    [Fact]
    public void ARegistryWithThePreviousMachineSchemaIsRefusedWithoutChangingItsBytes()
    {
        var path = Path.Combine(_root, "motif.db");
        CreatePreviousRegistry(path);
        var before = SHA256.HashData(File.ReadAllBytes(path));

        var refusal = Assert.Throws<MotifStoreVersionException>(() => MachineDatabase.Open(_root));

        Assert.Equal(TaggedMachineSchema, MachineSchema.CurrentSchema);
        Assert.Equal($"The Motif machine database at '{Path.GetFileName(path)}' is schema {OriginalMachineSchema}, but this build requires exactly schema {TaggedMachineSchema}. This store cannot be upgraded by this build; delete it and let Motif recreate it.",
            refusal.Message);
        Assert.Equal(Path.GetFullPath(path), refusal.StorePath);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
        Assert.Equal(OriginalMachineSchema, ReadUserVersion(path));
    }

    private string Project(string folder, string vernacular)
    {
        var directory = Path.Combine(_root, folder);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "project.fwdata");
        File.WriteAllText(path, ProjectXml(vernacular));
        return path;
    }

    private static string ProjectXml(string vernacular) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <languageproject version="7000072">
          <rt class="LangProject" guid="{Guid.NewGuid():D}" ownerguid="">
            <CurVernWss><Uni>{vernacular}</Uni></CurVernWss>
          </rt>
        </languageproject>
        """;

    private static ClaimOutcome ClaimAfterBarrier(
        Barrier barrier, KnownProjectRegistry registry, string workspaceKey, string path, DateTimeOffset seen)
    {
        if (!barrier.SignalAndWait(TimeSpan.FromSeconds(15)))
            throw new TimeoutException("Both project-tag claims did not reach the shared start barrier.");
        try
        {
            registry.RecordWithTag(workspaceKey, path, seen, "shared");
            return new ClaimOutcome(workspaceKey, null);
        }
        catch (ProjectTagException exception)
        {
            return new ClaimOutcome(workspaceKey, exception);
        }
    }

    private sealed record ClaimOutcome(string WorkspaceKey, ProjectTagException? Refusal);

    private static string ReadTag(MachineDatabase machine, string workspaceKey)
    {
        using var connection = machine.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ProjectTag FROM KnownProjects WHERE WorkspaceKey = $key;";
        command.Parameters.AddWithValue("$key", workspaceKey);
        return command.ExecuteScalar() as string ?? throw new InvalidDataException("The project tag was not stored.");
    }

    private static void WriteTag(MachineDatabase machine, string workspaceKey, string tag)
    {
        using var connection = machine.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE KnownProjects SET ProjectTag = $tag WHERE WorkspaceKey = $key;";
        command.Parameters.AddWithValue("$tag", tag);
        command.Parameters.AddWithValue("$key", workspaceKey);
        Assert.Equal(1, command.ExecuteNonQuery());
    }

    private static void CreatePreviousRegistry(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            PRAGMA application_id = {MachineSchema.ApplicationId};
            CREATE TABLE KnownProjects (
                WorkspaceKey TEXT PRIMARY KEY,
                FullFwDataPath TEXT NOT NULL,
                LastSeenUtc TEXT NOT NULL
            );
            CREATE TABLE Usage (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TimestampUtc TEXT NOT NULL,
                Command TEXT NOT NULL,
                ArgumentShapeJson TEXT NOT NULL
            );
            PRAGMA user_version = {OriginalMachineSchema};
            """;
        command.ExecuteNonQuery();
    }

    private static int ReadUserVersion(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
