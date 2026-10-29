using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Store;

/// <summary>A grammar-check response and provenance read from one persisted row.</summary>
internal sealed record GrammarCheckStoredRecord(GrammarCheckResponse Response, GrammarCheckProvenance Provenance);

/// <summary>Reads project and Trial grammar checks and stores the project's current check.</summary>
public sealed class GrammarCheckRepository(MotifDatabase database)
{
    private readonly MotifDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>Reads the newest check for the Baseline, regardless of later Selection or parser changes.</summary>
    public GrammarCheckResponse? GetLatest(string baselineToken)
        => GetLatestStored(baselineToken)?.Response;

    /// <summary>Reads the grammar check for one exact Proposal revision.</summary>
    public GrammarCheckResponse? GetLatest(string baselineToken, string proposalId, string intentDigest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(intentDigest);
        return GetLatestStored(baselineToken, proposalId, intentDigest)?.Response;
    }

    /// <summary>Reads the latest stored check and its recorded provenance for the Baseline.</summary>
    internal GrammarCheckStoredRecord? GetLatestStored(string baselineToken)
        => ReadLatestStored(baselineToken, proposalId: null, intentDigest: null);

    /// <summary>Reads the stored check and its recorded provenance for one exact Proposal revision.</summary>
    internal GrammarCheckStoredRecord? GetLatestStored(string baselineToken, string proposalId, string intentDigest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(intentDigest);
        return ReadLatestStored(baselineToken, proposalId, intentDigest);
    }

