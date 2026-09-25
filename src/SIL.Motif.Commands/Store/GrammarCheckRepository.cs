using System.Globalization;
using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Commands.Store;

/// <summary>Stores grammar findings for a Baseline and the resolved Selection current when they were checked.</summary>
public sealed class GrammarCheckRepository(MotifDatabase database)
{
    private readonly MotifDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>Reads the newest check for the Baseline, regardless of later Selection or parser changes.</summary>
    public GrammarCheckResponse? GetLatest(string baselineToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineToken);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ResponseJson FROM GrammarChecks WHERE BaselineToken = $baseline
            ORDER BY CheckedUtc DESC, rowid DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$baseline", baselineToken);
        var json = command.ExecuteScalar() as string;
        if (json is null) return null;
        try
        {
            return JsonSerializer.Deserialize<GrammarCheckResponse>(json, MotifJson.CreateOptions())
                ?? throw new InvalidDataException("Stored grammar findings are incomplete.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Stored grammar findings are malformed.", exception);
        }
    }

    /// <summary>Replaces the check for the exact Baseline and Selection after a requested parser run succeeds.</summary>
    public void Save(string baselineToken, string selectionSha256, string? parserStamp, GrammarCheckResponse response)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineToken);
        ArgumentNullException.ThrowIfNull(selectionSha256);
        ArgumentNullException.ThrowIfNull(response);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO GrammarChecks (BaselineToken, SelectionSha256, ParserStamp, ResponseJson, CheckedUtc)
            VALUES ($baseline, $selection, $parser, $response, $checked)
            ON CONFLICT (BaselineToken, SelectionSha256) DO UPDATE SET
                ParserStamp = excluded.ParserStamp,
                ResponseJson = excluded.ResponseJson,
                CheckedUtc = excluded.CheckedUtc;
            """;
        command.Parameters.AddWithValue("$baseline", baselineToken);
        command.Parameters.AddWithValue("$selection", selectionSha256);
        command.Parameters.AddWithValue("$parser", (object?)parserStamp ?? DBNull.Value);
        command.Parameters.AddWithValue("$response", JsonSerializer.Serialize(response, MotifJson.CreateOptions()));
        command.Parameters.AddWithValue("$checked", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }
}
