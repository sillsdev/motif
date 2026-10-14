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
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO GrammarChecks (BaselineToken, ProposalId, IntentDigest, SelectionSha256, ParserStamp, ResponseJson, CheckedUtc)
            VALUES ($baseline, $proposal, $intent, $selection, NULL, $response, $checked)
            ON CONFLICT DO UPDATE SET
                IntentDigest = excluded.IntentDigest,
                SelectionSha256 = excluded.SelectionSha256,
                ParserStamp = excluded.ParserStamp,
                ResponseJson = excluded.ResponseJson,
                CheckedUtc = excluded.CheckedUtc;
            """;
        command.Parameters.AddWithValue("$baseline", JsonSerializer.Serialize(baseline, MotifJson.CreateOptions()));
        command.Parameters.AddWithValue("$proposal", proposalId.Value);
        command.Parameters.AddWithValue("$intent", intentDigest);
        command.Parameters.AddWithValue("$selection", selectionSha256);
        command.Parameters.AddWithValue("$response", JsonSerializer.Serialize(recordedResponse, MotifJson.CreateOptions()));
        command.Parameters.AddWithValue("$checked", checkedUtc.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }
}
