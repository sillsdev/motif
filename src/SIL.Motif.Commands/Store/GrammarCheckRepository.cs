using System.Globalization;
using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Store;

/// <summary>Reads project and Trial grammar checks and stores the project's current check.</summary>
public sealed class GrammarCheckRepository(MotifDatabase database)
{
    private readonly MotifDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>Reads the newest check for the Baseline, regardless of later Selection or parser changes.</summary>
    public GrammarCheckResponse? GetLatest(string baselineToken)
        => ReadLatest(baselineToken, proposalId: null, intentDigest: null);

    /// <summary>Reads the grammar check for one exact Proposal revision.</summary>
    public GrammarCheckResponse? GetLatest(string baselineToken, string proposalId, string intentDigest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(intentDigest);
        return ReadLatest(baselineToken, proposalId, intentDigest);
    }

    private GrammarCheckResponse? ReadLatest(string baselineToken, string? proposalId, string? intentDigest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineToken);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = proposalId is null
            ? "SELECT ResponseJson FROM GrammarChecks WHERE BaselineToken = $baseline AND ProposalId IS NULL AND IntentDigest IS NULL;"
            : """
                SELECT ResponseJson FROM GrammarChecks
                WHERE BaselineToken = $baseline AND ProposalId = $proposal AND IntentDigest = $intent;
                """;
        command.Parameters.AddWithValue("$baseline", baselineToken);
        if (proposalId is not null)
        {
            command.Parameters.AddWithValue("$proposal", proposalId);
            command.Parameters.AddWithValue("$intent", (object?)intentDigest ?? DBNull.Value);
        }
        RepositoryReadCounters.QueryExecuted();
        var json = command.ExecuteScalar() as string;
        if (json is null) return null;
        RepositoryReadCounters.RecordDeserialized();
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

    /// <summary>Replaces the Baseline's check and recorded Selection after a requested parser run succeeds.</summary>
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
            ON CONFLICT DO UPDATE SET
                SelectionSha256 = excluded.SelectionSha256,
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