    private GrammarCheckStoredRecord? ReadLatestStored(string baselineToken, string? proposalId, string? intentDigest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineToken);
        using var connection = _database.OpenConnection();
        return ReadLatestStored(connection, null, baselineToken, proposalId, intentDigest);
    }

    internal GrammarCheckResponse? GetLatest(SqliteConnection connection, SqliteTransaction transaction, string baselineToken)
        => ReadLatestStored(connection, transaction, baselineToken, null, null)?.Response;

    private static GrammarCheckStoredRecord? ReadLatestStored(SqliteConnection connection, SqliteTransaction? transaction,
        string baselineToken, string? proposalId, string? intentDigest)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = proposalId is null
            ? "SELECT BaselineToken, ProposalId, IntentDigest, SelectionSha256, ParserStamp, ResponseGzip, CheckedUtc " +
              "FROM GrammarChecks WHERE BaselineToken = $baseline AND ProposalId IS NULL AND IntentDigest IS NULL;"
            : """
                SELECT BaselineToken, ProposalId, IntentDigest, SelectionSha256, ParserStamp, ResponseGzip, CheckedUtc
                FROM GrammarChecks
                WHERE BaselineToken = $baseline AND ProposalId = $proposal AND IntentDigest = $intent;
                """;
        command.Parameters.AddWithValue("$baseline", baselineToken);
        if (proposalId is not null)
        {
            command.Parameters.AddWithValue("$proposal", proposalId);
            command.Parameters.AddWithValue("$intent", (object?)intentDigest ?? DBNull.Value);
        }
        RepositoryReadCounters.QueryExecuted();
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        RepositoryReadCounters.RecordDeserialized();
        try
        {
            var response = JsonSerializer.Deserialize<GrammarCheckResponse>(
                GrammarCheckStorage.Decompress(reader.GetFieldValue<byte[]>(5)), MotifJson.CreateOptions())
                ?? throw new InvalidDataException("Stored grammar findings are incomplete.");
            var proposal = NullableString(reader, 1);
            var intentDigestValue = NullableString(reader, 2);
            if ((proposal is null) != (intentDigestValue is null))
                throw new InvalidDataException("Stored Trial grammar-check identity is incomplete.");
            var trial = proposal is null ? null : new TrialGrammarCheckIdentity(proposal, intentDigestValue!);
            var storedProvenance = response.Provenance;
            var provenance = new GrammarCheckProvenance(
                ReadBaselineToken(reader.GetString(0)),
                reader.GetString(3),
                NullableString(reader, 4),
                ReadCheckedUtc(reader.GetString(6)),
                storedProvenance?.SourceLastWriteUtc,
                trial);
            if (storedProvenance is not null &&
                (storedProvenance.BaselineToken != provenance.BaselineToken ||
                 storedProvenance.SelectionSha256 != provenance.SelectionSha256 ||
                 storedProvenance.ParserStamp != provenance.ParserStamp ||
                 storedProvenance.CheckedUtc != provenance.CheckedUtc ||
                 storedProvenance.Trial != provenance.Trial))
                throw new InvalidDataException("Stored grammar-check provenance disagrees with its recorded identity.");
            return new GrammarCheckStoredRecord(response with { Provenance = provenance }, provenance);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Stored grammar findings are malformed.", exception);
        }
    }

    private static string? NullableString(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static BaselineToken ReadBaselineToken(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<BaselineToken>(json, MotifJson.CreateOptions())
                ?? throw new InvalidDataException("Stored grammar-check Baseline identity is incomplete.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Stored grammar-check Baseline identity is malformed.", exception);
        }
    }

    private static DateTimeOffset ReadCheckedUtc(string value)
    {
        try
        {
            return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.None);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("Stored grammar-check time is malformed.", exception);
        }
    }

    /// <summary>Replaces the Baseline's check and recorded Selection after a requested parser run succeeds.</summary>
    public GrammarCheckResponse Save(string baselineToken, string selectionSha256, string? parserStamp,
        GrammarCheckResponse response, DateTimeOffset? sourceLastWriteUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineToken);
        ArgumentNullException.ThrowIfNull(selectionSha256);
        ArgumentNullException.ThrowIfNull(response);
        var checkedUtc = DateTimeOffset.UtcNow;
        var recordedResponse = response with
        {
            Provenance = new GrammarCheckProvenance(ReadBaselineToken(baselineToken), selectionSha256, parserStamp,
                checkedUtc, sourceLastWriteUtc, Trial: null),
        };
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO GrammarChecks (BaselineToken, SelectionSha256, ParserStamp, ResponseGzip, CheckedUtc)
            VALUES ($baseline, $selection, $parser, $response, $checked)
            ON CONFLICT DO UPDATE SET
                SelectionSha256 = excluded.SelectionSha256,
                ParserStamp = excluded.ParserStamp,
                ResponseGzip = excluded.ResponseGzip,
                CheckedUtc = excluded.CheckedUtc;
            """;
        command.Parameters.AddWithValue("$baseline", baselineToken);
        command.Parameters.AddWithValue("$selection", selectionSha256);
        command.Parameters.AddWithValue("$parser", (object?)parserStamp ?? DBNull.Value);
        command.Parameters.AddWithValue("$response", GrammarCheckStorage.Compress(
            JsonSerializer.Serialize(recordedResponse, MotifJson.CreateOptions())));
        command.Parameters.AddWithValue("$checked", checkedUtc.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
        var rows = new List<(long RowId, DateTimeOffset Captured)>();
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT rowid, BaselineToken FROM GrammarChecks WHERE ProposalId IS NULL;";
            using var reader = read.ExecuteReader();
            while (reader.Read())
                rows.Add((reader.GetInt64(0), DateTimeOffset.Parse(ReadBaselineToken(reader.GetString(1)).CapturedUtc,
                    CultureInfo.InvariantCulture)));
        }
        foreach (var row in rows.OrderByDescending(row => row.Captured).ThenByDescending(row => row.RowId)
                     .Skip(MaximumProjectRows()))
        {
            using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM GrammarChecks WHERE rowid = $id;";
            delete.Parameters.AddWithValue("$id", row.RowId);
            delete.ExecuteNonQuery();
        }
        transaction.Commit();
        return recordedResponse;
    }

    private static int MaximumProjectRows() =>
        int.TryParse(Environment.GetEnvironmentVariable("MOTIF_GRAMMAR_BASELINE_MAX_ROWS"), out var maximum) && maximum > 0
            ? maximum : 2;
}
