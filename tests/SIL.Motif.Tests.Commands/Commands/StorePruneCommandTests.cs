using Microsoft.Data.Sqlite;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class StorePruneCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-prune-command-" + Guid.NewGuid().ToString("N"));

    public StorePruneCommandTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void PruneUsesTheProjectSettingAndReportsTheCheckpointedStore()
    {
        var project = Path.Combine(_root, "project.fwdata");
        File.WriteAllText(project, "fixture");
        File.WriteAllText(Path.Combine(_root, "project.motif.toml"), "[retention]\nstandalone-assessments-per-kind = 1\n");
        var seeded = ProjectStoreCommand.Run<string>(project, MotifProductVersion.CurrentText, (database, _) =>
        {
            var repository = new AssessmentRepository(database);
            for (var i = 0; i < 3; i++)
                repository.Record(new NewAssessmentRecord($"assessment-{i}", null, null, "pangloss", "ParseTime",
                    "{}", "scope", new AssessmentSubject.Words(Selection.Create("fixture", ["word"]), "none", "1"),
                    "{}", "outcome", "semantic", "grammar", "model", "pipeline", 0,
                    new AssessmentRecordMaterial.Words([new AssessedWord("word", "complete", [])], null, null, []),
                    DateTimeOffset.UtcNow.AddMinutes(i - 3).ToString("O")));
            return CommandOutcome<string>.Success("seeded");
        });
        Assert.True(seeded.Succeeded, seeded.Refusal?.Message);

        var result = StorePruneCommand.Run(new StorePruneRequest(project));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal(2, result.Value!.AssessmentsRemoved);
        Assert.True(result.Value.CheckpointComplete);
        Assert.True(result.Value.BytesBefore > 0);
        Assert.True(result.Value.BytesAfter > 0);
        var remaining = ProjectStoreCommand.Run<string>(project, MotifProductVersion.CurrentText,
            (database, _) => CommandOutcome<string>.Success(new AssessmentRepository(database).Get("assessment-2").AssessmentId));
        Assert.Equal("assessment-2", remaining.Value);
    }

    [Fact]
    public void AStoreOverItsBudgetIsRewrittenWhenItWasNotInIncrementalMode()
    {
        var project = Path.Combine(_root, "legacy.fwdata");
        var store = SeedBulkyStore(project, legacy: true);
        var before = new FileInfo(store).Length;

        var result = StorePruneCommand.Run(new StorePruneRequest(project, ByteBudget: 8_000_000));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.True(result.Value!.FullyVacuumed);
        Assert.False(result.Value.OverBudget);
        Assert.True(result.Value.AssessmentsRemoved >= 20);
        var after = new FileInfo(store).Length;
        Assert.True(after < before / 2, $"{before} bytes before and {after} after");
        Assert.True(after <= 10_000_000);
        using var connection = new SqliteConnection($"Data Source={store};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var mode = connection.CreateCommand();
        mode.CommandText = "PRAGMA auto_vacuum;";
        Assert.Equal(2L, (long)mode.ExecuteScalar()!);
    }

    [Fact]
    public void AnIncrementalStoreGivesItsSpaceBackWithoutARewrite()
    {
        var project = Path.Combine(_root, "current.fwdata");
        var store = SeedBulkyStore(project, legacy: false);
        var before = new FileInfo(store).Length;

        var result = StorePruneCommand.Run(new StorePruneRequest(project, ByteBudget: 8_000_000));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.False(result.Value!.FullyVacuumed);
        Assert.True(new FileInfo(store).Length < before / 2);
    }

    [Fact]
    public void ThePruneAfterAnAssessNeverRewritesTheFileAndTheWorkerCompactionDoes()
    {
        var project = Path.Combine(_root, "afterassess.fwdata");
        var store = SeedBulkyStore(project, legacy: true);

        var held = ProjectStoreCommand.Run<StorePruneResponse>(project, MotifProductVersion.CurrentText, (database, locator) =>
        {
            using var lease = ProjectParseLease.TryAcquire(database)!;
            return StorePruneCommand.RunHeld(database, locator, lease, 20, byteBudget: 8_000_000);
        });

        Assert.True(held.Succeeded, held.Refusal?.Message);
        Assert.False(held.Value!.FullyVacuumed);
        var afterPrune = new FileInfo(store).Length;
        var compacted = ProjectStoreCommand.Run<string>(project, MotifProductVersion.CurrentText, (database, _) =>
        {
            using var lease = ProjectParseLease.TryAcquire(database)!;
            return CommandOutcome<string>.Success(StoreCompaction.CompactWhenWorthwhile(database, lease).ToString());
        });
        Assert.Equal("True", compacted.Value);
        SqliteConnection.ClearAllPools();
        Assert.True(new FileInfo(store).Length < afterPrune / 2);
    }

    [Fact]
    public void AStoreFullOfPicturesReportsOverBudgetAndLosesNone()
    {
        var project = Path.Combine(_root, "pictures.fwdata");
        File.WriteAllText(project, "fixture");
        var id = SIL.Motif.Contract.Ids.CanonicalId.Mint();
        var seeded = ProjectStoreCommand.Run<string>(project, MotifProductVersion.CurrentText, (database, _) =>
        {
            using (var connection = database.OpenConnection())
            using (var insert = connection.CreateCommand())
            {
                insert.CommandText = "INSERT INTO Proposals (ProposalId, Status) VALUES ($id, 'proposed');";
                insert.Parameters.AddWithValue("$id", id.Value);
                insert.ExecuteNonQuery();
            }
            var pictures = new ProposalPictureRepository(database);
            var bytes = new byte[1_000_000];
            for (var i = 0; i < 6; i++)
            {
                new Random(i).NextBytes(bytes);
                Assert.Equal(PictureWriteOutcome.Written, pictures.Add(id, null, "image/png", 10, 10, bytes).Outcome);
            }
            return CommandOutcome<string>.Success("seeded");
        });
        Assert.True(seeded.Succeeded, seeded.Refusal?.Message);

        var result = StorePruneCommand.Run(new StorePruneRequest(project, ByteBudget: 3_000_000));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.True(result.Value!.OverBudget);
        Assert.True(result.Value.BytesAfter > 3_000_000);
        var remaining = ProjectStoreCommand.Run<string>(project, MotifProductVersion.CurrentText, (database, _) =>
            CommandOutcome<string>.Success(new ProposalPictureRepository(database).List(id).Count.ToString()));
        Assert.Equal("6", remaining.Value);
    }

    private static string SeedBulkyStore(string project, bool legacy)
    {
        File.WriteAllText(project, "fixture");
        var store = Path.ChangeExtension(project, ".motif.db");
        var created = ProjectStoreCommand.Run<string>(project, MotifProductVersion.CurrentText,
            (_, _) => CommandOutcome<string>.Success("created"));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        if (legacy)
        {
            using var connection = new SqliteConnection($"Data Source={store};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode=DELETE; PRAGMA auto_vacuum=NONE; VACUUM; PRAGMA journal_mode=WAL;";
            command.ExecuteNonQuery();
        }
        var seeded = ProjectStoreCommand.Run<string>(project, MotifProductVersion.CurrentText, (database, _) =>
        {
            var repository = new AssessmentRepository(database);
            var bulky = new string('x', 1_000_000);
            for (var i = 0; i < 30; i++)
                repository.Record(new NewAssessmentRecord($"assessment-{i:D2}", null, null, "pangloss", "ParseTime",
                    "{}", "scope", new AssessmentSubject.Words(Selection.Create("fixture", ["word"]), "none", "1"),
                    "{}", "outcome", "semantic", "grammar", "model", "pipeline", 0,
                    new AssessmentRecordMaterial.Words([new AssessedWord(bulky + i, "complete", [])], null, null, []),
                    DateTimeOffset.UtcNow.AddMinutes(i - 30).ToString("O")));
            return CommandOutcome<string>.Success("seeded");
        });
        Assert.True(seeded.Succeeded, seeded.Refusal?.Message);
        return store;
    }

    [Fact]
    public void ExpiredSelectedEvidenceBecomesACataloguedRefusal()
    {
        var project = Path.Combine(_root, "project.fwdata");
        File.WriteAllText(project, "fixture");
        var result = ProjectStoreCommand.Run<string>(project, MotifProductVersion.CurrentText,
            (_, _) => throw new AssessmentExpiredException("assessment-1"));

        Assert.Equal(RefusalCodes.AssessmentExpired, result.Refusal!.Code);
        Assert.Equal(FailureReason.NotFound, result.Refusal.Reason);
        Assert.True(RefusalCatalog.Specs.ContainsKey(RefusalCodes.AssessmentExpired));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }
}
