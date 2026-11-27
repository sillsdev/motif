using System.Globalization;
using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Worker.Store;

// Trial writes stay here because Worker.Runtime cannot depend on Commands.
/// <summary>Stores grammar-health results produced while a Proposal Trial is open.</summary>
public sealed class TrialGrammarCheckRepository(MotifDatabase database)
{
    private readonly MotifDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>Saves a grammar check for one measured Proposal revision and Selection.</summary>
    public void Save(BaselineToken baseline, CanonicalId proposalId, string intentDigest,
        string selectionSha256, GrammarCheckResponse response, DateTimeOffset? sourceLastWriteUtc = null)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentException.ThrowIfNullOrWhiteSpace(intentDigest);
        ArgumentNullException.ThrowIfNull(selectionSha256);
        ArgumentNullException.ThrowIfNull(response);
        var checkedUtc = DateTimeOffset.UtcNow;
        var recordedResponse = response with
        {
            Provenance = new GrammarCheckProvenance(BaselineToken: baseline, SelectionSha256: selectionSha256,
                ParserStamp: null, CheckedUtc: checkedUtc, SourceLastWriteUtc: sourceLastWriteUtc,
                Trial: new TrialGrammarCheckIdentity(proposalId.Value, intentDigest)),
        };
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO GrammarChecks (BaselineToken, ProposalId, IntentDigest, SelectionSha256, ParserStamp, ResponseGzip, CheckedUtc)
            VALUES ($baseline, $proposal, $intent, $selection, NULL, $response, $checked)
            ON CONFLICT DO UPDATE SET
                IntentDigest = excluded.IntentDigest,
                SelectionSha256 = excluded.SelectionSha256,
                ParserStamp = excluded.ParserStamp,
                ResponseGzip = excluded.ResponseGzip,
                CheckedUtc = excluded.CheckedUtc;
            DELETE FROM GrammarChecks WHERE rowid IN (
                SELECT rowid FROM GrammarChecks WHERE ProposalId = $proposal
                ORDER BY (BaselineToken = $baseline AND IntentDigest = $intent) DESC,
                    CheckedUtc DESC, rowid DESC LIMIT -1 OFFSET $maximum);
            """;
        command.Parameters.AddWithValue("$baseline", JsonSerializer.Serialize(baseline, MotifJson.CreateOptions()));
        command.Parameters.AddWithValue("$proposal", proposalId.Value);
        command.Parameters.AddWithValue("$intent", intentDigest);
        command.Parameters.AddWithValue("$selection", selectionSha256);
        command.Parameters.AddWithValue("$response", GrammarCheckStorage.Compress(
            JsonSerializer.Serialize(recordedResponse, MotifJson.CreateOptions())));
        command.Parameters.AddWithValue("$checked", checkedUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$maximum", MaximumRows());
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    /// <summary>Reads the Baseline's own grammar check, the one <c>grammar check</c> stored; null when none exists.</summary>
    public GrammarCheckResponse? ReadBaselineCheck(BaselineToken baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ResponseGzip FROM GrammarChecks " +
            "WHERE BaselineToken = $baseline AND ProposalId IS NULL AND IntentDigest IS NULL;";
        command.Parameters.AddWithValue("$baseline", JsonSerializer.Serialize(baseline, MotifJson.CreateOptions()));
        return command.ExecuteScalar() is byte[] bytes
            ? JsonSerializer.Deserialize<GrammarCheckResponse>(GrammarCheckStorage.Decompress(bytes),
                MotifJson.CreateOptions())
            : null;
    }

    /// <summary>
    /// Reads the Baseline's own grammar check only when it was produced by the same parser and settings, named by
    /// <paramref name="identity"/>; a check stored under any other identity, or none, reads as absent.
    /// </summary>
    public GrammarCheckResponse? ReadBaselineCheck(BaselineToken baseline, string identity)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ResponseGzip FROM GrammarChecks " +
            "WHERE BaselineToken = $baseline AND ProposalId IS NULL AND IntentDigest IS NULL AND ParserStamp = $identity;";
        command.Parameters.AddWithValue("$baseline", JsonSerializer.Serialize(baseline, MotifJson.CreateOptions()));
        command.Parameters.AddWithValue("$identity", identity);
        return command.ExecuteScalar() is byte[] bytes
            ? JsonSerializer.Deserialize<GrammarCheckResponse>(GrammarCheckStorage.Decompress(bytes),
                MotifJson.CreateOptions())
            : null;
    }

    /// <summary>Stores the Baseline's own grammar check under the parser identity that produced it, replacing an earlier one.</summary>
    public void SaveBaselineCheck(BaselineToken baseline, GrammarCheckResponse response, string identity,
        DateTimeOffset? sourceLastWriteUtc = null)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        var checkedUtc = DateTimeOffset.UtcNow;
        var recorded = response with
        {
            Provenance = new GrammarCheckProvenance(BaselineToken: baseline, SelectionSha256: string.Empty,
                ParserStamp: identity, CheckedUtc: checkedUtc, SourceLastWriteUtc: sourceLastWriteUtc, Trial: null),
        };
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO GrammarChecks (BaselineToken, ProposalId, IntentDigest, SelectionSha256, ParserStamp, ResponseGzip, CheckedUtc)
            VALUES ($baseline, NULL, NULL, '', $identity, $response, $checked)
            ON CONFLICT DO UPDATE SET
                SelectionSha256 = excluded.SelectionSha256,
                ParserStamp = excluded.ParserStamp,
                ResponseGzip = excluded.ResponseGzip,
                CheckedUtc = excluded.CheckedUtc;
            """;
        command.Parameters.AddWithValue("$baseline", JsonSerializer.Serialize(baseline, MotifJson.CreateOptions()));
        command.Parameters.AddWithValue("$identity", identity);
        command.Parameters.AddWithValue("$response", GrammarCheckStorage.Compress(
            JsonSerializer.Serialize(recorded, MotifJson.CreateOptions())));
        command.Parameters.AddWithValue("$checked", checkedUtc.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static int MaximumRows() =>
        int.TryParse(Environment.GetEnvironmentVariable("MOTIF_GRAMMAR_CHECK_MAX_ROWS"), out var maximum) && maximum > 0
            ? maximum : 5;
}
