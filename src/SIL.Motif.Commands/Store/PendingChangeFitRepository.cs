using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Commands.Store;

public sealed class PendingChangeFitRepository(MotifDatabase database)
{
    private readonly MotifDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    public IReadOnlyList<ChangeFit>? Get(string draftRevision, long projectLastWriteUtcTicks,
        string baselineIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftRevision);
        ArgumentNullException.ThrowIfNull(baselineIdentity);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT FitSummaryJson FROM PendingChangeFits
            WHERE DraftRevision = $revision AND ProjectLastWriteUtcTicks = $lastWrite
                AND BaselineIdentity = $baselineIdentity;
            """;
        command.Parameters.AddWithValue("$revision", draftRevision);
        command.Parameters.AddWithValue("$lastWrite", projectLastWriteUtcTicks);
        command.Parameters.AddWithValue("$baselineIdentity", baselineIdentity);
        var json = command.ExecuteScalar() as string;
        if (json is null) return null;
        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<ChangeFit>>(json, MotifJson.CreateOptions())
                ?? throw new InvalidDataException("Stored pending change fit is incomplete.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Stored pending change fit is malformed.", exception);
        }
    }

    public void Save(string draftRevision, long projectLastWriteUtcTicks, string baselineIdentity,
        IReadOnlyList<ChangeFit> fitSummary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftRevision);
        ArgumentNullException.ThrowIfNull(baselineIdentity);
        ArgumentNullException.ThrowIfNull(fitSummary);
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var prune = connection.CreateCommand())
        {
            prune.Transaction = transaction;
            prune.CommandText = """
                DELETE FROM PendingChangeFits
                WHERE DraftRevision <> $revision OR ProjectLastWriteUtcTicks <> $lastWrite
                    OR BaselineIdentity <> $baselineIdentity;
                """;
            prune.Parameters.AddWithValue("$revision", draftRevision);
            prune.Parameters.AddWithValue("$lastWrite", projectLastWriteUtcTicks);
            prune.Parameters.AddWithValue("$baselineIdentity", baselineIdentity);
            prune.ExecuteNonQuery();
        }
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO PendingChangeFits
                (DraftRevision, ProjectLastWriteUtcTicks, BaselineIdentity, FitSummaryJson)
            VALUES ($revision, $lastWrite, $baselineIdentity, $fitSummary)
            ON CONFLICT (DraftRevision, ProjectLastWriteUtcTicks, BaselineIdentity) DO UPDATE SET
                FitSummaryJson = excluded.FitSummaryJson;
            """;
        command.Parameters.AddWithValue("$revision", draftRevision);
        command.Parameters.AddWithValue("$lastWrite", projectLastWriteUtcTicks);
        command.Parameters.AddWithValue("$baselineIdentity", baselineIdentity);
        command.Parameters.AddWithValue("$fitSummary",
            JsonSerializer.Serialize(fitSummary, MotifJson.CreateOptions()));
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public void Clear()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM PendingChangeFits;";
        command.ExecuteNonQuery();
    }
}
