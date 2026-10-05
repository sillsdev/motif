using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Commands.Store;

/// <summary>Keeps the latest parser refusal for a Baseline until parsing succeeds against that same Baseline.</summary>
public sealed class ParserRefusalRepository(MotifDatabase database)
{
    public Refusal? Get(BaselineToken token)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT RefusalJson FROM ParserRefusals WHERE BaselineToken = $baseline;";
        command.Parameters.AddWithValue("$baseline", JsonSerializer.Serialize(token, MotifJson.CreateOptions()));
        if (command.ExecuteScalar() is not string json) return null;
        try
        {
            return JsonSerializer.Deserialize<Refusal>(json, MotifJson.CreateOptions())
                ?? throw new InvalidDataException("The stored parser refusal is incomplete.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The stored parser refusal is malformed.", exception);
        }
    }

    public void Save(BaselineToken token, Refusal refusal)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ParserRefusals (BaselineToken, RefusalJson) VALUES ($baseline, $refusal)
            ON CONFLICT (BaselineToken) DO UPDATE SET RefusalJson = excluded.RefusalJson;
            """;
        command.Parameters.AddWithValue("$baseline", JsonSerializer.Serialize(token, MotifJson.CreateOptions()));
        command.Parameters.AddWithValue("$refusal", JsonSerializer.Serialize(refusal, MotifJson.CreateOptions()));
        command.ExecuteNonQuery();
    }

    public void Clear(BaselineToken token)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ParserRefusals WHERE BaselineToken = $baseline;";
        command.Parameters.AddWithValue("$baseline", JsonSerializer.Serialize(token, MotifJson.CreateOptions()));
        command.ExecuteNonQuery();
    }
}
