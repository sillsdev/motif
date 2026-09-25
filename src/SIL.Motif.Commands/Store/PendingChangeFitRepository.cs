using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Commands.Store;

public sealed class PendingChangeFitRepository(MotifDatabase database)
{
    private readonly MotifDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    public IReadOnlyList<ChangeFit>? Get(string draftRevision, long projectLastWriteUtcTicks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftRevision);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT FitSummaryJson FROM PendingChangeFits
            WHERE DraftRevision = $revision AND ProjectLastWriteUtcTicks = $lastWrite;
            """;
        command.Parameters.AddWithValue("$revision", draftRevision);
        command.Parameters.AddWithValue("$lastWrite", projectLastWriteUtcTicks);
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

    public void Save(string draftRevision, long projectLastWriteUtcTicks, IReadOnlyList<ChangeFit> fitSummary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftRevision);
        ArgumentNullException.ThrowIfNull(fitSummary);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO PendingChangeFits (DraftRevision, ProjectLastWriteUtcTicks, FitSummaryJson)
            VALUES ($revision, $lastWrite, $fitSummary)
            ON CONFLICT (DraftRevision, ProjectLastWriteUtcTicks) DO UPDATE SET
                FitSummaryJson = excluded.FitSummaryJson;
            """;
        command.Parameters.AddWithValue("$revision", draftRevision);
        command.Parameters.AddWithValue("$lastWrite", projectLastWriteUtcTicks);
        command.Parameters.AddWithValue("$fitSummary",
            JsonSerializer.Serialize(fitSummary, MotifJson.CreateOptions()));
        command.ExecuteNonQuery();
    }
}
