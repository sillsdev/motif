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
        command.CommandText = "DELETE FROM query_forms; DELETE FROM query_stems; DELETE FROM query_snapshot;";
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
        var size = encodedStems.Sum(EstimatedBytes) + encoded.Sum(EstimatedBytes) + EstimatedBytes(graphemes);
        if (size > MaximumBytes) throw new InvalidDataException("The query snapshot exceeds the 100 MB limit.");
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        Clear(connection, transaction);
        command.CommandText = "INSERT INTO query_snapshot VALUES (1,$trial,$grammar,$bundle,$ws,$graphemes,$unresolved,$ticks,$created,$order)";
        command.Parameters.AddWithValue("$trial", snapshot.TrialId);
        command.Parameters.AddWithValue("$grammar", snapshot.GrammarHash);
        command.Parameters.AddWithValue("$bundle", snapshot.BundleId);
        command.Parameters.AddWithValue("$ws", snapshot.WritingSystem);
        command.Parameters.AddWithValue("$graphemes", graphemes);
        command.Parameters.AddWithValue("$unresolved", snapshot.Snapshot.UnresolvedVariants);
        command.Parameters.AddWithValue("$ticks", snapshot.SourceWriteTicks);
        command.Parameters.AddWithValue("$created", snapshot.TrialCreatedUtc);
        command.Parameters.AddWithValue("$order", snapshot.TrialQueueOrder);
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
        command.CommandText = "INSERT INTO query_stems VALUES ($ordinal,$row)";
        for (var i = 0; i < encodedStems.Length; i++)
        {
            ordinal.Value = i;
            rowValue.Value = encodedStems[i];
            command.ExecuteNonQuery();
        }
    }

    private static long EstimatedBytes(string json) => (long)Encoding.UTF8.GetByteCount(json) * 3 + 384;

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
    public static Stored? Read(SqliteConnection connection, SqliteTransaction transaction, bool includeStems = true)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT TrialId,GrammarHash,BundleId,WritingSystem,GraphemesJson,UnresolvedVariants,SourceWriteTicks,TrialCreatedUtc,TrialQueueOrder FROM query_snapshot WHERE Id=1";
        string trial, hash, bundle, ws;
        string[] graphemes;
        int unresolved;
        long sourceTicks;
        string trialCreated;
        double trialOrder;
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
        }
        command.CommandText = "SELECT RowJson FROM query_forms ORDER BY Ordinal";
        var rows = new List<QueryForm>();
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var json = reader.GetString(0);
                bytes += EstimatedBytes(json);
                if (bytes > MaximumBytes) throw new InvalidDataException("The query snapshot exceeds the 100 MB limit.");
                rows.Add(JsonSerializer.Deserialize<QueryForm>(json) ?? throw new InvalidDataException("Invalid form row."));
            }
        command.CommandText = "SELECT RowJson FROM query_stems ORDER BY Ordinal";
        var stems = new List<QueryFactsReader.Stem>();
        if (includeStems) using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var json = reader.GetString(0);
                bytes += EstimatedBytes(json);
                if (bytes > MaximumBytes) throw new InvalidDataException("The query snapshot exceeds the 100 MB limit.");
                stems.Add(JsonSerializer.Deserialize<QueryFactsReader.Stem>(json) ?? throw new InvalidDataException("Invalid stem row."));
            }
        return new Stored(trial, hash, bundle, ws, new QueryFactsReader.Snapshot(rows, graphemes, unresolved, stems), sourceTicks, trialCreated, trialOrder);
    }
}
