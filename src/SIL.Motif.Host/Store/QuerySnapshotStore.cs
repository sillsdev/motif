using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Query;
using SIL.Motif.Host.Parsimony;

namespace SIL.Motif.Host.Store;

/// <summary>Atomically replaces a project's compact query rows with those of one completed Trial.</summary>
public static class QuerySnapshotStore
{
    /// <summary>Maximum bytes accepted for the materialized rows and their metadata.</summary>
    public const int MaximumBytes = 100 * 1024 * 1024;

    /// <summary>A snapshot bound to its Trial and source grammar.</summary>
    public sealed record Stored(string TrialId, string GrammarHash, string BundleId, string WritingSystem,
        QueryFactsReader.Snapshot Snapshot, long SourceWriteTicks = 0, string TrialCreatedUtc = "", double TrialQueueOrder = 0);

    /// <summary>Removes the materialized query rows and their metadata.</summary>
    public static void Clear(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM query_forms; DELETE FROM query_stems; DELETE FROM query_uses; DELETE FROM query_objects; DELETE FROM query_snapshot;";
        command.ExecuteNonQuery();
    }

    /// <summary>Binds the snapshot to the retained evidence bundle for the same Trial and grammar.</summary>
    public static void BindBundle(SqliteConnection connection, SqliteTransaction transaction,
        string trialId, string grammarHash, string bundleId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE query_snapshot SET BundleId=$bundle WHERE TrialId=$trial AND GrammarHash=$grammar";
        command.Parameters.AddWithValue("$bundle", bundleId);
        command.Parameters.AddWithValue("$trial", trialId);
        command.Parameters.AddWithValue("$grammar", grammarHash);
        command.ExecuteNonQuery();
    }

