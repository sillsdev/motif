using Microsoft.Data.Sqlite;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// <c>store relocate</c> points the Motif store of a copied project at the copy, but only for the same project, only
/// with confirmation, and only while neither the project nor the store is held.
/// </summary>
public sealed class StoreRelocateCommandTests : IDisposable
{
    private const string ProjectGuid = "0d1f9e4a-5b3c-4a8e-9f10-2c3d4e5f6a7b";
    private const string OtherGuid = "9a8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-relocate-" + Guid.NewGuid().ToString("N"));

    public StoreRelocateCommandTests() => Directory.CreateDirectory(_root);

    private string Original => Path.Combine(_root, "original", "Koro.fwdata");

    private string Copy => Path.Combine(_root, "copy", "Koro.fwdata");

    [Fact]
    public void TheSameProjectAtANewPathIsRelocatedAndTheRelocationIsRecorded()
    {
        CopyProject(ProjectGuid);
        var at = new DateTimeOffset(2026, 11, 16, 9, 30, 0, TimeSpan.Zero);

        var result = StoreRelocateCommand.Run(Confirmed(Copy), new FixedClock(at));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.True(result.Value!.Changed);
        Assert.Equal(Path.GetFullPath(Original), result.Value.OldProjectPath);
        Assert.Equal(Path.GetFullPath(Copy), result.Value.NewProjectPath);
        Assert.Equal("project-guid", result.Value.IdentityEvidence);
        Assert.Equal(at, result.Value.RelocatedUtc);
        var opened = ProjectStoreCommand.Run(Copy, MotifProductVersion.CurrentText,
            (_, project) => CommandOutcome<string>.Success(project.FullFwDataPath));
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        var rows = Query(StoreOf(Copy), "SELECT OldFwDataPath, NewFwDataPath, RelocatedUtc, Basis FROM StoreRelocations;");
        Assert.Equal([$"{Path.GetFullPath(Original)}|{Path.GetFullPath(Copy)}|{at:O}|project-guid"], rows);
        Assert.True(result.Value.OldProjectStillExists);
    }

    [Fact]
    public void RelocatingAgainToTheSamePathChangesAndRecordsNothing()
    {
        CopyProject(ProjectGuid);
        Assert.True(StoreRelocateCommand.Run(Confirmed(Copy)).Succeeded);

        var again = StoreRelocateCommand.Run(Confirmed(Copy));

        Assert.True(again.Succeeded, again.Refusal?.Message);
        Assert.False(again.Value!.Changed);
        Assert.Single(Query(StoreOf(Copy), "SELECT RelocationId FROM StoreRelocations;"));
    }

    [Fact]
    public void ADifferentProjectIsRefusedAndTheStoreIsLeftAsItWas()
    {
        CopyProject(OtherGuid);
        var before = File.ReadAllBytes(StoreOf(Copy));

        var result = StoreRelocateCommand.Run(Confirmed(Copy));

        Assert.False(result.Succeeded);
        Assert.Equal(RefusalCodes.StoreOtherProject, result.Refusal!.Code);
        Assert.Contains(OtherGuid, result.Refusal.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(StoreOf(Copy)));
        Assert.Equal(Path.GetFullPath(Original), Query(StoreOf(Copy), "SELECT FullFwDataPath FROM MotifMetadata;").Single());
    }

    [Fact]
    public void AStoreWhoseBaselinesNameAnotherProjectIsRefusedEvenWhenTheOldFileIsGone()
    {
        CopyProject(OtherGuid);
        File.Delete(Original);
        InsertBaseline(StoreOf(Copy), ProjectGuid);

        var result = StoreRelocateCommand.Run(Confirmed(Copy));

        Assert.False(result.Succeeded);
        Assert.Equal(RefusalCodes.StoreOtherProject, result.Refusal!.Code);
    }

    [Fact]
    public void AStoreWhoseBaselinesNameTheProjectIsRelocatedAndItsBaselinesFollow()
    {
        CopyProject(ProjectGuid);
        File.Delete(Original);
        InsertBaseline(StoreOf(Copy), ProjectGuid);

        var result = StoreRelocateCommand.Run(Confirmed(Copy));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal("project-guid", result.Value!.IdentityEvidence);
        Assert.Equal(Path.GetFullPath(Copy), Query(StoreOf(Copy), "SELECT FwDataPath FROM Baselines;").Single());
    }

    [Fact]
    public void WithNoGuidEvidenceTheRelocationIsRefusedAndNamesTheFlag()
    {
        CopyProject(ProjectGuid);
        File.Delete(Original);
        var before = File.ReadAllBytes(StoreOf(Copy));

        var result = StoreRelocateCommand.Run(Confirmed(Copy));

        Assert.False(result.Succeeded);
        Assert.Equal(RefusalCodes.StoreRelocateIdentityUnproven, result.Refusal!.Code);
        Assert.Contains("--match-by-name", result.Refusal.Message, StringComparison.Ordinal);
        Assert.Contains(ProjectGuid, result.Refusal.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(StoreOf(Copy)));
    }

    [Fact]
    public void WithNoGuidEvidenceTheMatchByNameFlagRelocatesAndRecordsTheBasis()
    {
        CopyProject(ProjectGuid);
        File.Delete(Original);

        var result = StoreRelocateCommand.Run(Confirmed(Copy) with { MatchByName = true });

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal("name", result.Value!.IdentityEvidence);
        Assert.False(result.Value.OldProjectStillExists);
        Assert.Equal(["name"], Query(StoreOf(Copy), "SELECT Basis FROM StoreRelocations;"));
    }

    [Fact]
    public void TheMatchByNameFlagDoesNotOverrideAGuidThatDiffers()
    {
        CopyProject(OtherGuid);

        var result = StoreRelocateCommand.Run(Confirmed(Copy) with { MatchByName = true });

        Assert.False(result.Succeeded);
        Assert.Equal(RefusalCodes.StoreOtherProject, result.Refusal!.Code);
    }

    [Fact]
    public void AnOlderSchemaStoreIsRelocatedThenUpgradedByTheNormalPathKeepingItsBackupAndRow()
    {
        CopyProject(ProjectGuid);
        const int olderSchema = 71;
        using (var old = new SqliteConnection($"Data Source={StoreOf(Copy)};Pooling=False"))
        {
            old.Open();
            using var command = old.CreateCommand();
            command.CommandText = $"PRAGMA user_version = {olderSchema}; DROP TABLE StoreRelocations;";
            command.ExecuteNonQuery();
        }
        var at = new DateTimeOffset(2026, 11, 16, 9, 30, 0, TimeSpan.Zero);

        var relocated = StoreRelocateCommand.Run(Confirmed(Copy), new FixedClock(at));

        Assert.True(relocated.Succeeded, relocated.Refusal?.Message);
        var opened = ProjectStoreCommand.Run(Copy, MotifProductVersion.CurrentText,
            (_, project) => CommandOutcome<string>.Success(project.FullFwDataPath));
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        Assert.True(File.Exists(SIL.Motif.Tests.TestFixtures.PreUpgradeBackups.StoreOf(StoreOf(Copy))));
        Assert.Equal([$"{Path.GetFullPath(Original)}|{Path.GetFullPath(Copy)}|{at:O}|project-guid"],
            Query(StoreOf(Copy), "SELECT OldFwDataPath, NewFwDataPath, RelocatedUtc, Basis FROM StoreRelocations;"));
    }

    [Fact]
    public void AStoreRegisteredToADifferentlyNamedProjectIsRefused()
    {
        CopyProject(ProjectGuid);
        var renamed = Path.Combine(_root, "copy", "Other.fwdata");
        File.Move(Copy, renamed);
        File.Move(StoreOf(Copy), StoreOf(renamed));

        var result = StoreRelocateCommand.Run(Confirmed(renamed));

        Assert.False(result.Succeeded);
        Assert.Equal(RefusalCodes.StoreOtherProject, result.Refusal!.Code);
        Assert.Contains("Koro", result.Refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingProjectPathIsRefusedAsNotFound()
    {
        var result = StoreRelocateCommand.Run(Confirmed(Path.Combine(_root, "nowhere", "Koro.fwdata")));

        Assert.False(result.Succeeded);
        Assert.Equal(RefusalCodes.ProjectNotFound, result.Refusal!.Code);
    }

    [Fact]
    public void AProjectWithNoStoreBesideItIsRefused()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Copy)!);
        File.WriteAllText(Copy, ProjectText(ProjectGuid));

        var result = StoreRelocateCommand.Run(Confirmed(Copy));

        Assert.False(result.Succeeded);
        Assert.Equal(RefusalCodes.StoreRelocateNoStore, result.Refusal!.Code);
    }

    [Fact]
    public void AHeldProjectIsRefusedAndTheStoreIsLeftAsItWas()
    {
        CopyProject(ProjectGuid);
        var before = File.ReadAllBytes(StoreOf(Copy));
        using var holder = new FileStream(Copy, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = StoreRelocateCommand.Run(Confirmed(Copy));

        Assert.False(result.Succeeded);
        Assert.Equal(RefusalCodes.ProjectInUse, result.Refusal!.Code);
        Assert.Equal(FailureReason.Busy, result.Refusal.Reason);
        holder.Dispose();
        Assert.Equal(before, File.ReadAllBytes(StoreOf(Copy)));
    }

    [Fact]
    public void AStoreAnotherMotifProcessHoldsOpenIsRefusedAndLeftAsItWas()
    {
        CopyProject(ProjectGuid);
        var before = File.ReadAllBytes(StoreOf(Copy));
        // The lease every open of the store holds; an exclusive one cannot be taken while it is.
        using var lease = new FileStream(StoreOf(Copy) + ".use.lock", FileMode.OpenOrCreate, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.None);

        var result = StoreRelocateCommand.Run(Confirmed(Copy));

        Assert.False(result.Succeeded);
        Assert.Equal(RefusalCodes.ProjectBusy, result.Refusal!.Code);
        lease.Dispose();
        Assert.Equal(before, File.ReadAllBytes(StoreOf(Copy)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("yes")]
    public void WithoutTheConfirmationPhraseNothingChangesAndThePhraseIsShown(string? confirmation)
    {
        CopyProject(ProjectGuid);
        var before = File.ReadAllBytes(StoreOf(Copy));

        var result = StoreRelocateCommand.Run(new StoreRelocateRequest(Copy, confirmation));

        Assert.False(result.Succeeded);
        Assert.Equal(RefusalCodes.StoreRelocateConfirmationRequired, result.Refusal!.Code);
        Assert.Contains(StoreRelocateCommand.ConfirmationPhrase, result.Refusal.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(StoreOf(Copy)));
    }

    [Fact]
    public void TheCopiedStoreRefusalNamesTheCommandThatFixesIt()
    {
        CopyProject(ProjectGuid);

        var refused = ProjectStoreCommand.Run(Copy, MotifProductVersion.CurrentText,
            (_, _) => CommandOutcome<string>.Success("opened"));

        Assert.False(refused.Succeeded);
        Assert.Equal(RefusalCodes.StoreOtherProject, refused.Refusal!.Code);
        Assert.Contains("motif store relocate --project", refused.Refusal.Message, StringComparison.Ordinal);
        Assert.Contains(StoreRelocateCommand.ConfirmationPhrase, refused.Refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANewStoreHasAnEmptyRelocationHistory()
    {
        CopyProject(ProjectGuid);

        Assert.Empty(Query(StoreOf(Copy), "SELECT RelocationId FROM StoreRelocations;"));
    }

    private StoreRelocateRequest Confirmed(string project) =>
        new(project, StoreRelocateCommand.ConfirmationPhrase);

    private static string StoreOf(string project) => Path.ChangeExtension(project, ".motif.db");

    private static string ProjectText(string guid) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<languageproject>\n" +
        $"<rt class=\"LangProject\" guid=\"{guid}\" lastModified=\"2026-01-01\">\n</rt>\n</languageproject>\n";

    // A project and its store made in one folder, then both copied to another, as a person copies a project.
    private void CopyProject(string copyGuid)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Original)!);
        Directory.CreateDirectory(Path.GetDirectoryName(Copy)!);
        File.WriteAllText(Original, ProjectText(ProjectGuid));
        var created = ProjectStoreCommand.Run(Original, MotifProductVersion.CurrentText,
            (_, _) => CommandOutcome<string>.Success("created"));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        SqliteConnection.ClearAllPools();
        File.Copy(StoreOf(Original), StoreOf(Copy));
        File.WriteAllText(Copy, ProjectText(copyGuid));
    }

    private static void InsertBaseline(string store, string guid)
    {
        using var connection = new SqliteConnection($"Data Source={store};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Baselines (ProjectKey, ProjectIdentity, SemanticSnapshotDigest, ProjectionVersion, CapturedUtc,
                BundleDigest, RootDirectory, FwDataPath, PublishedUtc, SourceLastWriteUtc)
            SELECT 'key', $guid, 'digest', '1', 'then', 'bundle', 'root', FullFwDataPath, 'then', 'then' FROM MotifMetadata;
            """;
        command.Parameters.AddWithValue("$guid", guid);
        command.ExecuteNonQuery();
    }

    private static List<string> Query(string store, string sql)
    {
        SqliteConnection.ClearAllPools();
        using var connection = new SqliteConnection($"Data Source={store};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
            rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetValue(i).ToString())));
        return rows;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
