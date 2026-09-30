using SIL.Motif.Host.Store;

namespace SIL.Motif.Worker.Store;

/// <summary>Stores whether the person skipped first-time setup for this project.</summary>
public sealed class ProjectSetupRepository(MotifDatabase database)
{
    private readonly MotifDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>Returns whether first-time setup has been skipped.</summary>
    public bool IsSkipped()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT SetupSkippedUtc IS NOT NULL FROM MotifMetadata WHERE Id = 1;";
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) != 0;
    }

    /// <summary>Records that first-time setup was skipped, preserving the first skip time.</summary>
    public void MarkSkipped()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE MotifMetadata SET SetupSkippedUtc = COALESCE(SetupSkippedUtc, $skippedUtc)
            WHERE Id = 1;
            """;
        command.Parameters.AddWithValue("$skippedUtc",
            DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }
}
