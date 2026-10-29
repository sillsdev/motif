using System.Text.Json;
using SIL.Motif.Projection.Usage;

namespace SIL.Motif.Host.Store;

/// <summary>
/// Writes <see cref="UsageLogEntry"/> rows to the machine store's <c>Usage</c> table — the home ADR 0021
/// decision 4's usage log requires. It is a database rather than a file because several <c>motif</c>
/// invocations may run at once, and two processes appending to one file interleave where two connections
/// into one SQLite database do not.
/// </summary>
public sealed class MachineUsageLog : IUsageLogSink
{
    private const int DefaultMaximumRows = 10_000;
    private readonly MachineDatabase _database;
    private readonly int _maximumRows;

    public MachineUsageLog(MachineDatabase database) : this(database, MaximumRowsFromEnvironment()) { }

    internal MachineUsageLog(MachineDatabase database, int maximumRows)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _maximumRows = maximumRows > 0 ? maximumRows : throw new ArgumentOutOfRangeException(nameof(maximumRows));
    }

    public void Append(UsageLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Usage (TimestampUtc, Command, ArgumentShapeJson)
            VALUES ($timestamp, $command, $shape);
            DELETE FROM Usage WHERE Id IN (SELECT Id FROM Usage ORDER BY Id DESC LIMIT -1 OFFSET $maximum);
            """;
        command.Parameters.AddWithValue("$timestamp", entry.TimestampUtc);
        command.Parameters.AddWithValue("$command", entry.Command);
        command.Parameters.AddWithValue("$shape", JsonSerializer.Serialize(entry.ArgumentShape));
        command.Parameters.AddWithValue("$maximum", _maximumRows);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static int MaximumRowsFromEnvironment() =>
        int.TryParse(Environment.GetEnvironmentVariable("MOTIF_USAGE_MAX_ROWS"), out var maximum) && maximum > 0
            ? maximum : DefaultMaximumRows;

    public IReadOnlyList<UsageLogEntry> ReadAll()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT TimestampUtc, Command, ArgumentShapeJson FROM Usage ORDER BY Id;";
        using var reader = command.ExecuteReader();
        var entries = new List<UsageLogEntry>();
        while (reader.Read())
        {
            var shape = JsonSerializer.Deserialize<IReadOnlyList<string>>(reader.GetString(2))
                ?? throw new InvalidDataException("The persisted usage row has a malformed argument shape.");
            entries.Add(new UsageLogEntry(reader.GetString(0), reader.GetString(1), shape));
        }
        return entries;
    }
}
