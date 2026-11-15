using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Worker.Baselines;

internal sealed record ProgressState(string Revision, string ContentRevision, string JudgementRevision,
    DateTimeOffset SourceLastWriteUtc, string? PendingReceiptId, ProgressCounts Totals,
    IReadOnlyDictionary<string, int> Excluded);

internal static class ProgressRepository
{
    internal static readonly AsyncLocal<Action<int>?> TextRowsRead = new();
    private static readonly JsonSerializerOptions Json = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = MotifJson.CreateOptions();
        options.TypeInfoResolverChain.Insert(0, ProgressJsonContext.Default);
        return options;
    }

    public static string? PendingReceipt(MotifDatabase database, string proposalId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT s.PendingReceiptId FROM ProgressState s JOIN Receipts r " +
            "ON r.ReceiptId = s.PendingReceiptId WHERE s.Id = 1 AND r.ProposalId = $proposal;";
        command.Parameters.AddWithValue("$proposal", proposalId);
        return command.ExecuteScalar() as string;
    }

    public static (ProgressState State, ProgressProjection Projection)? ReadPending(MotifDatabase database, string receiptId)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        var state = ReadState(connection, transaction);
        if (state?.PendingReceiptId != receiptId) return null;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT TextId, Title, IncidenceJson, ExcludedJson FROM ProgressTexts ORDER BY TextId;";
        using var reader = command.ExecuteReader();
        var rows = new List<(Guid Id, string Title, string Incidence, string Excluded)>();
        while (reader.Read())
            rows.Add((Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        var texts = new ProgressText[rows.Count];
        try
        {
            Parallel.For(0, rows.Count, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
            {
                var row = rows[i];
                texts[i] = new ProgressText(row.Id, row.Title, Deserialize<ProgressIncidence[]>(row.Incidence),
                    Deserialize<Dictionary<string, int>>(row.Excluded));
            });
        }
        catch (AggregateException exception) when (exception.InnerExceptions.All(item => item is InvalidDataException))
        {
            throw new InvalidDataException("The stored Progress projection is invalid.", exception);
        }
        return (state, new ProgressProjection(texts));
    }

    public static void PublishApplied(MotifDatabase database, ProgressProjection projection,
        string receiptId, DateTimeOffset sourceLastWriteUtc, string contentRevision)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        Publish(connection, transaction, projection, contentRevision, "judgement/" + receiptId,
            sourceLastWriteUtc, receiptId);
        transaction.Commit();
    }

    public static void Publish(SqliteConnection connection, SqliteTransaction transaction,
        ProgressProjection projection, string contentRevision, string judgementRevision,
        DateTimeOffset sourceLastWriteUtc, string? expectedReceiptId = null)
    {
        if (expectedReceiptId is not null)
        {
            using var check = connection.CreateCommand();
            check.Transaction = transaction;
            check.CommandText = "SELECT PendingReceiptId FROM ProgressState WHERE Id = 1;";
            if (!string.Equals(check.ExecuteScalar() as string, expectedReceiptId, StringComparison.Ordinal)) return;
        }
        var totals = ProgressProjectionBuilder.Aggregate(projection.Texts);
        var excluded = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pair in projection.Texts.SelectMany(text => text.Excluded))
            excluded[pair.Key] = excluded.GetValueOrDefault(pair.Key) + pair.Value;
        var serialized = new (string Counts, string Incidence, string Excluded)[projection.Texts.Count];
        Parallel.For(0, projection.Texts.Count, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
        {
            var text = projection.Texts[i];
            serialized[i] = (Serialize(ProgressProjectionBuilder.Aggregate([text])), Serialize(text.Incidence),
                Serialize(text.Excluded));
        });
        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM ProgressTexts;";
            delete.ExecuteNonQuery();
        }
        using (var row = connection.CreateCommand())
        {
            row.Transaction = transaction;
            row.CommandText = "INSERT INTO ProgressTexts (TextId, Title, CountsJson, IncidenceJson, ExcludedJson) " +
                "VALUES ($id, $title, $counts, $incidence, $excluded);";
            var id = row.Parameters.Add("$id", SqliteType.Text);
            var title = row.Parameters.Add("$title", SqliteType.Text);
            var counts = row.Parameters.Add("$counts", SqliteType.Text);
            var incidence = row.Parameters.Add("$incidence", SqliteType.Text);
            var excludedJson = row.Parameters.Add("$excluded", SqliteType.Text);
            for (var i = 0; i < projection.Texts.Count; i++)
            {
                var text = projection.Texts[i];
                id.Value = text.Id.ToString("D");
                title.Value = text.Title;
                counts.Value = serialized[i].Counts;
                incidence.Value = serialized[i].Incidence;
                excludedJson.Value = serialized[i].Excluded;
                row.ExecuteNonQuery();
            }
        }
        using var state = connection.CreateCommand();
        state.Transaction = transaction;
        state.CommandText = """
            INSERT INTO ProgressState (Id, Revision, ContentRevision, JudgementRevision, SourceLastWriteUtc,
                PendingReceiptId, TotalsJson, ExcludedJson)
            VALUES (1, $revision, $content, $judgement, $saved, NULL, $totals, $excluded)
            ON CONFLICT(Id) DO UPDATE SET Revision = excluded.Revision, ContentRevision = excluded.ContentRevision,
                JudgementRevision = excluded.JudgementRevision, SourceLastWriteUtc = excluded.SourceLastWriteUtc,
                PendingReceiptId = NULL, TotalsJson = excluded.TotalsJson, ExcludedJson = excluded.ExcludedJson;
            """;
        state.Parameters.AddWithValue("$revision", "progress/" + Guid.NewGuid().ToString("N"));
        state.Parameters.AddWithValue("$content", contentRevision);
        state.Parameters.AddWithValue("$judgement", judgementRevision);
        state.Parameters.AddWithValue("$saved", sourceLastWriteUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        state.Parameters.AddWithValue("$totals", Serialize(totals));
        state.Parameters.AddWithValue("$excluded", Serialize(excluded));
        state.ExecuteNonQuery();
    }

    public static ProgressState? ReadState(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Revision, ContentRevision, JudgementRevision, SourceLastWriteUtc, " +
            "PendingReceiptId, TotalsJson, ExcludedJson FROM ProgressState WHERE Id = 1;";
        using var reader = command.ExecuteReader();
        return !reader.Read() ? null : new ProgressState(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture), reader.IsDBNull(4) ? null : reader.GetString(4),
            Deserialize<ProgressCounts>(reader.GetString(5)), Deserialize<Dictionary<string, int>>(reader.GetString(6)));
    }

    public static IReadOnlyList<(ProgressText Text, ProgressCounts Counts)> ReadTexts(
        SqliteConnection connection, SqliteTransaction transaction, IReadOnlySet<Guid>? ids, string? after, int? limit,
        bool includeIncidence)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT TextId, Title, CountsJson, " + (includeIncidence ? "IncidenceJson" : "'[]'") +
            ", ExcludedJson FROM ProgressTexts WHERE ($after IS NULL OR TextId > $after) ORDER BY TextId;";
        command.Parameters.AddWithValue("$after", (object?)after ?? DBNull.Value);
        var rows = new List<(ProgressText, ProgressCounts)>();
        var examined = 0;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            examined++;
            var id = Guid.Parse(reader.GetString(0));
            if (ids is not null && !ids.Contains(id)) continue;
            var text = new ProgressText(id, reader.GetString(1),
                Deserialize<ProgressIncidence[]>(reader.GetString(3)),
                Deserialize<Dictionary<string, int>>(reader.GetString(4)));
            rows.Add((text, Deserialize<ProgressCounts>(reader.GetString(2))));
            if (limit is not null && rows.Count >= limit) break;
        }
        TextRowsRead.Value?.Invoke(examined);
        return rows;
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static T Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json)
                ?? throw new InvalidDataException("The stored Progress projection is incomplete.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The stored Progress projection is invalid.", exception);
        }
    }
}

[JsonSerializable(typeof(ProgressIncidence[]))]
[JsonSerializable(typeof(IReadOnlyList<ProgressIncidence>))]
[JsonSerializable(typeof(ProgressCounts))]
[JsonSerializable(typeof(Dictionary<string, int>))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, int>))]
internal partial class ProgressJsonContext : JsonSerializerContext;