    /// <summary>Finds an available retained candidate bundle for the Trial's project and grammar.</summary>
    public static string FindRetainedBundle(SqliteConnection connection, SqliteTransaction transaction,
        string trialId, string grammarHash)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT b.BundleId FROM ParsimonyBundles b JOIN Jobs j ON j.ProjectKey=b.ProjectKey " +
            "WHERE j.JobId=$trial AND b.InputKind='candidate' AND b.State='available' AND b.ModelFingerprint=$grammar " +
            "ORDER BY b.CreatedUtc DESC, b.BundleId DESC LIMIT 1";
        command.Parameters.AddWithValue("$trial", trialId);
        command.Parameters.AddWithValue("$grammar", grammarHash);
        return command.ExecuteScalar() as string ?? "";
    }

    /// <summary>Binds the current matching Trial when its candidate bundle becomes available.</summary>
    public static void BindPublishedBundle(SqliteConnection connection, SqliteTransaction transaction,
        string projectKey, string grammarHash, string bundleId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE query_snapshot SET BundleId=$bundle WHERE GrammarHash=$grammar AND " +
            "EXISTS (SELECT 1 FROM Jobs WHERE JobId=query_snapshot.TrialId AND ProjectKey=$project)";
        command.Parameters.AddWithValue("$bundle", bundleId);
        command.Parameters.AddWithValue("$grammar", grammarHash);
        command.Parameters.AddWithValue("$project", projectKey);
        command.ExecuteNonQuery();
    }

    /// <summary>Prunes the rows when their bound evidence bundle is deleted.</summary>
    public static void ClearForBundle(SqliteConnection connection, SqliteTransaction transaction, string bundleId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM query_snapshot WHERE BundleId=$bundle";
        command.Parameters.AddWithValue("$bundle", bundleId);
        if (command.ExecuteScalar() is not null) Clear(connection, transaction);
    }

    /// <summary>Publishes rows unless a newer Trial already has retained rows.</summary>
    public static void ReplaceIfNewer(SqliteConnection connection, SqliteTransaction transaction, Stored snapshot)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT incoming.CreatedUtc, incoming.QueueOrder FROM Jobs incoming " +
            "WHERE incoming.JobId=$trial AND incoming.Kind='trial' AND NOT EXISTS " +
            "(SELECT 1 FROM query_snapshot s WHERE (s.TrialCreatedUtc,s.TrialQueueOrder,s.TrialId) > " +
            "(incoming.CreatedUtc,incoming.QueueOrder,incoming.JobId))";
        command.Parameters.AddWithValue("$trial", snapshot.TrialId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return;
        var created = reader.GetString(0);
        var order = reader.GetDouble(1);
        reader.Close();
        Replace(connection, transaction, snapshot with { TrialCreatedUtc = created, TrialQueueOrder = order });
    }

    /// <summary>Replaces the snapshot within the caller's evidence publication transaction.</summary>
    public static void Replace(SqliteConnection connection, SqliteTransaction transaction, Stored snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var graphemes = JsonSerializer.Serialize(snapshot.Snapshot.Graphemes);
        var encoded = snapshot.Snapshot.Forms.Select(row => JsonSerializer.Serialize(row)).ToArray();
        var encodedStems = (snapshot.Snapshot.Stems ?? []).Select(row => JsonSerializer.Serialize(row)).ToArray();
        var encodedUses = (snapshot.Snapshot.Uses ?? []).Select(row => JsonSerializer.Serialize(row)).ToArray();
        var encodedObjects = (snapshot.Snapshot.Objects ?? []).Select(row => JsonSerializer.Serialize(row)).ToArray();
        var size = encoded.Sum(EstimatedBytes) + EstimatedBytes(graphemes) +
            encodedStems.Sum(CompactBytes) + encodedUses.Sum(CompactBytes) + encodedObjects.Sum(CompactBytes);
        if (size > MaximumBytes) throw new InvalidDataException("The query snapshot exceeds the 100 MB limit.");
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        Clear(connection, transaction);
        command.CommandText = "INSERT INTO query_snapshot VALUES (1,$trial,$grammar,$bundle,$ws,$graphemes,$unresolved,$ticks,$created,$order,$coverage)";
        command.Parameters.AddWithValue("$trial", snapshot.TrialId);
        command.Parameters.AddWithValue("$grammar", snapshot.GrammarHash);
        command.Parameters.AddWithValue("$bundle", snapshot.BundleId);
        command.Parameters.AddWithValue("$ws", snapshot.WritingSystem);
        command.Parameters.AddWithValue("$graphemes", graphemes);
        command.Parameters.AddWithValue("$unresolved", snapshot.Snapshot.UnresolvedVariants);
        command.Parameters.AddWithValue("$ticks", snapshot.SourceWriteTicks);
        command.Parameters.AddWithValue("$created", snapshot.TrialCreatedUtc);
        command.Parameters.AddWithValue("$order", snapshot.TrialQueueOrder);
        command.Parameters.AddWithValue("$coverage", (snapshot.Snapshot.Coverage ?? QueryCoverage.Complete).ToJson());
        command.ExecuteNonQuery();
        command.Parameters.Clear();
        command.CommandText = "INSERT INTO query_forms VALUES ($ordinal,$row)";
        var ordinal = command.Parameters.Add("$ordinal", SqliteType.Integer);
        var rowValue = command.Parameters.Add("$row", SqliteType.Text);
        for (var i = 0; i < encoded.Length; i++)
        {
            ordinal.Value = i;
            rowValue.Value = encoded[i];
            command.ExecuteNonQuery();
        }
        command.CommandText = "INSERT INTO query_uses VALUES ($ordinal,$kind,$row)";
        var kind = command.Parameters.Add("$kind", SqliteType.Text);
        for (var i = 0; i < encodedUses.Length; i++)
        {
            ordinal.Value = i;
            kind.Value = snapshot.Snapshot.Uses![i].Values[0][0];
            rowValue.Value = encodedUses[i];
            command.ExecuteNonQuery();
        }
        command.Parameters.Remove(kind);
        foreach (var (table, rows) in new[] { ("query_stems", encodedStems), ("query_objects", encodedObjects) })
        {
            command.CommandText = $"INSERT INTO {table} VALUES ($ordinal,$row)";
            for (var i = 0; i < rows.Length; i++)
            {
                ordinal.Value = i;
                rowValue.Value = rows[i];
                command.ExecuteNonQuery();
            }
        }
    }

    // Rows number in the tens of thousands, so they are read without the general-purpose deserializer.
    private static QueryRow ParseRow(string json)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        string? id = null;
        var values = new List<string[]>();
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) throw new InvalidDataException("Invalid query row.");
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var name = reader.GetString();
            reader.Read();
            if (name == "Id") id = reader.GetString();
            else if (name == "Values" && reader.TokenType == JsonTokenType.StartArray)
                while (reader.Read() && reader.TokenType == JsonTokenType.StartArray)
                {
                    reader.Read();
                    if (reader.TokenType != JsonTokenType.String) { values.Add([]); continue; }
                    var first = reader.GetString()!;
                    reader.Read();
                    if (reader.TokenType != JsonTokenType.String) { values.Add([first]); continue; }
                    var column = new List<string> { first };
                    do column.Add(reader.GetString()!); while (reader.Read() && reader.TokenType == JsonTokenType.String);
                    values.Add([.. column]);
                }
            else throw new InvalidDataException("Invalid query row.");
        }
        return new QueryRow(id ?? throw new InvalidDataException("Invalid query row."), values);
    }

    private static long EstimatedBytes(string json) => (long)Encoding.UTF8.GetByteCount(json) * 3 + 384;

    // Positional rows carry no field names.
    private static long CompactBytes(string json) => (long)Encoding.UTF8.GetByteCount(json) + 96;

    /// <summary>Returns the current Trial and bundle identities without decoding their rows.</summary>
    public static (string TrialId, string BundleId)? ReadIdentity(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT TrialId, BundleId FROM query_snapshot WHERE Id=1";
        using var reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetString(0), reader.GetString(1)) : null;
    }

    /// <summary>Reads the current snapshot in the caller's read transaction, or null when no evidence job published one.</summary>
    /// <param name="connection">The open store connection.</param>
    /// <param name="transaction">The caller's read transaction.</param>
    /// <param name="sources">The sources to decode (see <see cref="QuerySources"/>); null decodes all of them.</param>
    /// <param name="useTargetKinds">The <c>target-kind</c> values the uses source is limited to; null keeps every kind.</param>
    public static Stored? Read(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyCollection<string>? sources = null,
        IReadOnlyCollection<string>? useTargetKinds = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT TrialId,GrammarHash,BundleId,WritingSystem,GraphemesJson,UnresolvedVariants,SourceWriteTicks,TrialCreatedUtc,TrialQueueOrder,CoverageJson FROM query_snapshot WHERE Id=1";
        string trial, hash, bundle, ws;
        string[] graphemes;
        int unresolved;
        long sourceTicks;
        string trialCreated;
        double trialOrder;
        QueryCoverage coverage;
        long bytes;
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read()) return null;
            trial = reader.GetString(0); hash = reader.GetString(1); bundle = reader.GetString(2); ws = reader.GetString(3);
            bytes = EstimatedBytes(reader.GetString(4));
            graphemes = JsonSerializer.Deserialize<string[]>(reader.GetString(4)) ?? throw new InvalidDataException("Invalid graphemes.");
            unresolved = reader.GetInt32(5);
            sourceTicks = reader.GetInt64(6);
            trialCreated = reader.GetString(7);
            trialOrder = reader.GetDouble(8);
            coverage = QueryCoverage.FromJson(reader.GetString(9));
        }
        bool Wants(string source) => sources is null || sources.Contains(source);
        var rows = new List<QueryForm>();
        if (Wants(QuerySources.Forms))
        {
            command.CommandText = "SELECT RowJson FROM query_forms ORDER BY Ordinal";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var json = reader.GetString(0);
                bytes += EstimatedBytes(json);
                if (bytes > MaximumBytes) throw new InvalidDataException("The query snapshot exceeds the 100 MB limit.");
                rows.Add(JsonSerializer.Deserialize<QueryForm>(json) ?? throw new InvalidDataException("Invalid form row."));
            }
        }
        List<QueryRow> ReadRows(string source, string table)
        {
            var list = new List<QueryRow>();
            if (!Wants(source)) return list;
            var limited = table == "query_uses" && useTargetKinds is not null;
            command.Parameters.Clear();
            command.CommandText = limited
                ? $"SELECT RowJson FROM {table} WHERE TargetKind IN ({string.Join(",", useTargetKinds!.Select((_, i) => "$k" + i))}) ORDER BY Ordinal"
                : $"SELECT RowJson FROM {table} ORDER BY Ordinal";
            if (limited) { var n = 0; foreach (var value in useTargetKinds!) command.Parameters.AddWithValue("$k" + n++, value); }
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var json = reader.GetString(0);
                bytes += CompactBytes(json);
                if (bytes > MaximumBytes) throw new InvalidDataException("The query snapshot exceeds the 100 MB limit.");
                list.Add(ParseRow(json));
            }
            return list;
        }
        var stems = ReadRows(QuerySources.Stems, "query_stems");
        var uses = ReadRows(QuerySources.Uses, "query_uses");
        var objects = ReadRows(QuerySources.Objects, "query_objects");
        return new Stored(trial, hash, bundle, ws, new QueryFactsReader.Snapshot(rows, graphemes, unresolved, stems, uses, objects, coverage), sourceTicks, trialCreated, trialOrder);
    }
}
